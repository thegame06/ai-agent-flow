using System.Globalization;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;

namespace AgentFlow.Api.Collections;

/// <summary>What the collections assistant knows about the customer it is talking to.</summary>
public sealed record CollectionsCustomerContext
{
    public bool Found { get; init; }
    public string? CustomerName { get; init; }
    public string? FirstName { get; init; }
    public string Currency { get; init; } = "MXN";
    public decimal OverdueBalance { get; init; }
    public decimal TotalBalance { get; init; }
    public int MaxDaysPastDue { get; init; }
    public string? Stage { get; init; }
    public IReadOnlyList<CollectionsLoanBrief> Loans { get; init; } = [];
    public CollectionsPromiseBrief? PendingPromise { get; init; }
    /// <summary>The last campaign message sent to this customer, so the assistant knows why it wrote.</summary>
    public string? LastCampaignMessage { get; init; }
    public string? LastCampaignName { get; init; }
    public string? CurrentOutcome { get; init; }
    /// <summary>Today in the customer's timezone ("2026-11-27 (viernes)"): models don't know the date.</summary>
    public string Today { get; init; } = string.Empty;
    public string? Note { get; init; }
    /// <summary>Set when an advisor handled this conversation and gave it back to the assistant.</summary>
    public string? Conversation { get; init; }
    /// <summary>
    /// How this customer pays (the lender's methods and the customer's references). Always in the
    /// context: with it only behind a separate tool, the model kept saying it had no payment data.
    /// </summary>
    public PaymentInstructions? HowToPay { get; init; }
}

public sealed record CollectionsLoanBrief(
    string LoanId,
    string? Product,
    decimal OverdueBalance,
    decimal TotalBalance,
    int DaysPastDue,
    string? NextPaymentDate,
    decimal? NextPaymentAmount,
    string Status);

public sealed record CollectionsPromiseBrief(decimal? Amount, string PromisedDate, string Status);

public sealed record CollectionsAssistantActionResult(bool Ok, string Message);

/// <summary>
/// Backs the collections assistant's tools. Everything is keyed by the customer's phone (the
/// WhatsApp sender), so the assistant can only see and act on the person it is talking to.
/// </summary>
public interface ICollectionsAssistantService
{
    Task<CollectionsCustomerContext> GetContextAsync(string tenantId, string phone, CancellationToken ct = default, string? sessionId = null);
    /// <param name="whenText">What the customer said about the date ("el lunes"); resolved here, preferred over <paramref name="promisedDate"/>.</param>
    Task<CollectionsAssistantActionResult> RegisterPromiseAsync(string tenantId, string phone, decimal? amount, DateTimeOffset? promisedDate, string? whenText, string? notes, CancellationToken ct = default);
    Task<CollectionsAssistantActionResult> RecordOutcomeAsync(string tenantId, string phone, string outcome, string? notes, CancellationToken ct = default);
    /// <summary>How this customer can pay: the lender's payment methods and the customer's own references.</summary>
    Task<PaymentInstructions> GetPaymentInstructionsAsync(string tenantId, string phone, CancellationToken ct = default);
    /// <summary>The customer says they already paid: saved for a person to verify.</summary>
    Task<CollectionsAssistantActionResult> ReportPaymentAsync(string tenantId, string phone, decimal? amount, string? whenText, string? reference, string? method, string? notes, CancellationToken ct = default, string? sessionId = null);
    Task<bool> AttachReceiptsAsync(string tenantId, string partyId, IReadOnlyList<string> mediaIds, CancellationToken ct = default);
    /// <param name="sessionId">The live conversation: it waits for a person and the assistant stops answering.</param>
    Task<CollectionsAssistantActionResult> HandoffAsync(string tenantId, string phone, string reason, CancellationToken ct = default, string? sessionId = null);
}

public sealed class CollectionsAssistantService : ICollectionsAssistantService
{
    private const string Actor = "collections-assistant";
    private static readonly TimeSpan ContactWindow = TimeSpan.FromDays(30);

    private readonly ICollectionsStore _collections;
    private readonly ICollectionPromiseService _promises;
    private readonly ICampaignStore _campaigns;
    private readonly ICampaignOutcomeService _outcomes;
    private readonly AgentFlow.Api.Connect.IConversationHandoffService? _handoff;
    private readonly ICollectionPaymentDeskStore? _paymentDesk;

