namespace AgentFlow.Api.Collections;

/// <summary>One payment to register. The loan is found by the lender's id or by AgentFlow's id.</summary>
public sealed record CollectionPaymentInput
{
    public string LoanId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
    public string? Reference { get; init; }
}

public sealed record CollectionPaymentResult
{
    public int Applied { get; init; }
    public int Duplicates { get; init; }
    public int PromisesResolved { get; init; }
    public IReadOnlyList<CollectionImportError> Errors { get; init; } = [];
    public IReadOnlyList<CollectionPaymentDocument> Payments { get; init; } = [];
}

public interface ICollectionPaymentService
{
    Task<CollectionPaymentResult> RegisterAsync(
        string tenantId,
        IReadOnlyList<CollectionPaymentInput> payments,
        string source,
        string createdBy,
        CancellationToken ct = default);
}

/// <summary>
/// Applies payments to loans as they arrive, so a promise is closed the same day instead of at
/// the next portfolio cut-off. A payment covers the overdue balance first (penalties, fees,
/// interest, principal) and lowers the total balance; the next import still overwrites the loan
/// with the lender's own numbers.
/// </summary>
public sealed class CollectionPaymentService : ICollectionPaymentService
{
    private readonly ICollectionsStore _store;
    private readonly ICollectionPromiseService _promises;

    public CollectionPaymentService(ICollectionsStore store, ICollectionPromiseService promises)
    {
        _store = store;
        _promises = promises;
    }

    public async Task<CollectionPaymentResult> RegisterAsync(
        string tenantId,
        IReadOnlyList<CollectionPaymentInput> payments,
        string source,
        string createdBy,
        CancellationToken ct = default)
    {
        var errors = new List<CollectionImportError>();
        var applied = new List<CollectionPaymentDocument>();
        var duplicates = 0;
        var parties = new HashSet<string>();
        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var externalIds = payments.Select(p => p.LoanId?.Trim() ?? string.Empty).Where(x => x.Length > 0).Distinct().ToList();
        var byExternal = new Dictionary<string, CollectionLoanDocument>(await _store.GetLoansByExternalIdsAsync(tenantId, externalIds, ct), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < payments.Count; i++)
        {
            var row = i + 1;
            var input = payments[i];
            var loanKey = input.LoanId?.Trim() ?? string.Empty;
            if (loanKey.Length == 0) { errors.Add(new(row, "loan_id", "Falta el prestamo.")); continue; }
            if (input.Amount <= 0) { errors.Add(new(row, "amount", "El monto debe ser mayor a cero.")); continue; }

            var reference = string.IsNullOrWhiteSpace(input.Reference) ? null : input.Reference.Trim();
            if (reference is not null &&
                (!seenReferences.Add(reference) || await _store.PaymentReferenceExistsAsync(tenantId, reference, ct)))
            {
                duplicates++;
                continue;
            }

            var loan = byExternal.GetValueOrDefault(loanKey) ?? await _store.GetLoanAsync(tenantId, loanKey, ct);
            if (loan is null) { errors.Add(new(row, "loan_id", $"No existe el prestamo {loanKey}.")); continue; }

            var overdueBefore = loan.OverdueBalance;
            Apply(loan, input.Amount, input.PaidAt ?? DateTimeOffset.UtcNow);
            await _store.UpsertLoanAsync(loan, ct);

            var payment = new CollectionPaymentDocument
            {
                TenantId = tenantId,
                LoanId = loan.Id,
                LoanExternalId = loan.LoanExternalId,
                PartyId = loan.PartyId,
                Amount = input.Amount,
                PaidAt = input.PaidAt ?? DateTimeOffset.UtcNow,
                Reference = reference,
                Source = source,
                OverdueBefore = overdueBefore,
                OverdueAfter = loan.OverdueBalance,
                CreatedBy = createdBy
            };
            await _store.InsertPaymentAsync(payment, ct);
            applied.Add(payment);
            parties.Add(loan.PartyId);
        }

        var resolved = 0;
        foreach (var partyId in parties)
            resolved += await _promises.ResolveAsync(tenantId, DateTimeOffset.UtcNow, partyId, ct);

        return new CollectionPaymentResult
        {
            Applied = applied.Count,
            Duplicates = duplicates,
            PromisesResolved = resolved,
            Errors = errors,
            Payments = applied
        };
    }

    /// <summary>Covers penalties, fees, interest and principal (in that order), then the rest of the balance.</summary>
    public static void Apply(CollectionLoanDocument loan, decimal amount, DateTimeOffset paidAt)
    {
        // An overdue total imported without its breakdown is treated as principal.
        var breakdown = loan.OverduePrincipal + loan.OverdueInterest + loan.OverduePenalties + loan.OverdueFees;
        if (loan.OverdueBalance > breakdown)
            loan.OverduePrincipal += loan.OverdueBalance - breakdown;

        var remaining = amount;
        loan.OverduePenalties = Take(loan.OverduePenalties, ref remaining);
        loan.OverdueFees = Take(loan.OverdueFees, ref remaining);
        loan.OverdueInterest = Take(loan.OverdueInterest, ref remaining);
        loan.OverduePrincipal = Take(loan.OverduePrincipal, ref remaining);
        loan.OverdueBalance = loan.OverduePrincipal + loan.OverdueInterest + loan.OverduePenalties + loan.OverdueFees;
        loan.TotalBalance = Math.Max(0, loan.TotalBalance - amount);

        if (loan.OverdueBalance <= 0)
        {
            loan.DaysPastDue = 0;
            loan.Stage = DelinquencyStage.Current;
        }
        if (loan.TotalBalance <= 0)
            loan.Status = CollectionLoanStatus.PaidOff;

        loan.LastPaymentDate = paidAt;
        loan.LastPaymentAmount = amount;
        loan.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static decimal Take(decimal bucket, ref decimal remaining)
    {
        var paid = Math.Min(bucket, remaining);
        remaining -= paid;
        return bucket - paid;
    }
}
