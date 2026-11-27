namespace AgentFlow.Api.Collections;

public interface ICollectionPromiseService
{
    /// <summary>Records a promise to pay for a customer; returns null when the customer has no loans.</summary>
    Task<CollectionPromiseDocument?> CreateAsync(
        string tenantId,
        string partyId,
        decimal? amount,
        DateTimeOffset promisedDate,
        string source,
        string createdBy,
        string? notes = null,
        string? campaignId = null,
        string? contactExecutionId = null,
        IReadOnlyCollection<string>? loanIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves pending promises against the current portfolio: kept when the overdue balance of
    /// the promised loans dropped by the promised amount (or to zero), broken when the promised
    /// date is before <paramref name="asOf"/> without that. Returns how many changed.
    /// </summary>
    Task<int> ResolveAsync(string tenantId, DateTimeOffset asOf, string? partyId = null, CancellationToken ct = default);
}

public sealed class CollectionPromiseService : ICollectionPromiseService
{
    // A payment within 10% of the promised amount counts as kept (rounding, fees).
    private const decimal KeptTolerance = 0.9m;

    private readonly ICollectionsStore _store;

    public CollectionPromiseService(ICollectionsStore store) => _store = store;

    public async Task<CollectionPromiseDocument?> CreateAsync(
        string tenantId,
        string partyId,
        decimal? amount,
        DateTimeOffset promisedDate,
        string source,
        string createdBy,
        string? notes = null,
        string? campaignId = null,
        string? contactExecutionId = null,
        IReadOnlyCollection<string>? loanIds = null,
        CancellationToken ct = default)
    {
        var loans = await _store.GetLoansByPartyAsync(tenantId, partyId, ct);
        if (loans.Count == 0) return null;

        var promised = loanIds is { Count: > 0 }
            ? loans.Where(l => loanIds.Contains(l.Id)).ToList()
            : loans.Where(l => l.OverdueBalance > 0 && l.Status != CollectionLoanStatus.PaidOff).ToList();
        if (promised.Count == 0) promised = loans.ToList();

        // A customer has one live promise: a new one replaces any earlier pending promise.
        foreach (var earlier in await _store.GetPromisesAsync(tenantId, CollectionPromiseStatus.Pending, partyId, 50, ct))
        {
            earlier.Status = CollectionPromiseStatus.Cancelled;
            earlier.ResolvedAt = DateTimeOffset.UtcNow;
            earlier.Notes = string.Join(" ", new[] { earlier.Notes, "Reemplazada por una nueva promesa." }.Where(x => !string.IsNullOrWhiteSpace(x)));
            await _store.UpdatePromiseAsync(earlier, ct);
        }

        var promise = new CollectionPromiseDocument
        {
            TenantId = tenantId,
            PartyId = partyId,
            CustomerName = loans[0].CustomerName,
            LoanIds = promised.Select(l => l.Id).ToList(),
            Amount = amount is > 0 ? amount : null,
            PromisedDate = promisedDate,
            OverdueAtCreation = promised.Sum(l => l.OverdueBalance),
            Source = source,
            CampaignId = campaignId,
            ContactExecutionId = contactExecutionId,
            Notes = notes,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _store.InsertPromiseAsync(promise, ct);
        return promise;
    }

    public async Task<int> ResolveAsync(string tenantId, DateTimeOffset asOf, string? partyId = null, CancellationToken ct = default)
    {
        var pending = await _store.GetPromisesAsync(tenantId, CollectionPromiseStatus.Pending, partyId, 5000, ct);
        var changed = 0;
        foreach (var group in pending.GroupBy(p => p.PartyId))
        {
            var loans = (await _store.GetLoansByPartyAsync(tenantId, group.Key, ct)).ToDictionary(l => l.Id);
            foreach (var promise in group)
            {
                var current = promise.LoanIds.Sum(id => loans.TryGetValue(id, out var loan) ? loan.OverdueBalance : 0m);
                var paid = promise.OverdueAtCreation - current;
                var target = promise.Amount ?? promise.OverdueAtCreation;
                var kept = current <= 0 || (target > 0 && paid >= target * KeptTolerance);

                if (kept)
                    promise.Status = CollectionPromiseStatus.Kept;
                else if (promise.PromisedDate.Date < asOf.Date)
                    promise.Status = CollectionPromiseStatus.Broken;
                else
                    continue;

                promise.OverdueAtResolution = current;
                promise.ResolvedAt = DateTimeOffset.UtcNow;
                await _store.UpdatePromiseAsync(promise, ct);
                changed++;
            }
        }
        return changed;
    }
}
