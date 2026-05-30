using AgentFlow.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Infrastructure.Repositories;

public sealed class MongoCheckpointStore : ICheckpointStore
{
    private readonly IMongoCollection<CheckpointDocument> _collection;

    public MongoCheckpointStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<CheckpointDocument>("checkpoints");
        
        // Ensure indices
        var indexKeys = Builders<CheckpointDocument>.IndexKeys
            .Ascending(x => x.TenantId)
            .Ascending(x => x.ExecutionId);
        
        _collection.Indexes.CreateOne(new CreateIndexModel<CheckpointDocument>(indexKeys, new CreateIndexOptions { Unique = true }));
    }

    public async Task SaveAsync(AgentCheckpoint checkpoint, CancellationToken ct = default)
    {
        await _collection.ReplaceOneAsync(
            x => x.ExecutionId == checkpoint.ExecutionId && x.TenantId == checkpoint.TenantId,
            Map(checkpoint),
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    public async Task<AgentCheckpoint?> GetAsync(string executionId, string tenantId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.ExecutionId == executionId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);
        return doc is null ? null : Map(doc);
    }

    public async Task DeleteAsync(string executionId, string tenantId, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(x => x.ExecutionId == executionId && x.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<AgentCheckpoint>> GetPendingAsync(string tenantId, int limit = 50, CancellationToken ct = default)
    {
        var docs = await _collection.Find(x => x.TenantId == tenantId)
            .Limit(limit)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        return docs.Select(Map).ToList();
    }

    private static CheckpointDocument Map(AgentCheckpoint checkpoint) => new()
    {
        ExecutionId = checkpoint.ExecutionId,
        TenantId = checkpoint.TenantId,
        AgentKey = checkpoint.AgentKey,
        CheckpointId = checkpoint.CheckpointId,
        Reason = checkpoint.Reason,
        ToolName = checkpoint.ToolName,
        ToolInputJson = checkpoint.ToolInputJson,
        LlmRationale = checkpoint.LlmRationale,
        CreatedAt = checkpoint.CreatedAt,
        Context = checkpoint.Context.ToDictionary()
    };

    private static AgentCheckpoint Map(CheckpointDocument doc) => new()
    {
        ExecutionId = doc.ExecutionId,
        TenantId = doc.TenantId,
        AgentKey = doc.AgentKey,
        CheckpointId = doc.CheckpointId,
        Reason = doc.Reason,
        ToolName = doc.ToolName,
        ToolInputJson = doc.ToolInputJson,
        LlmRationale = doc.LlmRationale,
        CreatedAt = doc.CreatedAt,
        Context = doc.Context
    };

    [BsonIgnoreExtraElements]
    private sealed class CheckpointDocument
    {
        [BsonId]
        public ObjectId MongoId { get; set; }
        public string ExecutionId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string AgentKey { get; set; } = string.Empty;
        public string CheckpointId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string? ToolName { get; set; }
        public string? ToolInputJson { get; set; }
        public string? LlmRationale { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public Dictionary<string, string> Context { get; set; } = [];
    }
}
