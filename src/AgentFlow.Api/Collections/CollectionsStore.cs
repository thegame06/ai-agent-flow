using MongoDB.Driver;

namespace AgentFlow.Api.Collections;

public interface ICollectionsStore
{
    Task<IReadOnlyDictionary<string, CollectionLoanDocument>> GetLoansByExternalIdsAsync(string tenantId, IReadOnlyCollection<string> loanExternalIds, CancellationToken ct = default);
    Task UpsertLoanAsync(CollectionLoanDocument loan, CancellationToken ct = default);
    Task UpsertSnapshotAsync(CollectionLoanSnapshotDocument snapshot, CancellationToken ct = default);

    Task<IReadOnlyList<CollectionLoanDocument>> GetLoansAsync(string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<CollectionLoanDocument>> GetLoansByPartyAsync(string tenantId, string partyId, CancellationToken ct = default);
    Task<CollectionLoanDocument?> GetLoanAsync(string tenantId, string loanId, CancellationToken ct = default);
    Task<IReadOnlyList<CollectionLoanSnapshotDocument>> GetSnapshotsAsync(string tenantId, string loanId, int limit, CancellationToken ct = default);

    Task InsertImportBatchAsync(CollectionImportBatchDocument batch, CancellationToken ct = default);
    Task<IReadOnlyList<CollectionImportBatchDocument>> GetImportBatchesAsync(string tenantId, int limit, CancellationToken ct = default);

    Task InsertPaymentAsync(CollectionPaymentDocument payment, CancellationToken ct = default);
    Task<IReadOnlyList<CollectionPaymentDocument>> GetPaymentsByLoanAsync(string tenantId, string loanId, int limit = 50, CancellationToken ct = default);
    Task<bool> PaymentReferenceExistsAsync(string tenantId, string reference, CancellationToken ct = default);
    Task<IReadOnlyList<CollectionPaymentDocument>> GetPaymentsBetweenAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task<IReadOnlyList<CollectionLoanSnapshotDocument>> GetSnapshotsBetweenAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task InsertPromiseAsync(CollectionPromiseDocument promise, CancellationToken ct = default);
    Task UpdatePromiseAsync(CollectionPromiseDocument promise, CancellationToken ct = default);
    Task<CollectionPromiseDocument?> GetPromiseAsync(string tenantId, string promiseId, CancellationToken ct = default);
    Task<IReadOnlyList<CollectionPromiseDocument>> GetPromisesAsync(string tenantId, CollectionPromiseStatus? status = null, string? partyId = null, int limit = 500, CancellationToken ct = default);
}

public sealed class MongoCollectionsStore : ICollectionsStore
{
    private readonly IMongoCollection<CollectionLoanDocument> _loans;
    private readonly IMongoCollection<CollectionLoanSnapshotDocument> _snapshots;
    private readonly IMongoCollection<CollectionImportBatchDocument> _batches;
    private readonly IMongoCollection<CollectionPromiseDocument> _promises;
    private readonly IMongoCollection<CollectionPaymentDocument> _payments;

