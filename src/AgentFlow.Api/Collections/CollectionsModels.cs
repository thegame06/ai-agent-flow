using MongoDB.Bson.Serialization.Attributes;

namespace AgentFlow.Api.Collections;

public enum CollectionLoanStatus
{
    Active,
    PaidOff,
    ChargedOff,
    Restructured
}

/// <summary>Delinquency bucket derived from days past due.</summary>
public enum DelinquencyStage
{
    Current,
    Dpd1To30,
    Dpd31To60,
    Dpd61To90,
    Dpd90Plus
}

public static class DelinquencyStages
{
    public static DelinquencyStage FromDaysPastDue(int daysPastDue) => daysPastDue switch
    {
        <= 0 => DelinquencyStage.Current,
        <= 30 => DelinquencyStage.Dpd1To30,
        <= 60 => DelinquencyStage.Dpd31To60,
        <= 90 => DelinquencyStage.Dpd61To90,
        _ => DelinquencyStage.Dpd90Plus
    };

    public static string Label(DelinquencyStage stage) => stage switch
    {
        DelinquencyStage.Current => "Al corriente",
        DelinquencyStage.Dpd1To30 => "1-30 días",
        DelinquencyStage.Dpd31To60 => "31-60 días",
        DelinquencyStage.Dpd61To90 => "61-90 días",
        _ => "90+ días"
    };
}

/// <summary>One loan as received from the lender (a CSV row or an API batch item).</summary>
public sealed record CollectionLoanRow
{
    /// <summary>Lender's customer id. A customer can own several loans.</summary>
    public string? CustomerId { get; init; }
    public string? FullName { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }

    /// <summary>Lender's loan id; unique per tenant, so re-importing updates instead of duplicating.</summary>
    public string? LoanId { get; init; }
    public string? Product { get; init; }
    public string? Currency { get; init; }
    public decimal? OriginalAmount { get; init; }
    public DateTimeOffset? DisbursedAt { get; init; }
    public string? PaymentFrequency { get; init; }

    public decimal? TotalBalance { get; init; }
    public decimal? OverduePrincipal { get; init; }
    public decimal? OverdueInterest { get; init; }
    public decimal? OverduePenalties { get; init; }
    public decimal? OverdueFees { get; init; }
    /// <summary>Optional; when absent it is the sum of the overdue components.</summary>
    public decimal? OverdueBalance { get; init; }

    /// <summary>Optional; when absent it is derived from <see cref="OldestDueDate"/> and the snapshot date.</summary>
    public int? DaysPastDue { get; init; }
    public DateTimeOffset? OldestDueDate { get; init; }
    public DateTimeOffset? NextPaymentDate { get; init; }
    public decimal? NextPaymentAmount { get; init; }
    public DateTimeOffset? LastPaymentDate { get; init; }
    public decimal? LastPaymentAmount { get; init; }
    public string? Status { get; init; }

    public List<CollectionInstallment>? Installments { get; init; }
}

public sealed class CollectionInstallment
{
    public int Number { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    /// <summary>pending | paid | partial | overdue</summary>
    public string Status { get; set; } = "pending";
}

[BsonIgnoreExtraElements]
public sealed class CollectionLoanDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string LoanExternalId { get; set; } = string.Empty;

    public string PartyId { get; set; } = string.Empty;
    public string? CustomerExternalId { get; set; }
    // Denormalized for search and listing without joining parties.
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }

    public string? Product { get; set; }
    public string Currency { get; set; } = "MXN";
    public decimal? OriginalAmount { get; set; }
    public DateTimeOffset? DisbursedAt { get; set; }
    public string? PaymentFrequency { get; set; }

    public decimal TotalBalance { get; set; }
    public decimal OverduePrincipal { get; set; }
    public decimal OverdueInterest { get; set; }
    public decimal OverduePenalties { get; set; }
    public decimal OverdueFees { get; set; }
    public decimal OverdueBalance { get; set; }

    public int DaysPastDue { get; set; }
    public DelinquencyStage Stage { get; set; }
    public CollectionLoanStatus Status { get; set; }

    public DateTimeOffset? NextPaymentDate { get; set; }
    public decimal? NextPaymentAmount { get; set; }
    public DateTimeOffset? LastPaymentDate { get; set; }
    public decimal? LastPaymentAmount { get; set; }
    public List<CollectionInstallment> Installments { get; set; } = [];

    /// <summary>
    /// Where the account came from: "import" (the lender's portfolio) or "sale" (a sale on
    /// installments in AgentFlow, whose lateness AgentFlow computes from the schedule).
    /// </summary>
    public string? Origin { get; set; }
    /// <summary>The sale an installment account finances.</summary>
    public string? SaleId { get; set; }

    /// <summary>Cut-off date of the data (the lender's portfolio snapshot).</summary>
    public DateTimeOffset SnapshotDate { get; set; }
    public string? LastImportBatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Loan state at a cut-off date, kept per import so the evolution can be followed.</summary>
