using System.Globalization;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;

namespace AgentFlow.Api.Collections;

/// <summary>How much overdue money came back in a period, and how much of it the collections work brought in.</summary>
public sealed record RecoveryReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string Currency { get; init; } = "MXN";
    public int AttributionWindowDays { get; init; }
    public RecoveryTotals Totals { get; init; } = new();
    public RecoveryFunnel Funnel { get; init; } = new();
    public RecoveryPromises Promises { get; init; } = new();
    public IReadOnlyList<RecoveryResolution> Resolution { get; init; } = [];
    public IReadOnlyList<RecoveryGroup> ByCampaign { get; init; } = [];
    public IReadOnlyList<RecoveryGroup> ByStage { get; init; } = [];
    public IReadOnlyList<RecoveryDay> Daily { get; init; } = [];
    public IReadOnlyList<RecoveryEventView> Recent { get; init; } = [];
}

public sealed record RecoveryTotals
{
    /// <summary>Every overdue amount that came back in the period (payments and cut-off decreases).</summary>
    public decimal Recovered { get; init; }
    /// <summary>The part paid by customers a campaign reached in the attribution window before paying.</summary>
    public decimal Attributed { get; init; }
    public decimal Unattributed { get; init; }
    /// <summary>Overdue balance of the customers reached in the period, as it was when they were contacted.</summary>
    public decimal ManagedOverdue { get; init; }
    /// <summary>Attributed / managed overdue.</summary>
    public decimal RecoveryRate { get; init; }
    public int CustomersPaid { get; init; }
    public int AttributedCustomers { get; init; }
    public int Events { get; init; }
    /// <summary>Average days from the campaign contact to the attributed payment.</summary>
    public decimal? AvgDaysToPay { get; init; }
}

public sealed record RecoveryFunnel
{
    public int Contacts { get; init; }
    public int Reached { get; init; }
    public int Replied { get; init; }
    public int Promised { get; init; }
    public int Paid { get; init; }
}

public sealed record RecoveryPromises
{
    /// <summary>Promises made in the period, not counting cancelled ones (replaced or withdrawn).</summary>
    public int Created { get; init; }
    public decimal PromisedAmount { get; init; }
    public int Kept { get; init; }
    public int Broken { get; init; }
    public int Pending { get; init; }
    public int Cancelled { get; init; }
    /// <summary>Kept / (kept + broken): promises still open don't count yet.</summary>
    public decimal KeptRate { get; init; }
    /// <summary>Money paid by customers with an open promise when they paid.</summary>
    public decimal RecoveredWithPromise { get; init; }
}

/// <summary>Who settled the reached contacts: the assistant, the automatic reply reading, an advisor...</summary>
public sealed record RecoveryResolution(string Key, string Label, int Contacts);

/// <summary>One row of the campaign or stage breakdown.</summary>
public sealed record RecoveryGroup
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Contacts { get; init; }
    public int Reached { get; init; }
    public int Replied { get; init; }
    public int Promised { get; init; }
    public int CustomersPaid { get; init; }
    public decimal ManagedOverdue { get; init; }
    public decimal Recovered { get; init; }
    public decimal RecoveryRate { get; init; }
}

public sealed record RecoveryDay(DateTime Date, decimal Attributed, decimal Unattributed, int Promises);

public sealed record RecoveryEventView
{
    public string PartyId { get; init; } = string.Empty;
    public string? Customer { get; init; }
    public string? LoanExternalId { get; init; }
    public decimal Amount { get; init; }
    public DateTimeOffset At { get; init; }
    /// <summary>payment (registered payment) or cutoff (the overdue went down between two cut-offs).</summary>
    public string Kind { get; init; } = "payment";
    public string? CampaignId { get; init; }
    public string? CampaignName { get; init; }
    public bool WithPromise { get; init; }
}

