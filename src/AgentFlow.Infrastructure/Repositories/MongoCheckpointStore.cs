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
        var filter = Builders<CheckpointDocument>.Filter.Where(
            x => x.ExecutionId == checkpoint.ExecutionId && x.TenantId == checkpoint.TenantId);

        var update = Builders<CheckpointDocument>.Update
            .SetOnInsert(x => x.MongoId, ObjectId.GenerateNewId())
            .Set(x => x.ExecutionId, checkpoint.ExecutionId)
            .Set(x => x.TenantId, checkpoint.TenantId)
            .Set(x => x.AgentKey, checkpoint.AgentKey)
            .Set(x => x.CheckpointId, checkpoint.CheckpointId)
            .Set(x => x.Reason, checkpoint.Reason)
            .Set(x => x.ToolName, checkpoint.ToolName)
            .Set(x => x.ToolInputJson, checkpoint.ToolInputJson)
            .Set(x => x.LlmRationale, checkpoint.LlmRationale)
            .Set(x => x.CreatedAt, checkpoint.CreatedAt)
            .Set(x => x.Context, checkpoint.Context.ToDictionary());

        await _collection.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
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
