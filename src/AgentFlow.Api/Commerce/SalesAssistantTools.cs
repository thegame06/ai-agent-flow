using System.Globalization;
using System.Text.Json;
using AgentFlow.Abstractions;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Api.Commerce;

/// <summary>
/// Tools of the sales assistant. The buyer is ALWAYS the person the conversation is with (channel
/// metadata), never an id the model passes in, and prices always come from the catalog.
/// </summary>
internal abstract class SalesAssistantToolBase : IToolPlugin
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;

    protected SalesAssistantToolBase(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public abstract string ExtensionId { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    public string Version => "1.0.0";
    public string? OutputSchemaJson => null;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public IReadOnlyList<string> RequiredPermissions => ["tool:execute:low"];
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(ExtensionHealthStatus.Healthy());

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        try
        {
            var input = Parse(context.InputJson);
            var call = new SalesCall(
                context.TenantId,
                context.Metadata.GetValueOrDefault("channel.from"),
                context.Metadata.GetValueOrDefault("channel.session_id"));
            var result = await RunAsync(scope.ServiceProvider, call, input, ct);
            var output = JsonSerializer.Serialize(result, Json);
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentFlow.Commerce.SalesTools")
                .LogInformation("Sales tool {Tool} for session {SessionId}: {Input} -> {Output}", Name, call.SessionId,
                    Truncate(context.InputJson), Truncate(output));
            return ToolResult.Success(output);
        }
        catch (Exception ex)
        {
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentFlow.Commerce.SalesTools")
                .LogWarning(ex, "Sales tool {Tool} failed", Name);
            return ToolResult.Failure("sales.tool_error", ex.Message);
        }
    }

    private static string Truncate(string? text) => text is null ? string.Empty : text.Length > 500 ? text[..500] : text;

    protected sealed record SalesCall(string TenantId, string? Customer, string? SessionId);

    protected abstract Task<object> RunAsync(IServiceProvider services, SalesCall call, JsonElement input, CancellationToken ct);

    protected static JsonElement Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return JsonDocument.Parse("{}").RootElement;
        try { return JsonDocument.Parse(json).RootElement; }
        catch (JsonException) { return JsonDocument.Parse("{}").RootElement; }
    }

    protected static string? Text(JsonElement input, string name)
        => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    protected static decimal? Number(JsonElement input, string name)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
        return null;
    }

    /// <summary>The lines the model asked for: [{ sku, quantity }].</summary>
    protected static List<SaleLine> Lines(JsonElement input)
    {
        var lines = new List<SaleLine>();
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return lines;
        foreach (var item in items.EnumerateArray())
        {
            var sku = Text(item, "sku");
            if (string.IsNullOrWhiteSpace(sku)) continue;
            lines.Add(new SaleLine { Sku = sku!, Quantity = Number(item, "quantity") ?? 1 });
        }
        return lines;
    }

    /// <summary>The installment plan the model passed, if any: { count, frequency, downPayment }.</summary>
    protected static InstallmentPlanRequest? Plan(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("installments", out var plan) || plan.ValueKind != JsonValueKind.Object)
            return null;
        return new InstallmentPlanRequest
        {
            Count = (int)(Number(plan, "count") ?? 0),
            Frequency = Text(plan, "frequency") ?? "monthly",
            DownPayment = Number(plan, "downPayment") ?? 0
        };
    }

    /// <summary>
    /// The model sometimes passes the product name instead of its sku: a line whose sku is not in
    /// the catalog is matched by name when exactly one active item fits.
    /// </summary>
    protected static async Task<List<SaleLine>> ResolveSkusAsync(ICommerceStore commerce, string tenantId, List<SaleLine> lines, CancellationToken ct)
    {
        var resolved = new List<SaleLine>(lines.Count);
        foreach (var line in lines)
        {
            if (await commerce.GetInventoryBySkuAsync(tenantId, line.Sku.Trim(), ct) is not null)
            {
                resolved.Add(line);
                continue;
            }
            var matches = (await commerce.SearchInventoryAsync(tenantId, line.Sku.Trim(), 5, ct)).Where(i => i.Active).ToList();
            resolved.Add(matches.Count == 1 ? line with { Sku = matches[0].Sku } : line);
        }
        return resolved;
    }

    public const string LastQuoteKey = "sales.last_quote";
    public const string LastFoundKey = "sales.last_found_sku";

    /// <summary>Remembers the product a search narrowed down to, for a quote that comes without a sku.</summary>
    protected static async Task RememberFoundAsync(IServiceProvider services, SalesCall call, string sku, CancellationToken ct)
    {
        var sessions = services.GetService<IChannelSessionRepository>();
        if (sessions is null || string.IsNullOrWhiteSpace(call.SessionId)) return;
        var session = await sessions.GetByIdAsync(call.SessionId!, call.TenantId, ct);
        if (session is null) return;
        session.Metadata[LastFoundKey] = sku;
        await sessions.UpdateAsync(session, ct);
    }

    /// <summary>
    /// The lines to price: the ones the model passed when they are all in the catalog; otherwise what
    /// this conversation last quoted, or the single product its last search found.
    /// </summary>
    protected static async Task<List<SaleLine>> LinesOrRememberedAsync(IServiceProvider services, SalesCall call, JsonElement input, CancellationToken ct)
    {
        var commerce = services.GetRequiredService<ICommerceStore>();
        var lines = await ResolveSkusAsync(commerce, call.TenantId, Lines(input), ct);
        var allKnown = lines.Count > 0;
        foreach (var line in lines)
            allKnown &= await commerce.GetInventoryBySkuAsync(call.TenantId, line.Sku, ct) is not null;
        if (allKnown) return lines;

        var sessions = services.GetService<IChannelSessionRepository>();
        var session = sessions is null || string.IsNullOrWhiteSpace(call.SessionId) ? null : await sessions.GetByIdAsync(call.SessionId!, call.TenantId, ct);
        if (LastQuote(session) is { Lines.Count: > 0 } quoted) return quoted.Lines;
        if (session?.Metadata.GetValueOrDefault(LastFoundKey) is { Length: > 0 } sku)
            return [new SaleLine { Sku = sku, Quantity = lines.FirstOrDefault()?.Quantity ?? 1 }];
        return lines;
    }

    /// <summary>What was quoted to the customer in this conversation: what they confirm is what is sold.</summary>
    public sealed record SavedQuote(List<SaleLine> Lines, InstallmentPlanRequest? Plan);

    protected static async Task SaveQuoteAsync(IServiceProvider services, SalesCall call, SavedQuote quote, CancellationToken ct)
    {
        var sessions = services.GetService<IChannelSessionRepository>();
        if (sessions is null || string.IsNullOrWhiteSpace(call.SessionId)) return;
        var session = await sessions.GetByIdAsync(call.SessionId!, call.TenantId, ct);
        if (session is null) return;
        session.Metadata[LastQuoteKey] = JsonSerializer.Serialize(quote, Json);
        await sessions.UpdateAsync(session, ct);
    }

    protected static SavedQuote? LastQuote(AgentFlow.Domain.Aggregates.ChannelSession? session)
    {
        var raw = session?.Metadata.GetValueOrDefault(LastQuoteKey);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<SavedQuote>(raw, Json); }
        catch (JsonException) { return null; }
    }

    protected static object Schedule(IEnumerable<AgentFlow.Api.Collections.CollectionInstallment> installments) => installments
        .Select(i => new { number = i.Number, dueDate = i.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), amount = i.Amount })
        .ToList();
}