/// <summary>Everything the report is computed from; the service loads it, <see cref="RecoveryReportBuilder"/> computes.</summary>
public sealed record RecoveryInputs
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<CollectionLoanDocument> Loans { get; init; } = [];
    /// <summary>Snapshots up to <see cref="To"/>, including each loan's cut-off before the period.</summary>
    public IReadOnlyList<CollectionLoanSnapshotDocument> Snapshots { get; init; } = [];
    public IReadOnlyList<CollectionPaymentDocument> Payments { get; init; } = [];
    public IReadOnlyList<CollectionPromiseDocument> Promises { get; init; } = [];
    /// <summary>Campaign contacts from the attribution window before <see cref="From"/> up to <see cref="To"/>.</summary>
    public IReadOnlyList<CampaignContactExecutionContract> Contacts { get; init; } = [];
    public IReadOnlyDictionary<string, string> CampaignNames { get; init; } = new Dictionary<string, string>();
}

public static class RecoveryReportBuilder
{
    /// <summary>A payment counts for the last campaign contact made this many days before it.</summary>
    public static readonly TimeSpan AttributionWindow = TimeSpan.FromDays(30);
    /// <summary>A promise still explains a payment made up to this long after its date.</summary>
    private static readonly TimeSpan PromiseGrace = TimeSpan.FromDays(7);

    private sealed record Event(string PartyId, string LoanId, decimal Amount, DateTimeOffset At, string Kind)
    {
        /// <summary>The campaign contact the customer reported the payment to, if any.</summary>
        public string? ReportedToContactId { get; init; }
        public CampaignContactExecutionContract? Contact { get; set; }
        public bool WithPromise { get; set; }
    }

