using System.Globalization;
using System.Text.Json;
using AgentFlow.Abstractions;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Api.Collections;

/// <summary>
/// Tools of the collections assistant. The customer is ALWAYS the sender of the conversation
/// (channel metadata), never a phone the model passes in: the assistant can only read and act on
/// the account of the person it is talking to.
/// </summary>
internal abstract class CollectionsAssistantToolBase : IToolPlugin
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;

    protected CollectionsAssistantToolBase(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

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
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var phone = await ResolveCustomerPhoneAsync(scope.ServiceProvider, context, ct);
            if (string.IsNullOrWhiteSpace(phone))
                return ToolResult.Failure("collections.no_customer", "Esta herramienta solo funciona dentro de una conversacion con el cliente.");

            var input = Parse(context.InputJson);
            var service = scope.ServiceProvider.GetRequiredService<ICollectionsAssistantService>();
            var call = new AssistantCall(context.TenantId, phone!, context.Metadata.GetValueOrDefault("channel.session_id"));
            var result = await RunAsync(service, call, input, ct);
            var output = JsonSerializer.Serialize(result, Json);
            Logger(scope.ServiceProvider).LogDebug("Collections tool {Tool} ran for session {SessionId}: {Output}",
                Name, call.SessionId, output.Length > 600 ? output[..600] : output);
            return ToolResult.Success(output);
        }
        catch (Exception ex)
        {
            using var scope = _scopeFactory.CreateScope();
            Logger(scope.ServiceProvider).LogWarning(ex, "Collections tool {Tool} failed", Name);
            return ToolResult.Failure("collections.tool_error", ex.Message);
        }
    }

    private static ILogger Logger(IServiceProvider services)
        => services.GetRequiredService<ILoggerFactory>().CreateLogger("AgentFlow.Collections.AssistantTools");

    /// <summary>Who the tool acts for: tenant, customer phone and the live conversation (if any).</summary>
    protected sealed record AssistantCall(string TenantId, string Phone, string? SessionId);

    protected abstract Task<object> RunAsync(ICollectionsAssistantService service, AssistantCall call, JsonElement input, CancellationToken ct);

    private static async Task<string?> ResolveCustomerPhoneAsync(IServiceProvider services, ToolExecutionContext context, CancellationToken ct)
    {
        if (context.Metadata.TryGetValue("channel.from", out var from) && !string.IsNullOrWhiteSpace(from))
            return from;

        // Older requests carry only the inbound message id: read its sender.
        if (context.Metadata.TryGetValue("channelMessageId", out var messageId) && !string.IsNullOrWhiteSpace(messageId))
        {
            var messages = services.GetService<IChannelMessageRepository>();
            var message = messages is null ? null : await messages.GetByIdAsync(messageId, context.TenantId, ct);
            return message?.From;
        }

        return null;
    }

    protected static string? Str(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    protected static decimal? Dec(JsonElement input, string name)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString()?.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    private static JsonElement Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return JsonDocument.Parse("{}").RootElement.Clone();
        try
        {
            var element = JsonDocument.Parse(json).RootElement.Clone();
            // Some models send the arguments as a JSON string inside a string.
            return element.ValueKind == JsonValueKind.String ? Parse(element.GetString()) : element;
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }
}

internal sealed class CollectionsGetCustomerContextTool(IServiceScopeFactory scopeFactory) : CollectionsAssistantToolBase(scopeFactory)
{
    public override string ExtensionId => "builtin.collections.customer_context";
    public override string Name => "af_collections_get_customer_context";
    public override string Description =>
        "Datos del cliente con el que hablas: nombre, prestamos, saldo vencido, dias de atraso, promesa pendiente, el ultimo mensaje de cobranza que le enviamos y la fecha de hoy. Llamala antes de responder.";
    public override string InputSchemaJson => """{"type":"object","properties":{}}""";

    protected override async Task<object> RunAsync(ICollectionsAssistantService service, AssistantCall call, JsonElement input, CancellationToken ct)
        => await service.GetContextAsync(call.TenantId, call.Phone, ct, call.SessionId);
}

