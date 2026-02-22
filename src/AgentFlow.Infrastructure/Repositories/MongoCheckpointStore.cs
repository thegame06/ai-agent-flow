using AgentFlow.Abstractions;
using MongoDB.Driver;

namespace AgentFlow.Infrastructure.Repositories;

public sealed class MongoCheckpointStore : ICheckpointStore
{
    private readonly IMongoCollection<AgentCheckpoint> _collection;

    public MongoCheckpointStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<AgentCheckpoint>("checkpoints");
        
        // Ensure indices
        var indexKeys = Builders<AgentCheckpoint>.IndexKeys
            .Ascending(x => x.TenantId)
            .Ascending(x => x.ExecutionId);
        
        _collection.Indexes.CreateOne(new CreateIndexModel<AgentCheckpoint>(indexKeys, new CreateIndexOptions { Unique = true }));
    }

    public async Task SaveAsync(AgentCheckpoint checkpoint, CancellationToken ct = default)
    {
        await _collection.ReplaceOneAsync(
            x => x.ExecutionId == checkpoint.ExecutionId && x.TenantId == checkpoint.TenantId,
            checkpoint,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    public async Task<AgentCheckpoint?> GetAsync(string executionId, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.ExecutionId == executionId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task DeleteAsync(string executionId, string tenantId, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(x => x.ExecutionId == executionId && x.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<AgentCheckpoint>> GetPendingAsync(string tenantId, int limit = 50, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.TenantId == tenantId)
            .Limit(limit)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }
}