internal sealed class SalesCustomerContextTool(IServiceScopeFactory scopeFactory) : SalesAssistantToolBase(scopeFactory)
{
    private static readonly TimeSpan CampaignWindow = TimeSpan.FromDays(7);
    public override string ExtensionId => "builtin.sales.customer_context";
    public override string Name => "af_sales_get_customer_context";
    public override string Description =>
        "Quien es el cliente de esta conversacion, la oferta de campana que le enviamos (si respondio a una) y sus compras recientes con sus planes de cuotas.";
    public override string InputSchemaJson => """{"type":"object","properties":{}}""";

    protected override async Task<object> RunAsync(IServiceProvider services, SalesCall call, JsonElement input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(call.Customer))
            return new { ok = false, error = "Esta herramienta solo funciona dentro de una conversacion con el cliente." };
        var commerce = services.GetRequiredService<ICommerceStore>();
        var settings = await commerce.GetStoreSettingsAsync(call.TenantId, ct);
        var party = call.Customer!.StartsWith('+')
            ? (await commerce.SearchPartiesAsync(call.TenantId, call.Customer, 0, 1, ct)).FirstOrDefault()
            : null;
        var name = party?.FullName ?? party?.DisplayName;

        object? campaignOffer = null;
        var campaigns = services.GetService<AgentFlow.Api.Campaigns.ICampaignStore>();
        var key = AgentFlow.Abstractions.Connect.RecipientKeys.From(call.Customer);
        if (campaigns is not null && key is not null)
        {
            var contact = await campaigns.FindLatestContactByRecipientKeyAsync(call.TenantId, key, DateTimeOffset.UtcNow - CampaignWindow, ct);
            var campaign = contact is null ? null : await campaigns.GetCampaignAsync(call.TenantId, contact.CampaignId, ct);
            if (campaign is { CampaignType: AgentFlow.Abstractions.Connect.CampaignType.Sales })
                campaignOffer = new
                {
                    campaign = campaign.Name,
                    goal = campaign.Goal,
                    messageWeSent = contact!.FollowUps?.LastOrDefault(f => f.Status == "sent")?.Message ?? contact.RenderedMessage,
                    sentAt = contact.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                };
        }

