using AgentFlow.Application.Memory;
using MongoDB.Driver;

namespace AgentFlow.Infrastructure.Memory;

public sealed class MongoAuditMemory : IAuditMemory
{
    private readonly IMongoCollection<AuditEntry> _collection;

    public MongoAuditMemory(IMongoDatabase database)
    {
        _collection = database.GetCollection<AuditEntry>("audit_logs");
        
        // Ensure index for performance
        var executionIndex = Builders<AuditEntry>.IndexKeys
            .Ascending(x => x.ExecutionId)
            .Ascending(x => x.TenantId);
        
        var tenantRecentIndex = Builders<AuditEntry>.IndexKeys
            .Ascending(x => x.TenantId)
            .Descending(x => x.OccurredAt);

        var correlationIndex = Builders<AuditEntry>.IndexKeys
            .Ascending(x => x.TenantId)
            .Ascending(x => x.CorrelationId)
            .Descending(x => x.OccurredAt);

        _collection.Indexes.CreateOne(new CreateIndexModel<AuditEntry>(executionIndex));
        _collection.Indexes.CreateOne(new CreateIndexModel<AuditEntry>(tenantRecentIndex));
        _collection.Indexes.CreateOne(new CreateIndexModel<AuditEntry>(correlationIndex));
    }

    public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(entry, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetForExecutionAsync(string executionId, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.ExecutionId == executionId && x.TenantId == tenantId)
            .SortBy(x => x.OccurredAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetForAgentAsync(string agentId, string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.AgentId == agentId && x.TenantId == tenantId && x.OccurredAt >= from && x.OccurredAt <= to)
            .SortByDescending(x => x.OccurredAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetRecentAsync(string tenantId, int limit = 100, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.TenantId == tenantId)
            .SortByDescending(x => x.OccurredAt)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetByCorrelationAsync(string tenantId, string correlationId, int limit = 200, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.TenantId == tenantId && x.CorrelationId == correlationId)
            .SortByDescending(x => x.OccurredAt)
            .Limit(limit)
            .ToListAsync(ct);
    }
}