    public MongoCollectionsStore(IMongoDatabase database)
    {
        _loans = database.GetCollection<CollectionLoanDocument>("collection_loans");
        _snapshots = database.GetCollection<CollectionLoanSnapshotDocument>("collection_loan_snapshots");
        _batches = database.GetCollection<CollectionImportBatchDocument>("collection_import_batches");
        _promises = database.GetCollection<CollectionPromiseDocument>("collection_promises");
        _payments = database.GetCollection<CollectionPaymentDocument>("collection_payments");
        _payments.Indexes.CreateOne(new CreateIndexModel<CollectionPaymentDocument>(
            Builders<CollectionPaymentDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.LoanId).Descending(x => x.PaidAt)));
        _payments.Indexes.CreateOne(new CreateIndexModel<CollectionPaymentDocument>(
            Builders<CollectionPaymentDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.Reference)));
        _promises.Indexes.CreateOne(new CreateIndexModel<CollectionPromiseDocument>(
            Builders<CollectionPromiseDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.PartyId).Descending(x => x.CreatedAt)));
        _promises.Indexes.CreateOne(new CreateIndexModel<CollectionPromiseDocument>(
            Builders<CollectionPromiseDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.Status).Ascending(x => x.PromisedDate)));

        var loanKeys = Builders<CollectionLoanDocument>.IndexKeys;
        _loans.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<CollectionLoanDocument>(
                loanKeys.Ascending(x => x.TenantId).Ascending(x => x.LoanExternalId),
                new CreateIndexOptions { Name = "ux_collection_loan_external", Unique = true }),
            new CreateIndexModel<CollectionLoanDocument>(
                loanKeys.Ascending(x => x.TenantId).Ascending(x => x.PartyId)),
            new CreateIndexModel<CollectionLoanDocument>(
                loanKeys.Ascending(x => x.TenantId).Descending(x => x.DaysPastDue))
        });
        _snapshots.Indexes.CreateOne(new CreateIndexModel<CollectionLoanSnapshotDocument>(
            Builders<CollectionLoanSnapshotDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.LoanId).Descending(x => x.SnapshotDate)));
        _batches.Indexes.CreateOne(new CreateIndexModel<CollectionImportBatchDocument>(
            Builders<CollectionImportBatchDocument>.IndexKeys.Ascending(x => x.TenantId).Descending(x => x.CreatedAt)));
    }

    public async Task<IReadOnlyDictionary<string, CollectionLoanDocument>> GetLoansByExternalIdsAsync(string tenantId, IReadOnlyCollection<string> loanExternalIds, CancellationToken ct = default)
    {
        if (loanExternalIds.Count == 0) return new Dictionary<string, CollectionLoanDocument>();
        var docs = await _loans.Find(x => x.TenantId == tenantId && loanExternalIds.Contains(x.LoanExternalId)).ToListAsync(ct);
        return docs.ToDictionary(x => x.LoanExternalId, StringComparer.OrdinalIgnoreCase);
    }

    public Task UpsertLoanAsync(CollectionLoanDocument loan, CancellationToken ct = default)
        => _loans.ReplaceOneAsync(x => x.Id == loan.Id && x.TenantId == loan.TenantId, loan, new ReplaceOptions { IsUpsert = true }, ct);

    public Task UpsertSnapshotAsync(CollectionLoanSnapshotDocument snapshot, CancellationToken ct = default)
        => _snapshots.ReplaceOneAsync(x => x.Id == snapshot.Id, snapshot, new ReplaceOptions { IsUpsert = true }, ct);

    public async Task<IReadOnlyList<CollectionLoanDocument>> GetLoansAsync(string tenantId, CancellationToken ct = default)
        => await _loans.Find(x => x.TenantId == tenantId).ToListAsync(ct);

    public async Task<IReadOnlyList<CollectionLoanDocument>> GetLoansByPartyAsync(string tenantId, string partyId, CancellationToken ct = default)
        => await _loans.Find(x => x.TenantId == tenantId && x.PartyId == partyId).SortByDescending(x => x.DaysPastDue).ToListAsync(ct);

    public async Task<CollectionLoanDocument?> GetLoanAsync(string tenantId, string loanId, CancellationToken ct = default)
        => await _loans.Find(x => x.TenantId == tenantId && x.Id == loanId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CollectionLoanSnapshotDocument>> GetSnapshotsAsync(string tenantId, string loanId, int limit, CancellationToken ct = default)
        => await _snapshots.Find(x => x.TenantId == tenantId && x.LoanId == loanId).SortByDescending(x => x.SnapshotDate).Limit(limit).ToListAsync(ct);

    public Task InsertImportBatchAsync(CollectionImportBatchDocument batch, CancellationToken ct = default)
        => _batches.InsertOneAsync(batch, cancellationToken: ct);

    public async Task<IReadOnlyList<CollectionImportBatchDocument>> GetImportBatchesAsync(string tenantId, int limit, CancellationToken ct = default)
        => await _batches.Find(x => x.TenantId == tenantId).SortByDescending(x => x.CreatedAt).Limit(limit).ToListAsync(ct);

    public Task InsertPromiseAsync(CollectionPromiseDocument promise, CancellationToken ct = default)
        => _promises.InsertOneAsync(promise, cancellationToken: ct);

    public Task UpdatePromiseAsync(CollectionPromiseDocument promise, CancellationToken ct = default)
        => _promises.ReplaceOneAsync(x => x.Id == promise.Id && x.TenantId == promise.TenantId, promise, cancellationToken: ct);

    public async Task<CollectionPromiseDocument?> GetPromiseAsync(string tenantId, string promiseId, CancellationToken ct = default)
        => await _promises.Find(x => x.TenantId == tenantId && x.Id == promiseId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CollectionPromiseDocument>> GetPromisesAsync(string tenantId, CollectionPromiseStatus? status = null, string? partyId = null, int limit = 500, CancellationToken ct = default)
    {
        var filter = Builders<CollectionPromiseDocument>.Filter.Eq(x => x.TenantId, tenantId);
        if (status.HasValue)
            filter &= Builders<CollectionPromiseDocument>.Filter.Eq(x => x.Status, status.Value);
        if (!string.IsNullOrWhiteSpace(partyId))
            filter &= Builders<CollectionPromiseDocument>.Filter.Eq(x => x.PartyId, partyId);
        return await _promises.Find(filter).SortByDescending(x => x.CreatedAt).Limit(Math.Clamp(limit, 1, 5000)).ToListAsync(ct);
    }

    public Task InsertPaymentAsync(CollectionPaymentDocument payment, CancellationToken ct = default)
        => _payments.InsertOneAsync(payment, cancellationToken: ct);

    public async Task<IReadOnlyList<CollectionPaymentDocument>> GetPaymentsByLoanAsync(string tenantId, string loanId, int limit = 50, CancellationToken ct = default)
        => await _payments.Find(x => x.TenantId == tenantId && x.LoanId == loanId).SortByDescending(x => x.PaidAt).Limit(Math.Clamp(limit, 1, 500)).ToListAsync(ct);

    public async Task<bool> PaymentReferenceExistsAsync(string tenantId, string reference, CancellationToken ct = default)
        => await _payments.Find(x => x.TenantId == tenantId && x.Reference == reference).AnyAsync(ct);

    public async Task<IReadOnlyList<CollectionPaymentDocument>> GetPaymentsBetweenAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => await _payments.Find(x => x.TenantId == tenantId && x.PaidAt >= from && x.PaidAt <= to).ToListAsync(ct);

    public async Task<IReadOnlyList<CollectionLoanSnapshotDocument>> GetSnapshotsBetweenAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => await _snapshots.Find(x => x.TenantId == tenantId && x.SnapshotDate >= from && x.SnapshotDate <= to).ToListAsync(ct);
}