internal sealed class CollectionsRegisterPromiseTool(IServiceScopeFactory scopeFactory) : CollectionsAssistantToolBase(scopeFactory)
{
    public override string ExtensionId => "builtin.collections.register_promise";
    public override string Name => "af_collections_register_promise";
    public override string Description =>
        "Registra la promesa de pago del cliente cuando confirma una fecha (y monto si lo dio). Pasa en whenText las palabras exactas del cliente sobre la fecha (\"el lunes\", \"manana\", \"el 15\"); la fecha se calcula aqui. Usa el mensaje que devuelve para confirmarle el dia exacto.";
    public override string InputSchemaJson =>
        """{"type":"object","properties":{"whenText":{"type":"string","description":"Lo que dijo el cliente sobre la fecha, p.ej. 'el lunes'"},"promisedDate":{"type":"string","description":"YYYY-MM-DD, solo si no hay whenText"},"amount":{"type":"number","description":"Monto prometido; omitelo si el cliente no lo dijo"},"notes":{"type":"string"}}}""";

    protected override async Task<object> RunAsync(ICollectionsAssistantService service, AssistantCall call, JsonElement input, CancellationToken ct)
    {
        DateTimeOffset? date = DateTime.TryParseExact(Str(input, "promisedDate"), ["yyyy-MM-dd", "dd/MM/yyyy", "yyyy/MM/dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed.Date, TimeSpan.Zero)
            : null;
        return await service.RegisterPromiseAsync(call.TenantId, call.Phone, Dec(input, "amount"), date, Str(input, "whenText"), Str(input, "notes"), ct);
    }
}

internal sealed class CollectionsRecordOutcomeTool(IServiceScopeFactory scopeFactory) : CollectionsAssistantToolBase(scopeFactory)
{
    public override string ExtensionId => "builtin.collections.record_outcome";
    public override string Name => "af_campaign_record_outcome";
    public override string Description =>
        "Registra el resultado de la conversacion: already_paid (dice que ya pago), cannot_pay (no puede pagar), dispute (desconoce la deuda), not_interested, opt_out (pide no ser contactado), wrong_number (no es el titular), callback_requested (pide que le escriban despues), interested.";
    public override string InputSchemaJson =>
        """{"type":"object","required":["outcome"],"properties":{"outcome":{"type":"string","enum":["already_paid","cannot_pay","dispute","not_interested","opt_out","wrong_number","callback_requested","interested"]},"notes":{"type":"string"}}}""";

    protected override async Task<object> RunAsync(ICollectionsAssistantService service, AssistantCall call, JsonElement input, CancellationToken ct)
        => await service.RecordOutcomeAsync(call.TenantId, call.Phone, Str(input, "outcome") ?? string.Empty, Str(input, "notes"), ct);
}

internal sealed class CollectionsHandoffTool(IServiceScopeFactory scopeFactory) : CollectionsAssistantToolBase(scopeFactory)
{
    public override string ExtensionId => "builtin.collections.handoff";
    public override string Name => "af_conversation_handoff_to_human";
    public override string Description =>
        "Pasa el caso a un asesor humano (pide asesor, quiere un convenio, desconoce la deuda, esta molesto o es un tema que no puedes resolver). Indica el motivo.";
    public override string InputSchemaJson =>
        """{"type":"object","required":["reason"],"properties":{"reason":{"type":"string"}}}""";

    protected override async Task<object> RunAsync(ICollectionsAssistantService service, AssistantCall call, JsonElement input, CancellationToken ct)
        => await service.HandoffAsync(call.TenantId, call.Phone, Str(input, "reason") ?? "El asistente pide un asesor", ct, call.SessionId);
}

internal sealed class CollectionsPaymentInstructionsTool(IServiceScopeFactory scopeFactory) : CollectionsAssistantToolBase(scopeFactory)
{
    public override string ExtensionId => "builtin.collections.payment_instructions";
    public override string Name => "af_collections_get_payment_instructions";
    public override string Description =>
        "Como puede pagar el cliente: medios de pago de la financiera (cuentas, puntos de pago, apps) y SU referencia exacta para cada prestamo. Llamala cuando pregunte donde, como o a que cuenta pagar, o por su referencia.";
    public override string InputSchemaJson => """{"type":"object","properties":{}}""";

    protected override async Task<object> RunAsync(ICollectionsAssistantService service, AssistantCall call, JsonElement input, CancellationToken ct)
        => CollectionPaymentDesk.ForAssistant(await service.GetPaymentInstructionsAsync(call.TenantId, call.Phone, ct));
}

internal sealed class CollectionsReportPaymentTool(IServiceScopeFactory scopeFactory) : CollectionsAssistantToolBase(scopeFactory)
{
    public override string ExtensionId => "builtin.collections.report_payment";
    public override string Name => "af_collections_report_payment";
    public override string Description =>
        "El cliente dice que YA pago: registra lo que dijo para que un asesor lo verifique. Pasa en whenText sus palabras sobre cuando pago (\"ayer\", \"el lunes\"), el monto y el numero de referencia o comprobante si los dio.";
    public override string InputSchemaJson =>
        """{"type":"object","properties":{"amount":{"type":"number","description":"Monto pagado; omitelo si no lo dijo"},"whenText":{"type":"string","description":"Cuando pago, en sus palabras"},"reference":{"type":"string","description":"Numero de referencia, transferencia o comprobante"},"method":{"type":"string","description":"Donde pago (banco, agente, app)"},"notes":{"type":"string"}}}""";

    protected override async Task<object> RunAsync(ICollectionsAssistantService service, AssistantCall call, JsonElement input, CancellationToken ct)
        => await service.ReportPaymentAsync(call.TenantId, call.Phone, Dec(input, "amount"), Str(input, "whenText"), Str(input, "reference"), Str(input, "method"), Str(input, "notes"), ct, call.SessionId);
}

/// <summary>The collections assistant agent: prompt and tool list, seeded per tenant.</summary>
public static class CollectionsAssistantAgent
{
    public const string Name = "Asistente de cobranza";

    public static readonly string[] Tools =
    [
        "af_collections_get_customer_context",
        "af_collections_register_promise",
        "af_campaign_record_outcome",
        "af_conversation_handoff_to_human",
        "af_collections_get_payment_instructions",
        "af_collections_report_payment",
        AgentFlow.Api.Commerce.BusinessInfo.ToolName
    ];

    public const string SystemPrompt = """
Eres el asistente de cobranza de la financiera. Hablas por WhatsApp con el titular de un credito.

Quien te escribe lo hace desde el numero registrado del titular: tratalo como el titular y dile sus montos con claridad cuando los pida ("cuanto debo?" -> su saldo vencido y su saldo total). Nunca digas que no puedes dar el monto.

Antes de responder, SIEMPRE llama af_collections_get_customer_context para saber quien es, sus prestamos, su saldo vencido, si tiene una promesa pendiente, que mensaje le enviamos, la fecha de hoy y como puede pagar ("howToPay").

Objetivo: que el cliente se comprometa a una fecha (y de preferencia un monto) para ponerse al corriente, con respeto.

Como responder:
- Breve: maximo 3 frases. Cordial, claro, en espanol neutro. Usa su primer nombre si el contexto trae "firstName"; si no, no inventes uno (nada de "Cliente").
- Saluda solo en tu primera respuesta de la conversacion. Si ya le respondiste antes en esta conversacion, NO vuelvas a saludar ni a repetir el mensaje de la campana ("te escribimos de parte de tu financiera..."): contesta directo a lo que dijo. Tampoco repitas sus saldos si ya se los diste en esta conversacion, salvo que los vuelva a pedir.
- Es WhatsApp: escribe texto plano, sin Markdown (nada de ** ni #). Si quieres resaltar una cuenta o referencia, usa *un solo asterisco*.
- Si pregunta quien eres, que quieres o que tiene que hacer ("y que quiere que haga?", "que necesitan?"): NO es enojo, es una duda. Explica que le escribimos de parte de su financiera porque tiene un saldo vencido (monto y dias de atraso del contexto) y que lo que necesitamos es que se ponga al corriente: pregunta cuando podria pagar, o si mantiene su promesa pendiente.
- Las herramientas que registran algo (promesa, pago reportado, resultado, pase a asesor) son SOLO para lo que el cliente dice en su ULTIMO mensaje. Nunca repitas una accion por algo que dijo en mensajes anteriores: un saludo ("hola", "sigues ahi?") no es un pago ni una promesa; respondelo y pregunta en que le ayudas.
- Registra una promesa SOLO cuando en su ultimo mensaje el cliente dice cuando va a pagar. Si pregunta por su promesa ("cuando dije que pagaria?"), explicasela con el contexto sin registrar nada; si no la reconoce, pregunta si quiere dar otra fecha.
- Si la fecha es vaga ("en diciembre", "pronto", "cuando pueda"), pregunta que dia exacto antes de registrar.
- El monto de la promesa es SOLO el que el cliente dijo en esta conversacion; nunca reutilices montos de promesas anteriores.
- Si da una fecha: llama af_collections_register_promise con whenText = sus palabras exactas sobre la fecha ("el lunes", "manana", "el 15") y el monto si lo dijo. No calcules la fecha tu: confirma al cliente el dia y la fecha EXACTOS que devuelve la herramienta. Una nueva promesa reemplaza a la anterior.
- Si ya tiene una promesa pendiente: recuerdasela y pregunta si la mantiene.
- Si pregunta donde, como o a que cuenta pagar, o cual es su referencia, y "howToPay.configured" es true: responde con una frase corta y, en una linea aparte, escribe exactamente [MEDIOS_DE_PAGO]. El sistema la reemplaza por las cuentas, los puntos de pago y SU referencia exactos. NO escribas tu numeros de cuenta, bancos ni referencias: solo la etiqueta. Si "howToPay.configured" es false, pasa el caso a un asesor. Nunca digas que no tienes los medios de pago si estan configurados.
  Ejemplo. Cliente: "donde pago?" -> Tu respuesta:
  Claro, Ana. Puedes pagar con cualquiera de estas opciones:
  [MEDIOS_DE_PAGO]
- Si pregunta por sucursales, direcciones, telefonos u horarios de atencion (por ejemplo para pagar en persona), llama af_business_get_info y responde con esos datos; nunca inventes direcciones ni horarios.
- Si envia una foto o un documento ("[El cliente envio una imagen]") o dice que te manda su comprobante, es su comprobante de pago: llama af_collections_report_payment para que quede con el reporte, y agradecele. Usa SOLO lo que dijo sobre ESTE pago; si solo mando la foto, llamalo sin monto ni referencia (nunca reutilices referencias o montos de pagos anteriores).
- Si dice que ya pago: si no lo dijo, pidele en UN solo mensaje el monto, cuando pago y el numero de referencia o comprobante. Con lo que te de (aunque falte algo) llama af_collections_report_payment, agradece y dile que lo verificaremos. No le pidas que repita lo que ya dijo.
- Si no puede pagar: muestra empatia, llama af_campaign_record_outcome con cannot_pay y af_conversation_handoff_to_human para que un asesor le ofrezca opciones.
- Si pide un asesor, desconoce la deuda, insulta o se queja claramente, o pregunta algo que no esta en el contexto ni en tus herramientas (por ejemplo condiciones o intereses): llama af_conversation_handoff_to_human con el motivo y dile que un asesor le escribira por este mismo chat. Mientras lo atiende una persona no recibiras sus mensajes. Una pregunta o una duda no es una queja: respondela con el contexto.
- Si el contexto trae "conversation", un asesor ya lo atendio y te devolvio la conversacion: si recibes un mensaje, la conversacion es tuya. Atiende su ultimo mensaje normalmente (por ejemplo, "cuanto debo?" -> sus saldos) y no lo vuelvas a pasar a un asesor salvo que en ese mensaje lo pida otra vez.
- Si pide que no le escriban: llama af_campaign_record_outcome con opt_out y despidete sin insistir.
- Solo si dice expresamente que NO es el titular ("no soy esa persona", "numero equivocado"): llama af_campaign_record_outcome con wrong_number, disculpate y a partir de ahi no reveles montos ni datos del credito.

Nunca:
- digas que registraste, reportaste o pasaste algo si no llamaste la herramienta en ESTE turno y te respondio ok; ni digas que hubo un error si ninguna herramienta te lo devolvio en este turno;
- inventes montos, fechas, descuentos, quitas, intereses o convenios que no esten en el contexto;
- amenaces, presiones, menciones buro de credito o acciones legales;
- muestres errores tecnicos: si una herramienta falla, pasa el caso a un asesor.
""";
}