[BsonIgnoreExtraElements]
public sealed class CollectionLoanSnapshotDocument
{
    [BsonId] public string Id { get; set; } = string.Empty; // {loanId}:{yyyyMMdd}
    public string TenantId { get; set; } = string.Empty;
    public string LoanId { get; set; } = string.Empty;
    public DateTimeOffset SnapshotDate { get; set; }
    public int DaysPastDue { get; set; }
    public DelinquencyStage Stage { get; set; }
    public decimal TotalBalance { get; set; }
    public decimal OverdueBalance { get; set; }
    public CollectionLoanStatus Status { get; set; }
}

public sealed record CollectionImportError(int Row, string Field, string Message);

[BsonIgnoreExtraElements]
public sealed class CollectionImportBatchDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    /// <summary>csv | api</summary>
    public string Source { get; set; } = "csv";
    public string? FileName { get; set; }
    public DateTimeOffset SnapshotDate { get; set; }
    public int TotalRows { get; set; }
    public int Customers { get; set; }
    public int LoansCreated { get; set; }
    public int LoansUpdated { get; set; }
    public int Failed { get; set; }
    public List<CollectionImportError> Errors { get; set; } = [];
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record CollectionImportResult
{
    public bool DryRun { get; init; }
    public string? BatchId { get; init; }
    public DateTimeOffset SnapshotDate { get; init; }
    public int TotalRows { get; init; }
    public int ValidRows { get; init; }
    public int Customers { get; init; }
    public int LoansCreated { get; init; }
    public int LoansUpdated { get; init; }
    public int Failed { get; init; }
    public IReadOnlyList<CollectionImportError> Errors { get; init; } = [];
}

public enum CollectionPromiseStatus
{
    Pending,
    Kept,
    Broken,
    Cancelled
}

/// <summary>
/// A promise to pay made by a customer (by WhatsApp reply, an agent, a workflow or an operator).
/// It is resolved against later portfolio cut-offs: kept when the overdue balance went down by
/// the promised amount (or to zero), broken when the promised date passed without that.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class CollectionPromiseDocument
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string PartyId { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public List<string> LoanIds { get; set; } = [];
    public decimal? Amount { get; set; }
    public DateTimeOffset PromisedDate { get; set; }
    public CollectionPromiseStatus Status { get; set; } = CollectionPromiseStatus.Pending;
    /// <summary>Customer overdue balance when the promise was made; the baseline to resolve it.</summary>
    public decimal OverdueAtCreation { get; set; }
    public decimal? OverdueAtResolution { get; set; }
    /// <summary>reply, agent, workflow or human.</summary>
    public string Source { get; set; } = "human";
    public string? CampaignId { get; set; }
    public string? ContactExecutionId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>A payment received for a loan (from the lender's system, a file or an operator).</summary>
[BsonIgnoreExtraElements]
public sealed class CollectionPaymentDocument
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string LoanId { get; set; } = string.Empty;
    public string LoanExternalId { get; set; } = string.Empty;
    public string PartyId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    /// <summary>The lender's payment reference; a repeated reference is ignored.</summary>
    public string? Reference { get; set; }
    /// <summary>
    /// The campaign contact the customer told about this payment (a confirmed report). The recovery
    /// report credits that campaign directly instead of guessing by dates.
    /// </summary>
    public string? ContactExecutionId { get; set; }
    /// <summary>api, human or csv.</summary>
    public string Source { get; set; } = "api";
    public decimal OverdueBefore { get; set; }
    public decimal OverdueAfter { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
}