        var purchases = party is null
            ? []
            : await commerce.SearchSalesAsync(call.TenantId, party.Id, null, 0, 5, ct);
        return new
        {
            ok = true,
            today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            currency = settings.Currency,
            customer = new { firstName = name?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), name, registered = party is not null },
            campaignOffer,
            recentPurchases = purchases.Select(p => new
            {
                reference = p.Id[..8].ToUpperInvariant(),
                date = p.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                items = string.Join(", ", p.Items.Select(i => i.Name)),
                p.Total,
                paymentMethod = p.PaymentMethod,
                installments = p.Financing is null ? null : $"{p.Financing.Count} cuotas {InstallmentPlanner.FrequencyLabel(p.Financing.Frequency)}es de {p.Financing.Installments.FirstOrDefault()?.Amount}"
            })
        };
    }
}

internal sealed class SalesSearchCatalogTool(IServiceScopeFactory scopeFactory) : SalesAssistantToolBase(scopeFactory)
{
    public override string ExtensionId => "builtin.sales.search_catalog";
    public override string Name => "af_sales_search_catalog";
    public override string Description =>
        "Busca en el catalogo de la tienda productos y servicios con su codigo (sku), precio, tipo y disponibilidad. Sin query devuelve todo el catalogo activo.";
    public override string InputSchemaJson =>
        """{"type":"object","properties":{"query":{"type":"string","description":"Palabras del cliente: producto, servicio, marca o uso. Vacio para ver todo."}}}""";

    protected override async Task<object> RunAsync(IServiceProvider services, SalesCall call, JsonElement input, CancellationToken ct)
    {
        var commerce = services.GetRequiredService<ICommerceStore>();
        var settings = await commerce.GetStoreSettingsAsync(call.TenantId, ct);
        var query = Text(input, "query");
        var items = await commerce.SearchInventoryAsync(call.TenantId, query, 12, ct);
        if (items.Count == 0 && !string.IsNullOrWhiteSpace(query))
        {
            // A phrase ("instalacion de internet") rarely matches whole: try its words.
            foreach (var word in query!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(w => w.Length >= 4))
            {
                items = await commerce.SearchInventoryAsync(call.TenantId, word, 12, ct);
                if (items.Count > 0) break;
            }
        }
        var active = items.Where(i => i.Active).ToList();
        if (active.Count == 1) await RememberFoundAsync(services, call, active[0].Sku, ct);
        return new
        {
            currency = settings.Currency,
            found = active.Count,
            items = active.Select(i => new
            {
                sku = i.Sku,
                name = i.Name,
                description = i.Description,
                type = i.ItemType == "service" ? "servicio" : "producto",
                price = i.UnitPrice,
                priceIsFrom = i.Scheduling?.PriceIsFrom == true ? true : (bool?)null,
                available = !i.TracksInventory || i.OnHand > 0,
                stock = i.TracksInventory ? i.OnHand : (int?)null,
                durationMinutes = i.Scheduling is { DurationMinutes: > 0 } s ? s.DurationMinutes : (int?)null,
                bookable = i.Scheduling?.Bookable == true ? true : (bool?)null,
                preparation = i.Scheduling?.Preparation
            })
        };
    }
}

