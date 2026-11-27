using AgentFlow.Extensions;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Entitlements;

/// <summary>
/// Durable per-tenant app entitlements. Every read goes to Mongo (no local cache), so all
/// API instances see the same state and nothing is lost on restart or redeploy.
/// Collections: tenant_extension_states, tenant_extension_allowlists, extension_quarantine.
/// </summary>
public sealed class MongoTenantExtensionStateStore : ITenantExtensionStateStore
{
    private readonly IMongoCollection<TenantExtensionStateDocument> _states;
    private readonly IMongoCollection<TenantExtensionAllowlistDocument> _allowlists;
    private readonly IMongoCollection<ExtensionQuarantineDocument> _quarantine;

    public MongoTenantExtensionStateStore(IMongoDatabase database)
    {
        _states = database.GetCollection<TenantExtensionStateDocument>("tenant_extension_states");
        _allowlists = database.GetCollection<TenantExtensionAllowlistDocument>("tenant_extension_allowlists");
        _quarantine = database.GetCollection<ExtensionQuarantineDocument>("extension_quarantine");

        _states.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<TenantExtensionStateDocument>(
                Builders<TenantExtensionStateDocument>.IndexKeys.Ascending(x => x.TenantId)),
            new CreateIndexModel<TenantExtensionStateDocument>(
                Builders<TenantExtensionStateDocument>.IndexKeys.Ascending(x => x.ExtensionId))
        });
    }

    public async Task<IReadOnlyDictionary<string, bool>> GetStatesAsync(string tenantId, CancellationToken ct = default)
    {
        var docs = await _states.Find(x => x.TenantId == tenantId).ToListAsync(ct);
        return docs.ToDictionary(x => x.ExtensionId, x => x.Enabled);
    }

    public Task SetStateAsync(string tenantId, string extensionId, bool enabled, CancellationToken ct = default)
    {
        var doc = new TenantExtensionStateDocument
        {
            Id = StateId(tenantId, extensionId),
            TenantId = tenantId,
            ExtensionId = extensionId,
            Enabled = enabled,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return _states.ReplaceOneAsync(x => x.Id == doc.Id, doc, new ReplaceOptions { IsUpsert = true }, ct);
    }

    public Task RemoveStateAsync(string tenantId, string extensionId, CancellationToken ct = default)
    {
        var id = StateId(tenantId, extensionId);
        return _states.DeleteOneAsync(x => x.Id == id, ct);
    }

    public async Task<IReadOnlySet<string>> GetAllowlistAsync(string tenantId, CancellationToken ct = default)
    {
        var doc = await _allowlists.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync(ct);
        return doc?.ExtensionIds.ToHashSet() ?? new HashSet<string>();
    }

    public Task SetAllowlistAsync(string tenantId, IReadOnlyCollection<string> extensionIds, CancellationToken ct = default)
    {
        var doc = new TenantExtensionAllowlistDocument
        {
            TenantId = tenantId,
            ExtensionIds = extensionIds.Distinct().ToList(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return _allowlists.ReplaceOneAsync(x => x.TenantId == tenantId, doc, new ReplaceOptions { IsUpsert = true }, ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetQuarantinedAsync(CancellationToken ct = default)
    {
        var docs = await _quarantine.Find(FilterDefinition<ExtensionQuarantineDocument>.Empty).ToListAsync(ct);
        return docs.ToDictionary(x => x.ExtensionId, x => x.Reason);
    }

    public async Task QuarantineAsync(string extensionId, string reason, CancellationToken ct = default)
    {
        var doc = new ExtensionQuarantineDocument
        {
            ExtensionId = extensionId,
            Reason = reason,
            QuarantinedAt = DateTimeOffset.UtcNow
        };

        await _quarantine.ReplaceOneAsync(x => x.ExtensionId == extensionId, doc, new ReplaceOptions { IsUpsert = true }, ct);
        await _states.UpdateManyAsync(
            x => x.ExtensionId == extensionId,
            Builders<TenantExtensionStateDocument>.Update
                .Set(x => x.Enabled, false)
                .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow),
            cancellationToken: ct);
    }

    private static string StateId(string tenantId, string extensionId) => $"{tenantId}:{extensionId}";

    [BsonIgnoreExtraElements]
    private sealed class TenantExtensionStateDocument
    {
        [BsonId] public string Id { get; set; } = string.Empty;
        [BsonElement("tenantId")] public string TenantId { get; set; } = string.Empty;
        [BsonElement("extensionId")] public string ExtensionId { get; set; } = string.Empty;
        [BsonElement("enabled")] public bool Enabled { get; set; }
        [BsonElement("updatedAt")] public DateTimeOffset UpdatedAt { get; set; }
    }

    [BsonIgnoreExtraElements]
    private sealed class TenantExtensionAllowlistDocument
    {
        [BsonId] public string TenantId { get; set; } = string.Empty;
        [BsonElement("extensionIds")] public List<string> ExtensionIds { get; set; } = [];
        [BsonElement("updatedAt")] public DateTimeOffset UpdatedAt { get; set; }
    }

    [BsonIgnoreExtraElements]
    private sealed class ExtensionQuarantineDocument
    {
        [BsonId] public string ExtensionId { get; set; } = string.Empty;
        [BsonElement("reason")] public string Reason { get; set; } = string.Empty;
        [BsonElement("quarantinedAt")] public DateTimeOffset QuarantinedAt { get; set; }
    }
}
