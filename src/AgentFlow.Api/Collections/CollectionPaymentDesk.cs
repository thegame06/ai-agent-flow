using System.Globalization;
using System.Text.RegularExpressions;
using AgentFlow.Api.Campaigns;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Collections;

// ----------------------------------------------------------------------
// How customers pay (configured per lender) and payments customers say they made.

/// <summary>One way to pay: a bank account, a cash network, an app...</summary>
public sealed class CollectionPaymentMethod
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>bank_transfer, cash, app or other.</summary>
    public string Type { get; set; } = "bank_transfer";
    /// <summary>Bank or network name ("BAC", "Agentes Banpro").</summary>
    public string Name { get; set; } = string.Empty;
    public string? AccountNumber { get; set; }
    public string? AccountHolder { get; set; }
    public string? AccountType { get; set; }
    /// <summary>Only offered to customers whose loans are in this currency; empty = any.</summary>
    public string? Currency { get; set; }
    /// <summary>What the customer must do ("Indica tu referencia en el concepto").</summary>
    public string? Instructions { get; set; }
    public bool Enabled { get; set; } = true;
}

[BsonIgnoreExtraElements]
public sealed class CollectionPaymentSettingsDocument
{
    /// <summary>The tenant id: one document per lender.</summary>
    [BsonId] public string TenantId { get; set; } = string.Empty;
    public List<CollectionPaymentMethod> Methods { get; set; } = [];
    /// <summary>What the customer quotes when paying: loan (loan number) or customer (customer number).</summary>
    public string ReferenceKind { get; set; } = "loan";
    /// <summary>Said after the options ("Envia foto del comprobante por este chat").</summary>
    public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;
}

public enum CollectionPaymentReportStatus
{
    Pending,
    Confirmed,
    Rejected
}

