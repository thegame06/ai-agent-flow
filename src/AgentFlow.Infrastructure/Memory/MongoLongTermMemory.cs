using AgentFlow.Application.Memory;
using MongoDB.Driver;

namespace AgentFlow.Infrastructure.Memory;

public sealed class MongoLongTermMemory : ILongTermMemory
{
    private readonly IMongoCollection<MemoryEntry> _collection;

    public MongoLongTermMemory(IMongoDatabase database)
    {
        _collection = database.GetCollection<MemoryEntry>("agent_memory");
        
        // Index by key, agent and tenant for unique memory points
        var indexKeys = Builders<MemoryEntry>.IndexKeys
            .Ascending(x => x.AgentId)
            .Ascending(x => x.TenantId)
            .Ascending(x => x.Key);
            
        _collection.Indexes.CreateOne(new CreateIndexModel<MemoryEntry>(indexKeys, new CreateIndexOptions { Unique = true }));
    }

    public async Task<string?> RememberAsync(string agentId, string tenantId, string key, CancellationToken ct = default)
    {
        var entry = await _collection.Find(x => x.AgentId == agentId && x.TenantId == tenantId && x.Key == key)
            .FirstOrDefaultAsync(ct);
            
        return entry?.Value;
    }

    public async Task StoreAsync(string agentId, string tenantId, string key, string value, CancellationToken ct = default)
    {
        var update = Builders<MemoryEntry>.Update
            .Set(x => x.Value, value)
            .Set(x => x.StoredAt, DateTimeOffset.UtcNow);

        await _collection.UpdateOneAsync(
            x => x.AgentId == agentId && x.TenantId == tenantId && x.Key == key,
            update,
            new UpdateOptions { IsUpsert = true },
            ct);
    }

    public async Task<IReadOnlyList<MemoryEntry>> SearchByKeyPatternAsync(string agentId, string tenantId, string pattern, CancellationToken ct = default)
    {
        // Simple regex search for keys
        var filter = Builders<MemoryEntry>.Filter.And(
            Builders<MemoryEntry>.Filter.Eq(x => x.AgentId, agentId),
            Builders<MemoryEntry>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<MemoryEntry>.Filter.Regex(x => x.Key, new MongoDB.Bson.BsonRegularExpression(pattern, "i"))
        );

        return await _collection.Find(filter).ToListAsync(ct);
    }

    public async Task ForgetAsync(string agentId, string tenantId, string key, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(x => x.AgentId == agentId && x.TenantId == tenantId && x.Key == key, ct);
    }
}