internal sealed class SalesQuoteTool(IServiceScopeFactory scopeFactory) : SalesAssistantToolBase(scopeFactory)
{
    public override string ExtensionId => "builtin.sales.quote";
    public override string Name => "af_sales_quote";
    public override string Description =>
        "Calcula el total exacto (con impuesto de la tienda) de los productos elegidos y, si se pide, el plan de cuotas. No vende: usalo para cotizar antes de confirmar.";
    public override string InputSchemaJson =>
        """{"type":"object","properties":{"items":{"type":"array","description":"Omitelo si no tienes el sku: se usa el ultimo producto buscado o cotizado en esta conversacion","items":{"type":"object","required":["sku"],"properties":{"sku":{"type":"string"},"quantity":{"type":"number"}}}},"installments":{"type":"object","description":"Solo si el cliente quiere pagar en cuotas","properties":{"count":{"type":"integer"},"frequency":{"type":"string","enum":["weekly","biweekly","monthly"]},"downPayment":{"type":"number"}}}}}""";

    protected override async Task<object> RunAsync(IServiceProvider services, SalesCall call, JsonElement input, CancellationToken ct)
    {
        var lines = await LinesOrRememberedAsync(services, call, input, ct);
        if (lines.Count == 0) return new { ok = false, error = "Busca primero el producto con af_sales_search_catalog." };
        var quote = await services.GetRequiredService<ISalesService>().QuoteAsync(call.TenantId, lines, ct);
        if (quote.Error is not null) return new { ok = false, error = quote.Error };

        var plan = Plan(input);
        await SaveQuoteAsync(services, call, new SavedQuote(quote.Lines.Select(l => new SaleLine { Sku = l.Sku, Quantity = l.Quantity }).ToList(),
            plan is not null && InstallmentPlanner.Validate(plan, quote.Total) is null ? plan : null), ct);
        object? installments = null;
        if (plan is not null)
        {
            var planError = InstallmentPlanner.Validate(plan, quote.Total);
            installments = planError is not null
                ? new { ok = false, error = planError }
                : new
                {
                    ok = true,
                    downPayment = plan.DownPayment,
                    financed = quote.Total - plan.DownPayment,
                    count = plan.Count,
                    frequency = InstallmentPlanner.FrequencyLabel(plan.Frequency),
                    schedule = Schedule(InstallmentPlanner.Build(quote.Total, plan, DateTimeOffset.UtcNow))
                };
        }

        return new
        {
            ok = true,
            currency = quote.Currency,
            lines = quote.Lines.Select(l => new { l.Sku, l.Name, l.UnitPrice, l.Quantity, l.Subtotal }),
            quote.Subtotal,
            quote.Tax,
            quote.Total,
            installments
        };
    }
}