    public CollectionsAssistantService(
        ICollectionsStore collections,
        ICollectionPromiseService promises,
        ICampaignStore campaigns,
        ICampaignOutcomeService outcomes,
        AgentFlow.Api.Connect.IConversationHandoffService? handoff = null,
        ICollectionPaymentDeskStore? paymentDesk = null,
        AgentFlow.Domain.Repositories.IChannelSessionRepository? sessions = null)
    {
        _sessions = sessions;
        _handoff = handoff;
        _paymentDesk = paymentDesk;
        _collections = collections;
        _promises = promises;
        _campaigns = campaigns;
        _outcomes = outcomes;
    }

    public async Task<CollectionsCustomerContext> GetContextAsync(string tenantId, string phone, CancellationToken ct = default, string? sessionId = null)
    {
        var context = await BuildContextAsync(tenantId, phone, ct);
        if (context.Found && _paymentDesk is not null)
            context = context with { HowToPay = CollectionPaymentDesk.ForAssistant(await GetPaymentInstructionsAsync(tenantId, phone, ct)) };
        if (string.IsNullOrWhiteSpace(sessionId) || _handoff is null) return context;
        return context with { Conversation = await _handoff.GetAssistantNoteAsync(tenantId, sessionId!, ct) };
    }

    private async Task<CollectionsCustomerContext> BuildContextAsync(string tenantId, string phone, CancellationToken ct)
    {
        var (partyId, loans) = await FindCustomerAsync(tenantId, phone, ct);
        var contact = await LatestContactAsync(tenantId, phone, ct);
        var campaign = contact is null ? null : await _campaigns.GetCampaignAsync(tenantId, contact.CampaignId, ct);
        var today = Today(campaign?.Timezone);
        if (partyId is null)
            return new CollectionsCustomerContext { Found = false, Today = today, Note = "No hay prestamos registrados para este numero." };

        var open = loans.Where(l => l.Status is not CollectionLoanStatus.PaidOff).ToList();
        var name = loans.Select(l => l.CustomerName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        var pending = (await _collections.GetPromisesAsync(tenantId, CollectionPromiseStatus.Pending, partyId, 5, ct)).FirstOrDefault();
        var maxDpd = open.Count == 0 ? 0 : open.Max(l => l.DaysPastDue);
        var campaignName = campaign?.Name;

        return new CollectionsCustomerContext
        {
            Found = true,
            CustomerName = name,
            FirstName = FirstNameOf(name),
            Currency = loans.Select(l => l.Currency).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "MXN",
            OverdueBalance = open.Sum(l => l.OverdueBalance),
            TotalBalance = open.Sum(l => l.TotalBalance),
            MaxDaysPastDue = maxDpd,
            Stage = DelinquencyStages.Label(DelinquencyStages.FromDaysPastDue(maxDpd)),
            Loans = open.Select(l => new CollectionsLoanBrief(
                l.LoanExternalId, l.Product, l.OverdueBalance, l.TotalBalance, l.DaysPastDue,
                l.NextPaymentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), l.NextPaymentAmount,
                l.Status.ToString())).ToList(),
            PendingPromise = pending is null
                ? null
                : new CollectionsPromiseBrief(pending.Amount, pending.PromisedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), pending.Status.ToString()),
            LastCampaignMessage = contact?.FollowUps?.LastOrDefault(f => f.Status == "sent")?.Message ?? contact?.RenderedMessage,
            LastCampaignName = campaignName,
            CurrentOutcome = contact?.Outcome,
            Today = today
        };
    }

    public async Task<CollectionsAssistantActionResult> RegisterPromiseAsync(string tenantId, string phone, decimal? amount, DateTimeOffset? promisedDate, string? whenText, string? notes, CancellationToken ct = default)
    {
        if (amount is < 0) return new(false, "El monto no puede ser negativo.");

        var contact = await LatestContactAsync(tenantId, phone, ct);
        var campaign = contact is null ? null : await _campaigns.GetCampaignAsync(tenantId, contact.CampaignId, ct);
        var localNow = CampaignContactPolicy.ToLocal(DateTimeOffset.UtcNow, campaign?.Timezone ?? "America/Mexico_City");

        // Models get weekday arithmetic wrong; resolve "el lunes" / "manana" deterministically.
        var resolved = CampaignReplyClassifier.ResolveDate(whenText, localNow);
        // A vague answer ("en diciembre", "pronto") is not a date: never let the model fill one in.
        if (resolved is null && !string.IsNullOrWhiteSpace(whenText))
            return new(false, $"\"{whenText}\" no es una fecha concreta: pregunta al cliente que dia exacto.");
        resolved ??= promisedDate;
        if (resolved is null) return new(false, "No entendi la fecha: pide al cliente un dia concreto.");
        var date = new DateTimeOffset(resolved.Value.Year, resolved.Value.Month, resolved.Value.Day, 0, 0, 0, TimeSpan.Zero);
        if (date.Date < localNow.Date) return new(false, "La fecha prometida ya paso: pide una fecha a partir de hoy.");
        var promisedDateValue = date;
        var confirmation = $"Promesa registrada para el {Weekdays[(int)date.DayOfWeek]} {date:dd/MM/yyyy}" +
                           (amount is > 0 ? $" por {amount.Value.ToString("#,##0.00", CultureInfo.InvariantCulture)}." : ".");

        if (contact is not null)
        {
            // Through the campaign outcome so the campaign results and the promise stay in sync.
            await _outcomes.RecordAsync(tenantId, contact.Id, new CampaignOutcomeRequest(
                CampaignOutcomes.PromiseToPay, CampaignOutcomes.Sources.Agent, Actor, notes, amount, promisedDateValue), ct);
            return new(true, confirmation);
        }

        var (partyId, _) = await FindCustomerAsync(tenantId, phone, ct);
        if (partyId is null) return new(false, "No encontre prestamos para este cliente.");
        await _promises.CreateAsync(tenantId, partyId, amount, promisedDateValue, CampaignOutcomes.Sources.Agent, Actor, notes, ct: ct);
        return new(true, confirmation);
    }

    public async Task<CollectionsAssistantActionResult> RecordOutcomeAsync(string tenantId, string phone, string outcome, string? notes, CancellationToken ct = default)
    {
        if (CampaignOutcomes.Find(outcome) is null)
            return new(false, $"Resultado desconocido: {outcome}.");
        if (outcome == CampaignOutcomes.PromiseToPay)
            return new(false, "Para una promesa usa la herramienta de promesa de pago con fecha y monto.");

        var contact = await LatestContactAsync(tenantId, phone, ct);
        if (contact is null) return new(false, "Este cliente no tiene un contacto de campana reciente.");
        await _outcomes.RecordAsync(tenantId, contact.Id, new CampaignOutcomeRequest(outcome, CampaignOutcomes.Sources.Agent, Actor, notes), ct);
        return new(true, "Resultado registrado.");
    }

    public async Task<PaymentInstructions> GetPaymentInstructionsAsync(string tenantId, string phone, CancellationToken ct = default)
    {
        var (partyId, loans) = await FindCustomerAsync(tenantId, phone, ct);
        var settings = _paymentDesk is null ? null : await _paymentDesk.GetSettingsAsync(tenantId, ct);
        var instructions = CollectionPaymentDesk.BuildInstructions(settings, loans);
        return partyId is null && instructions.Configured
            ? instructions with { Message = "No hay prestamos registrados para este numero: da los medios de pago pero no inventes una referencia; si la necesita, pasa a un asesor." }
            : instructions;
    }

    public async Task<CollectionsAssistantActionResult> ReportPaymentAsync(string tenantId, string phone, decimal? amount, string? whenText, string? reference, string? method, string? notes, CancellationToken ct = default, string? sessionId = null)
    {
        if (amount is <= 0) return new(false, "El monto debe ser mayor a cero; si no lo dijo, omitelo.");
        var (partyId, loans) = await FindCustomerAsync(tenantId, phone, ct);
        if (partyId is null) return new(false, "No encontre prestamos para este cliente: pasa el caso a un asesor.");

        var contact = await LatestContactAsync(tenantId, phone, ct);
        var campaign = contact is null ? null : await _campaigns.GetCampaignAsync(tenantId, contact.CampaignId, ct);
        var localNow = CampaignContactPolicy.ToLocal(DateTimeOffset.UtcNow, campaign?.Timezone ?? "America/Mexico_City");
        var paidOn = CollectionPaymentDesk.ResolvePastDate(whenText, localNow);
        var open = loans.Where(l => l.Status is not CollectionLoanStatus.PaidOff).ToList();
        string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var saved = new CollectionPaymentReportDocument
        {
            TenantId = tenantId,
            PartyId = partyId,
            CustomerName = loans.Select(l => l.CustomerName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
            Phone = phone,
            Amount = amount,
            Currency = loans.Select(l => l.Currency).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "MXN",
            PaidOnText = Clean(whenText),
            PaidOn = paidOn,
            Reference = Clean(reference),
            Method = Clean(method),
            Notes = Clean(notes),
            LoanId = open.Count == 1 ? open[0].Id : null,
            ContactExecutionId = contact?.Id,
            Source = CampaignOutcomes.Sources.Agent,
        };
        if (_paymentDesk is not null)
        {
            // A receipt already confirmed is not a new payment (a customer repeating themselves, or the
            // assistant repeating its last action on a later "hola").
            if (saved.Reference is not null)
            {
                var all = await _paymentDesk.GetReportsAsync(tenantId, null, ct: ct);
                var confirmed = CollectionPaymentDesk.AlreadyConfirmed(all, saved);
                if (confirmed is not null)
                {
                    // A photo sent now is still evidence of that payment: keep it with the confirmed report.
                    var late = await TakeRecentReceiptsAsync(tenantId, sessionId, ct);
                    if (late.Count > 0)
                    {
                        CollectionPaymentDesk.Complete(confirmed, new CollectionPaymentReportDocument { TenantId = tenantId, PartyId = confirmed.PartyId, ReceiptMediaIds = late });
                        await _paymentDesk.UpdateReportAsync(confirmed, ct);
                    }
                    return new(true, $"Ese pago (comprobante {confirmed.Reference}) ya estaba registrado y confirmado: no lo reportes de nuevo; solo dile al cliente que ya esta aplicado.");
                }
            }

            // The date or receipt the customer sends afterwards belongs to the payment they already
            // reported: complete that report instead of adding a second one for the same money.
            saved.ReceiptMediaIds = await TakeRecentReceiptsAsync(tenantId, sessionId, ct);
            var pending = await _paymentDesk.GetReportsAsync(tenantId, CollectionPaymentReportStatus.Pending, ct: ct);
            var existing = CollectionPaymentDesk.PendingToComplete(pending, saved, DateTimeOffset.UtcNow);
            if (existing is not null)
            {
                CollectionPaymentDesk.Complete(existing, saved);
                await _paymentDesk.UpdateReportAsync(existing, ct);
                saved = existing;
            }
            else
            {
                await _paymentDesk.InsertReportAsync(saved, ct);
            }
        }

        // The campaign result is "already paid" right away; the money counts once a person confirms it.
        if (contact is not null && contact.Outcome != CampaignOutcomes.AlreadyPaid)
        {
            var summary = string.Join(" ", new[]
            {
                amount is > 0 ? $"Monto {amount.Value.ToString("#,##0.00", CultureInfo.InvariantCulture)}." : null,
                Clean(reference) is { } r ? $"Ref. {r}." : null
            }.Where(x => x is not null));
            await _outcomes.RecordAsync(tenantId, contact.Id, new CampaignOutcomeRequest(
                CampaignOutcomes.AlreadyPaid, CampaignOutcomes.Sources.Agent, Actor, summary.Length == 0 ? null : summary), ct);
        }

        var missing = new[]
        {
            saved.Amount is null ? "monto" : null,
            saved.PaidOn is null ? "fecha" : null,
            saved.Reference is null ? "numero de referencia o comprobante" : null
        }.Where(x => x is not null).ToList();
        return new(true, missing.Count == 0
            ? "Pago reportado; un asesor lo verificara."
            : $"Pago reportado; un asesor lo verificara. Faltan: {string.Join(", ", missing)}. Si no los dio, pideselos una sola vez; si los da, vuelve a reportarlo (se completa el mismo reporte).");
    }

    public async Task<CollectionsAssistantActionResult> HandoffAsync(string tenantId, string phone, string reason, CancellationToken ct = default, string? sessionId = null)
    {
        var why = string.IsNullOrWhiteSpace(reason) ? "El asistente pide un asesor" : reason.Trim();
        var paused = !string.IsNullOrWhiteSpace(sessionId) && _handoff is not null &&
                     await _handoff.RequestAsync(tenantId, sessionId!, why, Actor, ct);

        var contact = await LatestContactAsync(tenantId, phone, ct);
        if (contact is null)
            return paused ? new(true, "Caso pasado a un asesor.") : new(false, "Este cliente no tiene un contacto de campana reciente.");
        await _campaigns.UpdateContactExecutionAsync(contact with
        {
            NeedsAttention = true,
            AttentionReason = why,
            AttentionAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);
        return new(true, "Caso pasado a un asesor.");
    }

    private static readonly string[] Weekdays = ["domingo", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado"];

    private static string Today(string? timezone)
    {
        var local = CampaignContactPolicy.ToLocal(DateTimeOffset.UtcNow, timezone ?? "America/Mexico_City");
        return $"{local:yyyy-MM-dd} ({Weekdays[(int)local.DayOfWeek]})";
    }

    private readonly AgentFlow.Domain.Repositories.IChannelSessionRepository? _sessions;

    /// <summary>
    /// A photo or PDF from a customer who reported a payment in the last day goes straight to that
    /// pending report, whatever the assistant does with the message. Returns whether it was attached.
    /// </summary>
    public async Task<bool> AttachReceiptsAsync(string tenantId, string partyId, IReadOnlyList<string> mediaIds, CancellationToken ct = default)
    {
        if (_paymentDesk is null || mediaIds.Count == 0) return false;
        var now = DateTimeOffset.UtcNow;
        var report = (await _paymentDesk.GetReportsAsync(tenantId, CollectionPaymentReportStatus.Pending, ct: ct))
            .Where(r => r.PartyId == partyId && now - r.CreatedAt < ReceiptWindow)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault();
        if (report is null) return false;
        CollectionPaymentDesk.Complete(report, new CollectionPaymentReportDocument { TenantId = tenantId, PartyId = partyId, ReceiptMediaIds = mediaIds.ToList() });
        await _paymentDesk.UpdateReportAsync(report, ct);
        return true;
    }

    /// <summary>How long a file the customer sent still counts as the receipt of the payment they report.</summary>
    public static readonly TimeSpan ReceiptWindow = TimeSpan.FromHours(24);

    /// <summary>The files the customer sent in this conversation in the last day; each is attached to one report only.</summary>
    private async Task<List<string>> TakeRecentReceiptsAsync(string tenantId, string? sessionId, CancellationToken ct)
    {
        if (_sessions is null || string.IsNullOrWhiteSpace(sessionId)) return [];
        var session = await _sessions.GetByIdAsync(sessionId!, tenantId, ct);
        if (session?.Metadata.GetValueOrDefault(AgentFlow.Api.Channels.Provenance.LastMediaIdsKey) is not { Length: > 0 } ids) return [];
        var at = DateTimeOffset.TryParse(session.Metadata.GetValueOrDefault(AgentFlow.Api.Channels.Provenance.LastMediaAtKey),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTimeOffset.MinValue;
        if (DateTimeOffset.UtcNow - at > ReceiptWindow) return [];
        session.Metadata.Remove(AgentFlow.Api.Channels.Provenance.LastMediaIdsKey);
        await _sessions.UpdateAsync(session, ct);
        return ids.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cliente", "usuario", "titular", "unknown", "desconocido", "sin", "n/a", "na", "prueba", "test", "sr", "sra", "señor", "señora"
    };

    /// <summary>The customer's first name for a greeting, or null when the record holds no real name ("Cliente Prueba").</summary>
    public static string? FirstNameOf(string? fullName)
    {
        var first = fullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim(',', '.');
        return string.IsNullOrWhiteSpace(first) || first.Any(char.IsDigit) || GenericNames.Contains(first) ? null : first;
    }

    private async Task<CampaignContactExecutionContract?> LatestContactAsync(string tenantId, string phone, CancellationToken ct)
    {
        var key = RecipientKeys.From(phone);
        return key is null ? null : await _campaigns.FindLatestContactByRecipientKeyAsync(tenantId, key, DateTimeOffset.UtcNow - ContactWindow, ct);
    }

    private async Task<(string? PartyId, IReadOnlyList<CollectionLoanDocument> Loans)> FindCustomerAsync(string tenantId, string phone, CancellationToken ct)
    {
        var key = RecipientKeys.From(phone);
        if (key is null) return (null, []);

        // The campaign contact knows the party; otherwise match the phone in the portfolio.
        var contact = await LatestContactAsync(tenantId, phone, ct);
        if (!string.IsNullOrWhiteSpace(contact?.PartyId))
        {
            var byParty = await _collections.GetLoansByPartyAsync(tenantId, contact.PartyId!, ct);
            if (byParty.Count > 0) return (contact.PartyId, byParty);
        }

        var loans = (await _collections.GetLoansAsync(tenantId, ct))
            .Where(l => RecipientKeys.From(l.CustomerPhone) == key)
            .ToList();
        return loans.Count == 0 ? (null, []) : (loans[0].PartyId, loans);
    }
}