    public static RecoveryReport Build(RecoveryInputs input)
    {
        var loansById = input.Loans.ToDictionary(l => l.Id);
        var events = RecoveryEvents(input, loansById);

        var reachedByParty = input.Contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.PartyId) && IsReached(c))
            .GroupBy(c => c.PartyId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.CreatedAt).ToList());
        var promisesByParty = input.Promises.Where(p => p.Status != CollectionPromiseStatus.Cancelled)
            .GroupBy(p => p.PartyId).ToDictionary(g => g.Key, g => g.ToList());

        var contactsById = input.Contacts.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var e in events)
        {
            // A payment the customer reported in a campaign conversation belongs to that campaign: a
            // payment "hoy" carries no time and would otherwise look older than the message that got it.
            // Otherwise, last touch: the most recent contact before the money came back, within the window.
            e.Contact = (e.ReportedToContactId is { } reportedTo ? contactsById.GetValueOrDefault(reportedTo) : null)
                ?? reachedByParty.GetValueOrDefault(e.PartyId)?
                    .LastOrDefault(c => c.CreatedAt <= e.At && e.At - c.CreatedAt <= AttributionWindow);
            e.WithPromise = promisesByParty.GetValueOrDefault(e.PartyId)?
                .Any(p => p.CreatedAt <= e.At && e.At <= p.PromisedDate + PromiseGrace) ?? false;
        }

        var periodContacts = input.Contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.PartyId) && c.CreatedAt >= input.From && c.CreatedAt <= input.To)
            .Where(c => c.RetriedByExecutionId is null)
            .ToList();
        var promisedContactIds = input.Promises.Where(p => p.ContactExecutionId is not null).Select(p => p.ContactExecutionId!).ToHashSet();
        var attributed = events.Where(e => e.Contact is not null).ToList();
        var paidContactIds = attributed.Select(e => e.Contact!.Id).ToHashSet();

        bool Promised(CampaignContactExecutionContract c) => c.Outcome == CampaignOutcomes.PromiseToPay || promisedContactIds.Contains(c.Id);

        var managed = ManagedOverdue(periodContacts.Where(IsReached));
        var attributedAmount = attributed.Sum(e => e.Amount);
        var recovered = events.Sum(e => e.Amount);

        var periodPromises = input.Promises.Where(p => p.CreatedAt >= input.From && p.CreatedAt <= input.To).ToList();
        var livePromises = periodPromises.Where(p => p.Status != CollectionPromiseStatus.Cancelled).ToList();
        var kept = periodPromises.Count(p => p.Status == CollectionPromiseStatus.Kept);
        var broken = periodPromises.Count(p => p.Status == CollectionPromiseStatus.Broken);

        // The money of what happened in the period: recovered loans, then the customers worked, then the portfolio.
        var currency = MostCommon(events.Select(e => loansById.GetValueOrDefault(e.LoanId)?.Currency))
            ?? MostCommon(periodContacts.Select(c => c.Variables?.GetValueOrDefault("moneda")))
            ?? MostCommon(input.Loans.Select(l => l.Currency))
            ?? "MXN";

        return new RecoveryReport
        {
            From = input.From,
            To = input.To,
            Currency = currency,
            AttributionWindowDays = (int)AttributionWindow.TotalDays,
            Totals = new RecoveryTotals
            {
                Recovered = recovered,
                Attributed = attributedAmount,
                Unattributed = recovered - attributedAmount,
                ManagedOverdue = managed,
                RecoveryRate = Rate(attributedAmount, managed),
                CustomersPaid = events.Select(e => e.PartyId).Distinct().Count(),
                AttributedCustomers = attributed.Select(e => e.PartyId).Distinct().Count(),
                Events = events.Count,
                AvgDaysToPay = attributed.Count == 0
                    ? null
                    : Math.Round((decimal)attributed.Average(e => (e.At - e.Contact!.CreatedAt).TotalDays), 1)
            },
            Funnel = new RecoveryFunnel
            {
                Contacts = periodContacts.Count,
                Reached = periodContacts.Count(IsReached),
                Replied = periodContacts.Count(c => c.RepliedAt is not null),
                Promised = periodContacts.Count(Promised),
                Paid = periodContacts.Count(c => paidContactIds.Contains(c.Id))
            },
            Promises = new RecoveryPromises
            {
                Created = livePromises.Count,
                PromisedAmount = livePromises.Sum(p => p.Amount ?? 0m),
                Kept = kept,
                Broken = broken,
                Pending = periodPromises.Count(p => p.Status == CollectionPromiseStatus.Pending),
                Cancelled = periodPromises.Count(p => p.Status == CollectionPromiseStatus.Cancelled),
                KeptRate = Rate(kept, kept + broken),
                RecoveredWithPromise = events.Where(e => e.WithPromise).Sum(e => e.Amount)
            },
            Resolution = Resolution(periodContacts.Where(IsReached).ToList()),
            ByCampaign = Groups(
                periodContacts, attributed,
                c => c.CampaignId,
                key => input.CampaignNames.GetValueOrDefault(key) ?? "Campaña eliminada",
                Promised),
            ByStage = Groups(
                periodContacts, attributed,
                c => c.Variables?.GetValueOrDefault("etapa") is { Length: > 0 } stage ? stage : "Sin etapa",
                key => key,
                Promised),
            Daily = Daily(input, events, periodPromises),
            Recent = events
                .OrderByDescending(e => e.At)
                .Take(25)
                .Select(e => new RecoveryEventView
                {
                    PartyId = e.PartyId,
                    Customer = loansById.GetValueOrDefault(e.LoanId)?.CustomerName,
                    LoanExternalId = loansById.GetValueOrDefault(e.LoanId)?.LoanExternalId,
                    Amount = e.Amount,
                    At = e.At,
                    Kind = e.Kind,
                    CampaignId = e.Contact?.CampaignId,
                    CampaignName = e.Contact is null ? null : input.CampaignNames.GetValueOrDefault(e.Contact.CampaignId),
                    WithPromise = e.WithPromise
                })
                .ToList()
        };
    }

    /// <summary>
    /// Money that came back in the period: registered payments, plus the decrease of the overdue
    /// balance between two cut-offs that no registered payment explains (lenders that only send
    /// cut-offs). A cut-off that turns the loan charged-off or restructured is not money received.
    /// </summary>
    private static List<Event> RecoveryEvents(RecoveryInputs input, IReadOnlyDictionary<string, CollectionLoanDocument> loans)
    {
        var events = input.Payments
            .Where(p => p.PaidAt >= input.From && p.PaidAt <= input.To && p.Amount > 0)
            .Select(p => new Event(p.PartyId, p.LoanId, p.Amount, p.PaidAt, "payment") { ReportedToContactId = p.ContactExecutionId })
            .ToList();

        var paymentsByLoan = input.Payments.GroupBy(p => p.LoanId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var history in input.Snapshots.GroupBy(s => s.LoanId))
        {
            if (!loans.TryGetValue(history.Key, out var loan)) continue;
            var ordered = history.OrderBy(s => s.SnapshotDate).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var (previous, current) = (ordered[i - 1], ordered[i]);
                if (current.SnapshotDate < input.From || current.SnapshotDate > input.To) continue;
                if (current.Status is CollectionLoanStatus.ChargedOff or CollectionLoanStatus.Restructured) continue;

                var decrease = previous.OverdueBalance - current.OverdueBalance;
                if (decrease <= 0) continue;
                var registered = paymentsByLoan.GetValueOrDefault(loan.Id)?
                    .Where(p => p.PaidAt > previous.SnapshotDate && p.PaidAt <= current.SnapshotDate)
                    .Sum(p => p.Amount) ?? 0m;
                var unexplained = decrease - registered;
                if (unexplained >= 0.01m)
                    events.Add(new Event(loan.PartyId, loan.Id, unexplained, current.SnapshotDate, "cutoff"));
            }
        }
        return events;
    }

    /// <summary>Overdue of each customer when first reached in the period (counted once per customer).</summary>
    private static decimal ManagedOverdue(IEnumerable<CampaignContactExecutionContract> contacts)
        => contacts
            .GroupBy(c => c.PartyId!)
            .Sum(g => Overdue(g.OrderBy(c => c.CreatedAt).First()));

    private static decimal Overdue(CampaignContactExecutionContract contact)
        => decimal.TryParse(contact.Variables?.GetValueOrDefault("total_vencido"), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

    private static IReadOnlyList<RecoveryGroup> Groups(
        IReadOnlyList<CampaignContactExecutionContract> periodContacts,
        IReadOnlyList<Event> attributed,
        Func<CampaignContactExecutionContract, string> keyOf,
        Func<string, string> labelOf,
        Func<CampaignContactExecutionContract, bool> promised)
    {
        var contactsByKey = periodContacts.GroupBy(keyOf).ToDictionary(g => g.Key, g => g.ToList());
        var eventsByKey = attributed.GroupBy(e => keyOf(e.Contact!)).ToDictionary(g => g.Key, g => g.ToList());

        return contactsByKey.Keys.Union(eventsByKey.Keys)
            .Select(key =>
            {
                var contacts = contactsByKey.GetValueOrDefault(key) ?? [];
                var events = eventsByKey.GetValueOrDefault(key) ?? [];
                var managed = ManagedOverdue(contacts.Where(IsReached));
                var amount = events.Sum(e => e.Amount);
                return new RecoveryGroup
                {
                    Key = key,
                    Label = labelOf(key),
                    Contacts = contacts.Count,
                    Reached = contacts.Count(IsReached),
                    Replied = contacts.Count(c => c.RepliedAt is not null),
                    Promised = contacts.Count(promised),
                    CustomersPaid = events.Select(e => e.PartyId).Distinct().Count(),
                    ManagedOverdue = managed,
                    Recovered = amount,
                    RecoveryRate = Rate(amount, managed)
                };
            })
            .OrderByDescending(g => g.Recovered)
            .ThenByDescending(g => g.Contacts)
            .ToList();
    }

    private static IReadOnlyList<RecoveryResolution> Resolution(IReadOnlyList<CampaignContactExecutionContract> reached)
    {
        string Key(CampaignContactExecutionContract c) =>
            string.IsNullOrWhiteSpace(c.Outcome) ? (c.AttentionAt is not null ? "advisor_pending" : "none") : c.OutcomeSource switch
            {
                CampaignOutcomes.Sources.Agent => "assistant",
                CampaignOutcomes.Sources.Reply => "automatic",
                CampaignOutcomes.Sources.Human => "advisor",
                CampaignOutcomes.Sources.Workflow => "workflow",
                _ => "automatic"
            };

        var labels = new (string Key, string Label)[]
        {
            ("assistant", "Asistente IA"),
            ("automatic", "Lectura automática de la respuesta"),
            ("advisor", "Asesor"),
            ("workflow", "Flujo automatizado"),
            ("advisor_pending", "Esperando a un asesor"),
            ("none", "Sin resultado aún")
        };
        var counts = reached.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        return labels.Select(l => new RecoveryResolution(l.Key, l.Label, counts.GetValueOrDefault(l.Key)))
            .Where(r => r.Contacts > 0)
            .ToList();
    }

    private static IReadOnlyList<RecoveryDay> Daily(RecoveryInputs input, IReadOnlyList<Event> events, IReadOnlyList<CollectionPromiseDocument> promises)
    {
        var days = new List<RecoveryDay>();
        for (var day = input.From.UtcDateTime.Date; day <= input.To.UtcDateTime.Date; day = day.AddDays(1))
        {
            var current = day;
            var ofDay = events.Where(e => e.At.UtcDateTime.Date == current).ToList();
            days.Add(new RecoveryDay(
                current,
                ofDay.Where(e => e.Contact is not null).Sum(e => e.Amount),
                ofDay.Where(e => e.Contact is null).Sum(e => e.Amount),
                promises.Count(p => p.CreatedAt.UtcDateTime.Date == current)));
        }
        return days;
    }

    private static bool IsReached(CampaignContactExecutionContract c)
        => c.RepliedAt is not null || c.Status is
            CampaignContactExecutionStatus.Sent or CampaignContactExecutionStatus.Delivered or
            CampaignContactExecutionStatus.Read or CampaignContactExecutionStatus.Connected or
            CampaignContactExecutionStatus.WorkflowStarted;

    private static string? MostCommon(IEnumerable<string?> values)
        => values.Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v!.Trim().ToUpperInvariant())
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

    private static decimal Rate(decimal part, decimal total) => total <= 0 ? 0 : Math.Round(part / total, 4);
}

