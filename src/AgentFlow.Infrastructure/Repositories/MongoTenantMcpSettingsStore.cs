using AgentFlow.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Infrastructure.Repositories;

public sealed class MongoTenantMcpSettingsStore : ITenantMcpSettingsStore
{
    private readonly IMongoCollection<TenantMcpSettingsDocument> _collection;

    public MongoTenantMcpSettingsStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<TenantMcpSettingsDocument>("tenant_mcp_settings");
    }

    public async Task<TenantMcpSettings> GetAsync(string tenantId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync(ct);
        return doc is null ? Default(tenantId) : ToModel(doc);
    }

    public async Task<TenantMcpSettings> SaveAsync(TenantMcpSettings settings, CancellationToken ct = default)
    {
        var doc = new TenantMcpSettingsDocument
        {
            Id = settings.TenantId,
            TenantId = settings.TenantId,
            Enabled = settings.Enabled,
            Runtime = settings.Runtime,
            TimeoutSeconds = settings.TimeoutSeconds,
            RetryCount = settings.RetryCount,
            AllowedServers = settings.AllowedServers.ToArray(),
            UpdatedAt = settings.UpdatedAt,
            UpdatedBy = settings.UpdatedBy
        };

        await _collection.ReplaceOneAsync(x => x.TenantId == settings.TenantId, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return ToModel(doc);
    }

    private static TenantMcpSettings Default(string tenantId) => new()
    {
        TenantId = tenantId,
        Enabled = false,
        Runtime = "MicrosoftAgentFramework",
        TimeoutSeconds = 20,
        RetryCount = 1,
        AllowedServers = Array.Empty<string>(),
        UpdatedAt = DateTimeOffset.UtcNow,
        UpdatedBy = "system"
    };

    private static TenantMcpSettings ToModel(TenantMcpSettingsDocument d) => new()
    {
        TenantId = d.TenantId,
        Enabled = d.Enabled,
        Runtime = d.Runtime,
        TimeoutSeconds = d.TimeoutSeconds,
        RetryCount = d.RetryCount,
        AllowedServers = d.AllowedServers ?? Array.Empty<string>(),
        UpdatedAt = d.UpdatedAt,
        UpdatedBy = d.UpdatedBy
    };

    private sealed class TenantMcpSettingsDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;

        public string TenantId { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Runtime { get; set; } = "MicrosoftAgentFramework";
        public int TimeoutSeconds { get; set; } = 20;
        public int RetryCount { get; set; } = 1;
        public string[]? AllowedServers { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string? UpdatedBy { get; set; }
    }
}