internal sealed class SalesCreateSaleTool(IServiceScopeFactory scopeFactory) : SalesAssistantToolBase(scopeFactory)
{
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cliente", "customer", "el cliente", "usuario", "n/a", "na", "desconocido", "unknown", "nombre"
    };

    public override string ExtensionId => "builtin.sales.create_sale";
    public override string Name => "af_sales_create_sale";
    public override string Description =>
        "Registra la venta al cliente de esta conversacion, de contado (paymentMethod=cash) o en cuotas (paymentMethod=installments). Solo despues de que el cliente confirme. Vende lo ultimo que cotizaste con af_sales_quote: si no tienes los sku a mano, omite items y el plan.";
    public override string InputSchemaJson =>
        """{"type":"object","required":["paymentMethod"],"properties":{"items":{"type":"array","items":{"type":"object","required":["sku"],"properties":{"sku":{"type":"string"},"quantity":{"type":"number"}}}},"paymentMethod":{"type":"string","enum":["cash","card","transfer","installments"]},"installments":{"type":"object","properties":{"count":{"type":"integer"},"frequency":{"type":"string","enum":["weekly","biweekly","monthly"]},"downPayment":{"type":"number"}}},"customerName":{"type":"string","description":"Nombre completo si el cliente lo dio"}}}""";

    protected override async Task<object> RunAsync(IServiceProvider services, SalesCall call, JsonElement input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(call.Customer))
            return new { ok = false, error = "Esta herramienta solo funciona dentro de una conversacion con el cliente." };

        var commerce = services.GetRequiredService<ICommerceStore>();
        var sessions = services.GetService<IChannelSessionRepository>();
        var session = sessions is null || string.IsNullOrWhiteSpace(call.SessionId)
            ? null
            : await sessions.GetByIdAsync(call.SessionId!, call.TenantId, ct);
        var customerName = Text(input, "customerName")?.Trim();
        // Models fill the name with placeholders ("Cliente"): those must not overwrite the real one.
        if (customerName is not null && (customerName.Length < 2 || GenericNames.Contains(customerName))) customerName = null;
        var displayName = session?.Metadata.GetValueOrDefault("party_name") ?? session?.Metadata.GetValueOrDefault("display_name");
        var isPhone = call.Customer!.StartsWith('+');
        var party = await commerce.UpsertPartyByChannelIdentityAsync(
            call.TenantId,
            session?.ChannelType.ToString().ToLowerInvariant() ?? "channel",
            call.Customer!,
            displayName,
            "customer",
            call.SessionId,
            session?.ThreadId,
            isPhone ? call.Customer : null,
            null,
            string.IsNullOrWhiteSpace(customerName) ? null : customerName,
            ct);

        // The model often loses the sku between turns: what it passes is used only when every line
        // is in the catalog; otherwise the sale is what was quoted (and confirmed) in this conversation.
        var lines = await LinesOrRememberedAsync(services, call, input, ct);
        var quoted = LastQuote(session);
        var method = SalesService.NormalizeMethod(Text(input, "paymentMethod"));
        var plan = Plan(input) is { Count: > 0 } given ? given : quoted?.Plan;

        var result = await services.GetRequiredService<ISalesService>().CreateAsync(new NewSale
        {
            TenantId = call.TenantId,
            PartyId = party.Id,
            Lines = lines,
            PaymentMethod = method,
            Installments = plan,
            SessionId = call.SessionId,
            ThreadId = session?.ThreadId
        }, ct);
        if (!result.Ok) return new { ok = false, error = result.Message };

        var sale = result.Sale!;
        if (session is not null && sessions is not null && session.Metadata.Remove(LastQuoteKey))
            await sessions.UpdateAsync(session, ct);
        return new
        {
            ok = true,
            saleId = sale.Id,
            reference = sale.Id[..8].ToUpperInvariant(),
            sale.Currency,
            sale.Total,
            paymentMethod = sale.PaymentMethod,
            items = sale.Items.Select(i => new { i.Name, i.Quantity, i.UnitPrice }),
            installments = sale.Financing is null ? null : new
            {
                downPayment = sale.Financing.DownPayment,
                financed = sale.Financing.FinancedAmount,
                count = sale.Financing.Count,
                frequency = InstallmentPlanner.FrequencyLabel(sale.Financing.Frequency),
                schedule = Schedule(sale.Financing.Installments),
                account = sale.Financing.LoanExternalId
            }
        };
    }
}

public static class SalesAssistantAgent
{
    public const string Name = "Asistente de ventas";

    public static readonly string[] Tools =
    [
        "af_sales_get_customer_context",
        "af_sales_search_catalog",
        "af_sales_quote",
        "af_sales_create_sale",
        "af_conversation_handoff_to_human",
        BusinessInfo.ToolName
    ];