/// <summary>A payment the customer says they made; a person confirms it (it becomes a payment) or rejects it.</summary>
[BsonIgnoreExtraElements]
public sealed class CollectionPaymentReportDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string PartyId { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = "MXN";
    /// <summary>The customer's words about when ("ayer", "el 20").</summary>
    public string? PaidOnText { get; set; }
    public DateTimeOffset? PaidOn { get; set; }
    /// <summary>Receipt or transfer number the customer gave.</summary>
    public string? Reference { get; set; }
    /// <summary>Photos or PDFs of the receipt the customer sent (media store ids).</summary>
    public List<string> ReceiptMediaIds { get; set; } = [];
    public string? Method { get; set; }
    public string? Notes { get; set; }
    /// <summary>The loan the payment is for, when the customer has only one open loan or said which.</summary>
    public string? LoanId { get; set; }
    public string? ContactExecutionId { get; set; }
    public string Source { get; set; } = "agent";
    public CollectionPaymentReportStatus Status { get; set; } = CollectionPaymentReportStatus.Pending;
    public string? PaymentId { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public interface ICollectionPaymentDeskStore
{
    Task<CollectionPaymentSettingsDocument?> GetSettingsAsync(string tenantId, CancellationToken ct = default);
    Task SaveSettingsAsync(CollectionPaymentSettingsDocument settings, CancellationToken ct = default);
    Task InsertReportAsync(CollectionPaymentReportDocument report, CancellationToken ct = default);
    Task UpdateReportAsync(CollectionPaymentReportDocument report, CancellationToken ct = default);
    Task<CollectionPaymentReportDocument?> GetReportAsync(string tenantId, string reportId, CancellationToken ct = default);
    Task<IReadOnlyList<CollectionPaymentReportDocument>> GetReportsAsync(string tenantId, CollectionPaymentReportStatus? status, int limit = 200, CancellationToken ct = default);
}

public sealed class MongoCollectionPaymentDeskStore : ICollectionPaymentDeskStore
{
    private readonly IMongoCollection<CollectionPaymentSettingsDocument> _settings;
    private readonly IMongoCollection<CollectionPaymentReportDocument> _reports;

    public MongoCollectionPaymentDeskStore(IMongoDatabase database)
    {
        _settings = database.GetCollection<CollectionPaymentSettingsDocument>("collection_payment_settings");
        _reports = database.GetCollection<CollectionPaymentReportDocument>("collection_payment_reports");
        _reports.Indexes.CreateOne(new CreateIndexModel<CollectionPaymentReportDocument>(
            Builders<CollectionPaymentReportDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.Status).Descending(x => x.CreatedAt)));
    }

    public async Task<CollectionPaymentSettingsDocument?> GetSettingsAsync(string tenantId, CancellationToken ct = default)
        => await _settings.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync(ct);

    public Task SaveSettingsAsync(CollectionPaymentSettingsDocument settings, CancellationToken ct = default)
        => _settings.ReplaceOneAsync(x => x.TenantId == settings.TenantId, settings, new ReplaceOptions { IsUpsert = true }, ct);

    public Task InsertReportAsync(CollectionPaymentReportDocument report, CancellationToken ct = default)
        => _reports.InsertOneAsync(report, cancellationToken: ct);

    public Task UpdateReportAsync(CollectionPaymentReportDocument report, CancellationToken ct = default)
        => _reports.ReplaceOneAsync(x => x.Id == report.Id && x.TenantId == report.TenantId, report, cancellationToken: ct);

    public async Task<CollectionPaymentReportDocument?> GetReportAsync(string tenantId, string reportId, CancellationToken ct = default)
        => await _reports.Find(x => x.TenantId == tenantId && x.Id == reportId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CollectionPaymentReportDocument>> GetReportsAsync(string tenantId, CollectionPaymentReportStatus? status, int limit = 200, CancellationToken ct = default)
    {
        var filter = Builders<CollectionPaymentReportDocument>.Filter.Eq(x => x.TenantId, tenantId);
        if (status.HasValue) filter &= Builders<CollectionPaymentReportDocument>.Filter.Eq(x => x.Status, status.Value);
        return await _reports.Find(filter).SortByDescending(x => x.CreatedAt).Limit(Math.Clamp(limit, 1, 1000)).ToListAsync(ct);
    }
}

// ----------------------------------------------------------------------
// What the assistant tells the customer

public sealed record PaymentInstructionsLoan(string Reference, string? Product, decimal OverdueBalance, decimal TotalBalance, string Currency);

public sealed record PaymentInstructionsMethod(string Type, string Name, string? AccountNumber, string? AccountHolder, string? AccountType, string? Currency, string? Instructions);

public sealed record PaymentInstructions
{
    public bool Configured { get; init; }
    /// <summary>What the customer must quote: "numero de prestamo" or "numero de cliente".</summary>
    public string ReferenceLabel { get; init; } = "numero de prestamo";
    /// <summary>The customer's references, one per open loan with a balance (or the customer number).</summary>
    public IReadOnlyList<PaymentInstructionsLoan> Loans { get; init; } = [];
    public IReadOnlyList<PaymentInstructionsMethod> Methods { get; init; } = [];
    public string? Notes { get; init; }
    public string? Message { get; init; }
}

public static class CollectionPaymentDesk
{
    /// <summary>The assistant writes this; it is replaced by <see cref="RenderForChat"/> right before sending.</summary>
    public const string Placeholder = "[MEDIOS_DE_PAGO]";

    /// <summary>How long a pending report keeps collecting the details the customer sends afterwards.</summary>
    public static readonly TimeSpan CompletionWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// The customer's pending report that a new report only completes: same customer, reported in the
    /// last 24 hours, the same amount (or one of the two without amount), and not a different receipt.
    /// Two different receipts are two payments.
    /// </summary>
    public static CollectionPaymentReportDocument? PendingToComplete(
        IEnumerable<CollectionPaymentReportDocument> reports, CollectionPaymentReportDocument incoming, DateTimeOffset now)
        => reports
            .Where(r => r.Status == CollectionPaymentReportStatus.Pending)
            .Where(r => r.TenantId == incoming.TenantId && r.PartyId == incoming.PartyId)
            .Where(r => now - r.CreatedAt < CompletionWindow)
            .Where(r => r.Amount is null || incoming.Amount is null || r.Amount == incoming.Amount)
            .Where(r => r.Reference is null || incoming.Reference is null ||
                        string.Equals(r.Reference, incoming.Reference, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault();

    /// <summary>The customer's confirmed report with the same receipt, if this one only repeats it.</summary>
    public static CollectionPaymentReportDocument? AlreadyConfirmed(
        IEnumerable<CollectionPaymentReportDocument> reports, CollectionPaymentReportDocument incoming)
        => string.IsNullOrWhiteSpace(incoming.Reference)
            ? null
            : reports.FirstOrDefault(r => r.Status == CollectionPaymentReportStatus.Confirmed &&
                                          r.TenantId == incoming.TenantId && r.PartyId == incoming.PartyId &&
                                          string.Equals(r.Reference, incoming.Reference, StringComparison.OrdinalIgnoreCase));

    /// <summary>Fills the existing report with what the new one adds; what was already known stays.</summary>
    public static void Complete(CollectionPaymentReportDocument report, CollectionPaymentReportDocument incoming)
    {
        report.Amount ??= incoming.Amount;
        if (report.PaidOn is null && incoming.PaidOn is not null)
        {
            report.PaidOn = incoming.PaidOn;
            report.PaidOnText = incoming.PaidOnText;
        }
        report.PaidOnText ??= incoming.PaidOnText;
        report.Reference ??= incoming.Reference;
        report.Method ??= incoming.Method;
        report.LoanId ??= incoming.LoanId;
        report.ContactExecutionId ??= incoming.ContactExecutionId;
        foreach (var id in incoming.ReceiptMediaIds.Where(id => !report.ReceiptMediaIds.Contains(id)))
            report.ReceiptMediaIds.Add(id);
        if (!string.IsNullOrWhiteSpace(incoming.Notes) &&
            (report.Notes is null || !report.Notes.Contains(incoming.Notes, StringComparison.OrdinalIgnoreCase)))
            report.Notes = report.Notes is null ? incoming.Notes : $"{report.Notes} {incoming.Notes}";
    }
    private static readonly System.Text.RegularExpressions.Regex PlaceholderPattern =
        new(@"\[\s*MEDIOS[\s_]+DE[\s_]+PAGO\s*\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// What the model sees: which methods exist, without account numbers or holders, and how to
    /// hand them out. It cannot alter digits it never had.
    /// </summary>
    public static PaymentInstructions ForAssistant(PaymentInstructions instructions)
        => !instructions.Configured
            ? instructions
            : instructions with
            {
                Methods = instructions.Methods.Select(m => m with { AccountNumber = null, AccountHolder = null }).ToList(),
                Message = $"Para darle los medios de pago y su referencia escribe {Placeholder} en una linea aparte: el sistema pone las cuentas exactas. No escribas numeros de cuenta."
            };

    /// <summary>True when the reply names one of the lender's enabled payment methods.</summary>
    public static bool MentionsMethods(string? reply, CollectionPaymentSettingsDocument? settings)
        => !string.IsNullOrWhiteSpace(reply) && settings is not null &&
           settings.Methods.Any(m => m.Enabled && m.Name.Trim().Length >= 3 &&
                                     reply.Contains(m.Name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Keeps the model's opening line (the greeting) when it does not mention a method, and puts
    /// the exact block in place of everything else it wrote about paying.
    /// </summary>
    public static string ReplaceOwnDescription(string reply, string exact, CollectionPaymentSettingsDocument? settings)
    {
        var opening = reply.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
        if (opening.Length == 0 || MentionsMethods(opening, settings) || opening.Length > 160)
            opening = "Puedes pagar con cualquiera de estas opciones:";
        return $"{opening}\n\n{exact}";
    }

    public static bool HasPlaceholder(string? text) => !string.IsNullOrEmpty(text) && PlaceholderPattern.IsMatch(text);

    public static string ReplacePlaceholder(string text, string replacement)
    {
        // Lines the model wrote that the exact block already says (it likes to repeat the note) go away.
        var blockLines = replacement.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var own = string.Join("\n", text.Split('\n').Where(l => !blockLines.Contains(l.Trim())));
        var result = PlaceholderPattern.Replace(own, replacement.Replace("$", "$$"));
        return System.Text.RegularExpressions.Regex.Replace(result, @"\n{3,}", "\n\n").Trim();
    }

    private static readonly Dictionary<string, string> TypeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bank_transfer"] = "transferencia o deposito",
        ["cash"] = "efectivo",
        ["app"] = "app o billetera",
    };

    /// <summary>
    /// The payment options exactly as configured, for a chat (WhatsApp bold with single asterisks).
    /// Accounts and references are never retyped by the model.
    /// </summary>
    public static string RenderForChat(PaymentInstructions instructions)
    {
        if (!instructions.Configured || instructions.Methods.Count == 0)
            return "Un asesor te compartira por este chat los medios de pago.";

        var lines = new List<string>();
        foreach (var m in instructions.Methods)
        {
            var kind = TypeLabels.GetValueOrDefault(m.Type);
            lines.Add(kind is null ? $"*{m.Name}*" : $"*{m.Name}* ({kind})");
            var account = new[]
            {
                string.IsNullOrWhiteSpace(m.AccountNumber) ? null : $"Cuenta: *{m.AccountNumber}*",
                string.IsNullOrWhiteSpace(m.AccountType) ? null : m.AccountType,
                string.IsNullOrWhiteSpace(m.Currency) ? null : m.Currency,
                string.IsNullOrWhiteSpace(m.AccountHolder) ? null : $"Titular: {m.AccountHolder}"
            }.Where(x => x is not null).ToList();
            if (account.Count > 0) lines.Add(string.Join(" · ", account));
            if (!string.IsNullOrWhiteSpace(m.Instructions)) lines.Add(m.Instructions!);
            lines.Add(string.Empty);
        }

        if (instructions.Loans.Count == 1)
            lines.Add($"Tu referencia ({instructions.ReferenceLabel}): *{instructions.Loans[0].Reference}*");
        else if (instructions.Loans.Count > 1)
        {
            lines.Add($"Tus referencias ({instructions.ReferenceLabel}):");
            foreach (var loan in instructions.Loans)
                lines.Add($"- *{loan.Reference}*{(loan.OverdueBalance > 0 ? $": vencido {loan.Currency} {loan.OverdueBalance.ToString("#,##0.00", CultureInfo.InvariantCulture)}" : string.Empty)}");
        }
        if (!string.IsNullOrWhiteSpace(instructions.Notes)) lines.Add(instructions.Notes!);

        return string.Join("\n", lines).Trim();
    }

    public static PaymentInstructions BuildInstructions(CollectionPaymentSettingsDocument? settings, IReadOnlyList<CollectionLoanDocument> loans)
    {
        var methods = settings?.Methods.Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Name)).ToList() ?? [];
        if (methods.Count == 0)
            return new PaymentInstructions
            {
                Configured = false,
                Message = "La financiera no tiene medios de pago configurados: no inventes cuentas; pasa el caso a un asesor para que le de los datos."
            };

        var open = loans.Where(l => l.Status is not CollectionLoanStatus.PaidOff && l.TotalBalance > 0).ToList();
        var currencies = open.Select(l => l.Currency).Where(c => !string.IsNullOrWhiteSpace(c)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byCustomer = string.Equals(settings!.ReferenceKind, "customer", StringComparison.OrdinalIgnoreCase);

        var references = byCustomer
            ? open.GroupBy(l => l.CustomerExternalId ?? l.PartyId)
                .Select(g => new PaymentInstructionsLoan(g.Key, null, g.Sum(l => l.OverdueBalance), g.Sum(l => l.TotalBalance), g.First().Currency))
                .ToList()
            : open.OrderByDescending(l => l.OverdueBalance)
                .Select(l => new PaymentInstructionsLoan(l.LoanExternalId, l.Product, l.OverdueBalance, l.TotalBalance, l.Currency))
                .ToList();

        return new PaymentInstructions
        {
            Configured = true,
            ReferenceLabel = byCustomer ? "numero de cliente" : "numero de prestamo",
            Loans = references,
            // A method in another currency is not an option for this customer.
            Methods = methods
                .Where(m => string.IsNullOrWhiteSpace(m.Currency) || currencies.Count == 0 || currencies.Contains(m.Currency!))
                .Select(m => new PaymentInstructionsMethod(m.Type, m.Name, m.AccountNumber, m.AccountHolder, m.AccountType, m.Currency, m.Instructions))
                .ToList(),
            Notes = settings.Notes
        };
    }

    /// <summary>
    /// The date of a payment the customer already made: "ayer", "el lunes" (the last one), "el 20"
    /// (this month or the last). Never in the future.
    /// </summary>
    public static DateTimeOffset? ResolvePastDate(string? text, DateTimeOffset localNow)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var today = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, TimeSpan.Zero);
        var normalized = text.Trim().ToLowerInvariant()
            .Replace('á', 'a').Replace('é', 'e').Replace('í', 'i').Replace('ó', 'o').Replace('ú', 'u');

        if (Regex.IsMatch(normalized, @"\b(antier|anteayer|antes de ayer)\b")) return today.AddDays(-2);
        if (Regex.IsMatch(normalized, @"\bayer\b")) return today.AddDays(-1);
        if (Regex.IsMatch(normalized, @"\bhoy\b|\bhace (un )?rato\b|\bahorita\b")) return today;

        var resolved = CampaignReplyClassifier.ResolveDate(text, today);
        if (resolved is null) return null;
        var date = new DateTimeOffset(resolved.Value.Year, resolved.Value.Month, resolved.Value.Day, 0, 0, 0, TimeSpan.Zero);
        // The resolver looks forward (promises): bring a payment date back to the past —
        // "el lunes" is last Monday, "el 28" is the 28th of last month.
        var weekday = Regex.IsMatch(normalized, @"\b(lunes|martes|miercoles|jueves|viernes|sabado|domingo)\b");
        while (date > today)
            date = weekday ? date.AddDays(-7) : date.AddMonths(-1);
        return date;
    }
}

// ----------------------------------------------------------------------
// A person reviews what customers reported

public sealed record PaymentReportReviewResult(bool Ok, string Message, CollectionPaymentReportDocument? Report = null);

public interface ICollectionPaymentReportService
{
    /// <summary>The payment arrived: it is registered on the loan (closing the promise it covers).</summary>
    Task<PaymentReportReviewResult> ConfirmAsync(string tenantId, string reportId, string? loanId, decimal? amount, DateTimeOffset? paidAt, string userId, CancellationToken ct = default);
    Task<PaymentReportReviewResult> RejectAsync(string tenantId, string reportId, string? notes, string userId, CancellationToken ct = default);
}

public sealed class CollectionPaymentReportService : ICollectionPaymentReportService
{
    private readonly ICollectionPaymentDeskStore _desk;
    private readonly ICollectionsStore _collections;
    private readonly ICollectionPaymentService _payments;

    public CollectionPaymentReportService(ICollectionPaymentDeskStore desk, ICollectionsStore collections, ICollectionPaymentService payments)
    {
        _desk = desk;
        _collections = collections;
        _payments = payments;
    }

    public async Task<PaymentReportReviewResult> ConfirmAsync(string tenantId, string reportId, string? loanId, decimal? amount, DateTimeOffset? paidAt, string userId, CancellationToken ct = default)
    {
        var report = await _desk.GetReportAsync(tenantId, reportId, ct);
        if (report is null) return new(false, "Reporte no encontrado.");
        if (report.Status != CollectionPaymentReportStatus.Pending) return new(false, "Este reporte ya fue revisado.", report);

        var value = amount ?? report.Amount;
        if (value is not > 0) return new(false, "Indica el monto que se recibio.", report);

        var loans = await _collections.GetLoansByPartyAsync(tenantId, report.PartyId, ct);
        var loan = loans.FirstOrDefault(l => l.Id == (loanId ?? report.LoanId))
                   ?? loans.Where(l => l.Status is not CollectionLoanStatus.PaidOff).OrderByDescending(l => l.OverdueBalance).FirstOrDefault();
        if (loan is null) return new(false, "El cliente no tiene prestamos para aplicar el pago.", report);

        var result = await _payments.RegisterAsync(tenantId,
        [
            new CollectionPaymentInput
            {
                LoanId = loan.Id,
                Amount = value.Value,
                PaidAt = paidAt ?? report.PaidOn ?? DateTimeOffset.UtcNow,
                Reference = report.Reference,
                ContactExecutionId = report.ContactExecutionId
            }
        ], "reported", userId, ct);
        if (result.Errors.Count > 0) return new(false, result.Errors[0].Message, report);

        report.Status = CollectionPaymentReportStatus.Confirmed;
        report.LoanId = loan.Id;
        report.Amount = value;
        report.PaymentId = result.Payments.FirstOrDefault()?.Id;
        report.ReviewedBy = userId;
        report.ReviewedAt = DateTimeOffset.UtcNow;
        report.ReviewNotes = result.Duplicates > 0 ? "El pago ya estaba registrado con esa referencia." : null;
        await _desk.UpdateReportAsync(report, ct);
        return new(true, result.Duplicates > 0 ? "El pago ya estaba registrado." : "Pago registrado.", report);
    }

    public async Task<PaymentReportReviewResult> RejectAsync(string tenantId, string reportId, string? notes, string userId, CancellationToken ct = default)
    {
        var report = await _desk.GetReportAsync(tenantId, reportId, ct);
        if (report is null) return new(false, "Reporte no encontrado.");
        if (report.Status != CollectionPaymentReportStatus.Pending) return new(false, "Este reporte ya fue revisado.", report);
        report.Status = CollectionPaymentReportStatus.Rejected;
        report.ReviewedBy = userId;
        report.ReviewedAt = DateTimeOffset.UtcNow;
        report.ReviewNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await _desk.UpdateReportAsync(report, ct);
        return new(true, "Reporte rechazado.", report);
    }
}

/// <summary>Replaces the assistant's [MEDIOS_DE_PAGO] with the exact payment options of the customer it talks to.</summary>
public sealed class PaymentInstructionsReplyTransformer : AgentFlow.Core.Engine.IChannelReplyTransformer
{
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopes;

    public PaymentInstructionsReplyTransformer(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<string> TransformAsync(AgentFlow.Domain.Aggregates.ChannelMessage incoming, AgentFlow.Domain.Aggregates.ChannelDefinition channel, string reply, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var desk = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ICollectionPaymentDeskStore>(scope.ServiceProvider);
        var hasPlaceholder = CollectionPaymentDesk.HasPlaceholder(reply);
        if (!hasPlaceholder)
        {
            // Cheap check first: only replies that talk about this lender's payment methods.
            var settings = desk is null ? null : await desk.GetSettingsAsync(incoming.TenantId, ct);
            if (!CollectionPaymentDesk.MentionsMethods(reply, settings)) return reply;
        }

        var assistant = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ICollectionsAssistantService>(scope.ServiceProvider);
        // The customer is the sender of the conversation, never anything the model wrote.
        var instructions = await assistant.GetPaymentInstructionsAsync(incoming.TenantId, incoming.From, ct);
        var exact = CollectionPaymentDesk.RenderForChat(instructions);
        // The model described the methods in its own words instead of using the placeholder: its
        // version (which may have a wrong digit) is replaced by the exact block, keeping its opening line.
        return hasPlaceholder
            ? CollectionPaymentDesk.ReplacePlaceholder(reply, exact)
            : CollectionPaymentDesk.ReplaceOwnDescription(reply, exact, settings: await desk!.GetSettingsAsync(incoming.TenantId, ct));
    }
}
