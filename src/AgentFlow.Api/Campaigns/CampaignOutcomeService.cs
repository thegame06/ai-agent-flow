using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Collections;

namespace AgentFlow.Api.Campaigns;

/// <summary>Request to set the business result of one campaign contact.</summary>
public sealed record CampaignOutcomeRequest(
    string Outcome,
    string Source,
    string By,
    string? Notes = null,
    decimal? PromiseAmount = null,
    DateTimeOffset? PromiseDate = null);

/// <summary>Aggregated results of a campaign: funnel, outcomes and promises.</summary>
public sealed record CampaignResultsContract
{
    public string CampaignId { get; init; } = string.Empty;
    public int Contacts { get; init; }
    public int Sent { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int Replied { get; init; }
    public int WithOutcome { get; init; }
    public int Pending { get; init; }
    public decimal ReplyRate { get; init; }
    public decimal ContactRate { get; init; }
    public int Promises { get; init; }
    public decimal PromisedAmount { get; init; }
    public IReadOnlyList<CampaignOutcomeCount> Outcomes { get; init; } = [];
    public IReadOnlyList<CampaignContactExecutionContract> ContactsList { get; init; } = [];
}

public sealed record CampaignOutcomeCount(string Code, string Label, string Category, int Count);

public interface ICampaignOutcomeService
{
    /// <summary>Sets the outcome of a contact. A promise to pay also creates a collections promise.</summary>
    Task<CampaignContactExecutionContract> RecordAsync(string tenantId, string contactExecutionId, CampaignOutcomeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Attributes an inbound message to the latest campaign contact sent to that number (within
    /// the attribution window), stores the reply and classifies it. Returns null when the sender
    /// was not contacted by a campaign recently.
    /// </summary>
    Task<CampaignContactExecutionContract?> AttributeReplyAsync(string tenantId, string from, string text, DateTimeOffset receivedAt, CancellationToken ct = default);

    Task<CampaignResultsContract> GetResultsAsync(string tenantId, string campaignId, CancellationToken ct = default);
}

public sealed class CampaignOutcomeService : ICampaignOutcomeService
{
    /// <summary>Replies within this window after a campaign message are attributed to it.</summary>
    public static readonly TimeSpan AttributionWindow = TimeSpan.FromDays(7);

    /// <summary>A sent contact with no reply after this long counts as "sin respuesta".</summary>
    public static readonly TimeSpan NoResponseAfter = TimeSpan.FromHours(24);

    // Sources that a later automatic classification must not overwrite.
    private static readonly HashSet<string> StickySources = new(StringComparer.OrdinalIgnoreCase)
    {
        CampaignOutcomes.Sources.Human, CampaignOutcomes.Sources.Agent, CampaignOutcomes.Sources.Workflow
    };

    private readonly ICampaignStore _campaigns;
    private readonly ICollectionPromiseService _promises;

    public CampaignOutcomeService(ICampaignStore campaigns, ICollectionPromiseService promises)
    {
        _campaigns = campaigns;
        _promises = promises;
    }

    public async Task<CampaignContactExecutionContract> RecordAsync(string tenantId, string contactExecutionId, CampaignOutcomeRequest request, CancellationToken ct = default)
    {
        var definition = CampaignOutcomes.Find(request.Outcome)
            ?? throw new ArgumentException($"Unknown outcome '{request.Outcome}'.");
        var contact = await _campaigns.GetContactExecutionAsync(tenantId, contactExecutionId, ct)
            ?? throw new KeyNotFoundException("Campaign contact not found.");

        return await ApplyAsync(contact, definition.Code, request, ct);
    }

    public async Task<CampaignContactExecutionContract?> AttributeReplyAsync(string tenantId, string from, string text, DateTimeOffset receivedAt, CancellationToken ct = default)
    {
        var key = RecipientKeys.From(from);
        if (key is null) return null;

        var contact = await _campaigns.FindLatestContactByRecipientKeyAsync(tenantId, key, receivedAt - AttributionWindow, ct);
        if (contact is null || contact.Status is CampaignContactExecutionStatus.Failed or CampaignContactExecutionStatus.Skipped)
            return null;

        contact = contact with
        {
            RepliedAt = contact.RepliedAt ?? receivedAt,
            LastReply = Truncate(text, 1000),
            ReplyCount = contact.ReplyCount + 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (contact.OutcomeSource is not null && StickySources.Contains(contact.OutcomeSource))
            return await _campaigns.UpdateContactExecutionAsync(contact, ct) ?? contact;

        var campaign = await _campaigns.GetCampaignAsync(tenantId, contact.CampaignId, ct);
        // "manana" / "el viernes" are relative to the customer's day, not UTC.
        var classification = CampaignReplyClassifier.Classify(text, ToLocal(receivedAt, campaign?.Timezone));
        var outcome = classification.Outcome;
        // Keep a specific earlier outcome ("promesa") when a follow-up reply is just chatter ("gracias").
        if (outcome is null && contact.Outcome is not null && contact.Outcome != CampaignOutcomes.Replied)
            return await _campaigns.UpdateContactExecutionAsync(contact, ct) ?? contact;

        // Collections-only outcomes are not offered for e.g. sales campaigns: fall back to "replied".
        if (outcome is not null && campaign is not null && CampaignOutcomes.For(campaign.CampaignType).All(x => x.Code != outcome))
            outcome = null;

        return await ApplyAsync(contact, outcome ?? CampaignOutcomes.Replied, new CampaignOutcomeRequest(
            outcome ?? CampaignOutcomes.Replied,
            CampaignOutcomes.Sources.Reply,
            "reply-classifier",
            Notes: classification.Outcome is null ? null : $"Clasificado automaticamente de la respuesta: \"{Truncate(text, 200)}\"",
            PromiseAmount: classification.PromiseAmount,
            PromiseDate: classification.PromiseDate), ct);
    }

    public async Task<CampaignResultsContract> GetResultsAsync(string tenantId, string campaignId, CancellationToken ct = default)
    {
        var contacts = await _campaigns.GetContactExecutionsByCampaignAsync(tenantId, campaignId, 5000, ct);
        var now = DateTimeOffset.UtcNow;

        var sent = contacts.Count(c => IsReached(c.Status));
        var failed = contacts.Count(c => c.Status == CampaignContactExecutionStatus.Failed);
        var skipped = contacts.Count(c => c.Status == CampaignContactExecutionStatus.Skipped);
        var replied = contacts.Count(c => c.RepliedAt is not null);

        var effective = contacts.Select(c => EffectiveOutcome(c, now)).ToList();
        var outcomes = CampaignOutcomes.All
            .Select(d => new CampaignOutcomeCount(d.Code, d.Label, d.Category, effective.Count(o => o == d.Code)))
            .Where(x => x.Count > 0)
            .ToList();

        var promises = contacts.Where(c => c.Outcome == CampaignOutcomes.PromiseToPay).ToList();
        var positiveOrContacted = effective.Count(o => o is not null && CampaignOutcomes.Find(o)?.Category is "positive" or "negative" or "neutral");

        return new CampaignResultsContract
        {
            CampaignId = campaignId,
            Contacts = contacts.Count,
            Sent = sent,
            Failed = failed,
            Skipped = skipped,
            Replied = replied,
            WithOutcome = effective.Count(o => o is not null),
            Pending = effective.Count(o => o is null),
            ReplyRate = sent == 0 ? 0 : Math.Round((decimal)replied / sent, 4),
            ContactRate = contacts.Count == 0 ? 0 : Math.Round((decimal)positiveOrContacted / contacts.Count, 4),
            Promises = promises.Count,
            PromisedAmount = promises.Sum(p => p.PromiseAmount ?? 0m),
            Outcomes = outcomes,
            ContactsList = contacts.Select(c => c with { Outcome = EffectiveOutcome(c, now) ?? c.Outcome }).ToList()
        };
    }

    /// <summary>
    /// Outcome used for reporting: the recorded one, or one derived from delivery state —
    /// failed sends are "no se pudo contactar", sends without reply after 24h are "sin respuesta".
    /// </summary>
    public static string? EffectiveOutcome(CampaignContactExecutionContract c, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(c.Outcome)) return c.Outcome;
        if (c.Status == CampaignContactExecutionStatus.Failed) return CampaignOutcomes.Unreachable;
        if (IsReached(c.Status) && c.RepliedAt is null && now - c.CreatedAt >= NoResponseAfter) return CampaignOutcomes.NoResponse;
        return null;
    }

    private static bool IsReached(CampaignContactExecutionStatus status) => status is
        CampaignContactExecutionStatus.Sent or CampaignContactExecutionStatus.Delivered or
        CampaignContactExecutionStatus.Read or CampaignContactExecutionStatus.Connected or
        CampaignContactExecutionStatus.WorkflowStarted;

    private async Task<CampaignContactExecutionContract> ApplyAsync(
        CampaignContactExecutionContract contact,
        string outcome,
        CampaignOutcomeRequest request,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var notes = request.Notes;
        var promiseDate = request.PromiseDate;
        if (outcome == CampaignOutcomes.PromiseToPay && promiseDate is null)
        {
            // The customer promised but did not say when: assume 3 days and say so.
            promiseDate = new DateTimeOffset(now.UtcDateTime.Date.AddDays(3), TimeSpan.Zero);
            notes = string.Join(" ", new[] { notes, "Fecha no indicada; se asumio en 3 dias." }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        var updated = contact with
        {
            Outcome = outcome,
            OutcomeSource = request.Source,
            OutcomeAt = now,
            OutcomeBy = request.By,
            OutcomeNotes = notes,
            PromiseAmount = outcome == CampaignOutcomes.PromiseToPay ? request.PromiseAmount : null,
            PromiseDate = outcome == CampaignOutcomes.PromiseToPay ? promiseDate : null,
            UpdatedAt = now
        };
        updated = await _campaigns.UpdateContactExecutionAsync(updated, ct) ?? updated;

        var samePromise = contact.Outcome == CampaignOutcomes.PromiseToPay && contact.PromiseDate == promiseDate && contact.PromiseAmount == request.PromiseAmount;
        if (outcome == CampaignOutcomes.PromiseToPay && !samePromise && !string.IsNullOrWhiteSpace(contact.PartyId) && promiseDate is not null)
        {
            await _promises.CreateAsync(
                contact.TenantId,
                contact.PartyId!,
                request.PromiseAmount,
                promiseDate.Value,
                request.Source,
                request.By,
                notes,
                contact.CampaignId,
                contact.Id,
                ct: ct);
        }

        return updated;
    }

    private static DateTimeOffset ToLocal(DateTimeOffset value, string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone)) return value;
        try { return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timezone)); }
        catch (Exception) { return value; }
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