public interface ICollectionsRecoveryService
{
    Task<RecoveryReport> GetReportAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public sealed class CollectionsRecoveryService : ICollectionsRecoveryService
{
    /// <summary>How far back to look for each loan's cut-off before the period.</summary>
    private static readonly TimeSpan SnapshotLookback = TimeSpan.FromDays(120);

    private readonly ICollectionsStore _store;
    private readonly ICampaignStore _campaigns;

    public CollectionsRecoveryService(ICollectionsStore store, ICampaignStore campaigns)
    {
        _store = store;
        _campaigns = campaigns;
    }

    public async Task<RecoveryReport> GetReportAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var loans = await _store.GetLoansAsync(tenantId, ct);
        var snapshots = await _store.GetSnapshotsBetweenAsync(tenantId, from - SnapshotLookback, to, ct);
        var payments = await _store.GetPaymentsBetweenAsync(tenantId, from - SnapshotLookback, to, ct);
        var promises = await _store.GetPromisesAsync(tenantId, limit: 5000, ct: ct);
        var contacts = await _campaigns.GetContactExecutionsCreatedBetweenAsync(tenantId, from - RecoveryReportBuilder.AttributionWindow, to, ct);
        var campaigns = await _campaigns.GetCampaignsAsync(tenantId, ct);

        return RecoveryReportBuilder.Build(new RecoveryInputs
        {
            From = from,
            To = to,
            Loans = loans,
            Snapshots = snapshots,
            Payments = payments,
            Promises = promises,
            Contacts = contacts,
            CampaignNames = campaigns.ToDictionary(c => c.Id, c => c.Name)
        });
    }
}
