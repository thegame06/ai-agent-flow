using AgentFlow.Api.Commerce;

namespace AgentFlow.Api.Collections;

public interface ICollectionsImportService
{
    /// <summary>
    /// Validates and (unless <paramref name="dryRun"/>) applies a portfolio import: one row per loan,
    /// grouped by customer. Loans are upserted by the lender's loan id, so re-importing updates.
    /// </summary>
    Task<CollectionImportResult> ImportAsync(
        string tenantId,
        IReadOnlyList<(int RowNumber, CollectionLoanRow Row)> rows,
        IReadOnlyList<CollectionImportError> parseErrors,
        int totalRows,
        DateTimeOffset? snapshotDate,
        string source,
        string? fileName,
        bool dryRun,
        string requestedBy,
        CancellationToken ct = default);
}

public sealed class CollectionsImportService : ICollectionsImportService
{
    private const int MaxStoredErrors = 200;
    private readonly ICollectionsStore _store;
    private readonly ICommerceStore _parties;

    public CollectionsImportService(ICollectionsStore store, ICommerceStore parties)
    {
        _store = store;
        _parties = parties;
    }

    public async Task<CollectionImportResult> ImportAsync(
        string tenantId,
        IReadOnlyList<(int RowNumber, CollectionLoanRow Row)> rows,
        IReadOnlyList<CollectionImportError> parseErrors,
        int totalRows,
        DateTimeOffset? snapshotDate,
        string source,
        string? fileName,
        bool dryRun,
        string requestedBy,
        CancellationToken ct = default)
    {
        var snapshot = new DateTimeOffset(DateTime.SpecifyKind((snapshotDate ?? DateTimeOffset.UtcNow).UtcDateTime.Date, DateTimeKind.Utc));
        var errors = new List<CollectionImportError>(parseErrors);
        var valid = new List<(int RowNumber, CollectionLoanRow Row, string CustomerKey, CollectionLoanStatus Status)>();
        var seenLoanIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rowNumber, row) in rows)
        {
            var rowErrors = Validate(rowNumber, row, out var customerKey, out var status);
            if (rowErrors.Count == 0 && !seenLoanIds.Add(row.LoanId!))
                rowErrors.Add(new CollectionImportError(rowNumber, "loan_id", $"El préstamo '{row.LoanId}' está repetido en el archivo."));

            if (rowErrors.Count > 0) errors.AddRange(rowErrors);
            else valid.Add((rowNumber, row, customerKey!, status));
        }

        var existing = await _store.GetLoansByExternalIdsAsync(tenantId, valid.Select(v => v.Row.LoanId!).ToList(), ct);
        var customers = valid.Select(v => v.CustomerKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var failedRows = errors.Where(e => e.Row > 1).Select(e => e.Row).Distinct().Count();

        if (dryRun)
        {
            return new CollectionImportResult
            {
                DryRun = true,
                SnapshotDate = snapshot,
                TotalRows = totalRows,
                ValidRows = valid.Count,
                Customers = customers,
                LoansCreated = valid.Count(v => !existing.ContainsKey(v.Row.LoanId!)),
                LoansUpdated = valid.Count(v => existing.ContainsKey(v.Row.LoanId!)),
                Failed = failedRows,
                Errors = errors.Take(MaxStoredErrors).ToList()
            };
        }

        var batchId = Guid.NewGuid().ToString("N");
        var partyIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int created = 0, updated = 0, applied = 0;

        foreach (var (rowNumber, row, customerKey, status) in valid)
        {
            try
            {
                if (!partyIds.TryGetValue(customerKey, out var partyId))
                {
                    // Resolved by phone first, so a customer already known from WhatsApp is linked, not duplicated.
                    var party = await _parties.UpsertPartyByChannelIdentityAsync(
                        tenantId, "external", customerKey, row.FullName, "customer",
                        sourceSessionId: null, sourceThreadId: null, row.Phone, row.Email, row.FullName, ct);
                    partyId = party.Id;
                    partyIds[customerKey] = partyId;
                }

                var isNew = !existing.TryGetValue(row.LoanId!, out var loan);
                loan ??= new CollectionLoanDocument { TenantId = tenantId, LoanExternalId = row.LoanId!, CreatedAt = DateTimeOffset.UtcNow };
                Apply(loan, row, partyId, status, snapshot, batchId);

                await _store.UpsertLoanAsync(loan, ct);
                await _store.UpsertSnapshotAsync(new CollectionLoanSnapshotDocument
                {
                    Id = $"{loan.Id}:{snapshot:yyyyMMdd}",
                    TenantId = tenantId,
                    LoanId = loan.Id,
                    SnapshotDate = snapshot,
                    DaysPastDue = loan.DaysPastDue,
                    Stage = loan.Stage,
                    TotalBalance = loan.TotalBalance,
                    OverdueBalance = loan.OverdueBalance,
                    Status = loan.Status
                }, ct);

                applied++;
                if (isNew) created++; else updated++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(new CollectionImportError(rowNumber, "row", ex.Message));
                failedRows++;
            }
        }

        var storedErrors = errors.Take(MaxStoredErrors).ToList();
        await _store.InsertImportBatchAsync(new CollectionImportBatchDocument
        {
            Id = batchId,
            TenantId = tenantId,
            Source = source,
            FileName = fileName,
            SnapshotDate = snapshot,
            TotalRows = totalRows,
            Customers = partyIds.Count,
            LoansCreated = created,
            LoansUpdated = updated,
            Failed = failedRows,
            Errors = storedErrors,
            CreatedBy = requestedBy,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);

        return new CollectionImportResult
        {
            DryRun = false,
            BatchId = batchId,
            SnapshotDate = snapshot,
            TotalRows = totalRows,
            ValidRows = applied,
            Customers = partyIds.Count,
            LoansCreated = created,
            LoansUpdated = updated,
            Failed = failedRows,
            Errors = storedErrors
        };
    }

    private static List<CollectionImportError> Validate(int rowNumber, CollectionLoanRow row, out string? customerKey, out CollectionLoanStatus status)
    {
        var errors = new List<CollectionImportError>();
        customerKey = !string.IsNullOrWhiteSpace(row.CustomerId) ? row.CustomerId.Trim()
            : !string.IsNullOrWhiteSpace(row.Phone) ? row.Phone.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(row.LoanId))
            errors.Add(new CollectionImportError(rowNumber, "loan_id", "El id del préstamo es obligatorio."));
        if (customerKey is null)
            errors.Add(new CollectionImportError(rowNumber, "customer_id", "Se necesita customer_id o phone para identificar al cliente."));
        if (row.TotalBalance is null)
            errors.Add(new CollectionImportError(rowNumber, "total_balance", "El saldo total es obligatorio."));

        foreach (var (field, value) in new[]
                 {
                     ("total_balance", row.TotalBalance), ("overdue_principal", row.OverduePrincipal),
                     ("overdue_interest", row.OverdueInterest), ("overdue_penalties", row.OverduePenalties),
                     ("overdue_fees", row.OverdueFees), ("overdue_balance", row.OverdueBalance)
                 })
        {
            if (value < 0) errors.Add(new CollectionImportError(rowNumber, field, "El monto no puede ser negativo."));
        }

        if (row.DaysPastDue < 0)
            errors.Add(new CollectionImportError(rowNumber, "days_past_due", "Los días de atraso no pueden ser negativos."));

        if (!TryParseStatus(row.Status, out status))
            errors.Add(new CollectionImportError(rowNumber, "status", $"Estado '{row.Status}' no reconocido (use activo, liquidado, castigado o reestructurado)."));

        return errors;
    }

    private static void Apply(CollectionLoanDocument loan, CollectionLoanRow row, string partyId, CollectionLoanStatus status, DateTimeOffset snapshot, string batchId)
    {
        var overdueComponents = (row.OverduePrincipal ?? 0) + (row.OverdueInterest ?? 0) + (row.OverduePenalties ?? 0) + (row.OverdueFees ?? 0);
        var daysPastDue = row.DaysPastDue
            ?? (row.OldestDueDate is { } oldest ? Math.Max(0, (int)(snapshot - oldest).TotalDays) : 0);
        if (status is CollectionLoanStatus.PaidOff) daysPastDue = 0;

        loan.PartyId = partyId;
        loan.CustomerExternalId = row.CustomerId?.Trim();
        loan.CustomerName = row.FullName ?? loan.CustomerName;
        loan.CustomerPhone = row.Phone ?? loan.CustomerPhone;
        loan.Product = row.Product ?? loan.Product;
        loan.Currency = string.IsNullOrWhiteSpace(row.Currency) ? (loan.Currency ?? "MXN") : row.Currency.Trim().ToUpperInvariant();
        loan.OriginalAmount = row.OriginalAmount ?? loan.OriginalAmount;
        loan.DisbursedAt = row.DisbursedAt ?? loan.DisbursedAt;
        loan.PaymentFrequency = row.PaymentFrequency ?? loan.PaymentFrequency;

        loan.TotalBalance = row.TotalBalance ?? 0;
        loan.OverduePrincipal = row.OverduePrincipal ?? 0;
        loan.OverdueInterest = row.OverdueInterest ?? 0;
        loan.OverduePenalties = row.OverduePenalties ?? 0;
        loan.OverdueFees = row.OverdueFees ?? 0;
        loan.OverdueBalance = row.OverdueBalance ?? overdueComponents;

        loan.DaysPastDue = daysPastDue;
        loan.Stage = DelinquencyStages.FromDaysPastDue(daysPastDue);
        loan.Status = status;
        loan.NextPaymentDate = row.NextPaymentDate;
        loan.NextPaymentAmount = row.NextPaymentAmount;
        loan.LastPaymentDate = row.LastPaymentDate ?? loan.LastPaymentDate;
        loan.LastPaymentAmount = row.LastPaymentAmount ?? loan.LastPaymentAmount;
        if (row.Installments is { Count: > 0 }) loan.Installments = row.Installments;

        loan.SnapshotDate = snapshot;
        loan.LastImportBatchId = batchId;
        loan.UpdatedAt = DateTimeOffset.UtcNow;
    }

    internal static bool TryParseStatus(string? raw, out CollectionLoanStatus status)
    {
        status = CollectionLoanStatus.Active;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        switch (raw.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_"))
        {
            case "activo": case "active": case "vigente": case "al_corriente":
                status = CollectionLoanStatus.Active; return true;
            case "liquidado": case "pagado": case "paid_off": case "paidoff": case "closed":
                status = CollectionLoanStatus.PaidOff; return true;
            case "castigado": case "charged_off": case "chargedoff": case "write_off":
                status = CollectionLoanStatus.ChargedOff; return true;
            case "reestructurado": case "restructured":
                status = CollectionLoanStatus.Restructured; return true;
            default:
                return false;
        }
    }
}