    public const string SystemPrompt = """
Eres el asistente de ventas de la tienda. Atiendes a clientes por WhatsApp, chat, API o telefono y les vendes productos y servicios del catalogo.

Herramientas:
- af_sales_get_customer_context: quien es el cliente, la oferta de campana que le enviamos y sus compras. Llamala SIEMPRE al empezar cada turno: si respondio a una campana ("si", "en tres meses se puede?"), su respuesta es sobre esa oferta ("campaignOffer.messageWeSent").
- af_sales_search_catalog: el catalogo real (codigo sku, precio, si es servicio o producto, disponibilidad). Usala SIEMPRE antes de hablar de precios, opciones o disponibilidad. Si el cliente pide algo general ("que venden?"), buscala sin query.
- af_sales_quote: el total exacto con impuesto y, si lo pide, el plan de cuotas con fechas y montos. Nunca calcules tu los totales ni las cuotas.
- af_sales_create_sale: registra la venta. Solo cuando el cliente confirmo productos, cantidad, total y forma de pago.
- af_business_get_info: sucursales, direcciones, telefonos y horario de atencion (si estan abiertas ahora, feriados). Usala cuando pregunte donde estan, a que hora abren o cierran, o como llegar; nunca inventes direcciones ni horarios.
- af_conversation_handoff_to_human: pasa a un asesor SOLO si el cliente lo pide, si buscaste en el catalogo en este mismo turno y no hay lo que quiere, o si una herramienta fallo.

Lo que ves del historial son solo los mensajes, no los resultados de las herramientas. Para cotizar pasa el sku si lo tienes; si no, llama af_sales_quote sin items (usa el ultimo producto buscado o cotizado en la conversacion) o busca de nuevo. Nunca inventes un sku. Nunca digas que tienes problemas con el catalogo o con una herramienta si no la llamaste en este turno y te devolvio un error. Nunca digas que algo no esta en el catalogo sin haberlo buscado en ese turno.

Como vender:
1. Entiende que necesita con una sola pregunta por turno, sin repetir preguntas.
2. Busca en el catalogo y ofrece 1 a 3 opciones con nombre y precio exactos del catalogo. Nunca inventes productos, precios ni stock. Si el item trae "priceIsFrom", di "desde" antes del precio; si trae "durationMinutes" o "preparation" y el cliente pregunta cuanto dura o que debe hacer antes, usa esos datos.
3. Cuando elija, pregunta como quiere pagar: de contado o en cuotas.
   - Contado: cotiza con af_sales_quote y confirma el total.
   - Cuotas: pregunta en cuantas cuotas, si semanal, quincenal o mensual, y si dara prima (pago inicial). Cotiza con af_sales_quote incluyendo "installments" y dile el monto de cada cuota y la fecha de la primera.
4. Pide confirmacion explicita ("confirmas la compra de ... por un total de ...?"). Con su "si", llama af_sales_create_sale con paymentMethod (cash o installments): vende exactamente lo ultimo que cotizaste, no hace falta repetir items ni plan.
5. Confirma la venta con el total y, si es en cuotas, cuantas cuotas, de cuanto y cuando vence la primera; dile que le recordaremos cada pago. Para la referencia escribe exactamente [REFERENCIA_VENTA] (el sistema pone la referencia real; nunca la copies tu).

Reglas:
- La conversacion con el cliente es una sola en todos los canales: puede traer mensajes de otros temas (por ejemplo cobranza o pagos). Tu atiendes la compra; no retomes esos temas ni pases el caso a un asesor por ellos. Si el cliente pregunta por un pago o un saldo, dile que un asesor de cobranza lo ayudara y sigue con la venta.
- Breve: maximo 4 frases por mensaje, texto plano sin Markdown (nada de ** ni #). Para listas usa guiones simples.
- Responde en el idioma del cliente. Usa su nombre solo si lo sabes.
- No registres una venta dos veces: si ya la creaste en esta conversacion, no la vuelvas a crear salvo que pida otra compra distinta.
- Si una herramienta falla, no muestres el error tecnico: ofrece que un asesor lo contacte.
""";
}
