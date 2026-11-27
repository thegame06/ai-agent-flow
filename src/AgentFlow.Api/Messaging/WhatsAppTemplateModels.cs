using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Messaging;

/// <summary>Meta template statuses we act on. Only APPROVED templates can be sent.</summary>
public static class WhatsAppTemplateStatuses
{
    public const string Approved = "APPROVED";
    public const string Pending = "PENDING";
    public const string Rejected = "REJECTED";
    public const string Paused = "PAUSED";
    public const string Disabled = "DISABLED";
    /// <summary>No longer returned by Meta (deleted there).</summary>
    public const string Deleted = "DELETED";
}

public sealed class WhatsAppTemplateButton
{
    /// <summary>QUICK_REPLY, URL, PHONE_NUMBER or COPY_CODE.</summary>
    public string Type { get; set; } = "QUICK_REPLY";
    public string Text { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? PhoneNumber { get; set; }
}

public sealed class WhatsAppTemplateComponent
{
    /// <summary>HEADER, BODY, FOOTER or BUTTONS.</summary>
    public string Type { get; set; } = "BODY";
    /// <summary>HEADER only: TEXT, IMAGE, VIDEO, DOCUMENT or LOCATION.</summary>
    public string? Format { get; set; }
    public string? Text { get; set; }
    public List<WhatsAppTemplateButton>? Buttons { get; set; }
}

/// <summary>Where the value of one template placeholder comes from when the template is sent.</summary>
public sealed class WhatsAppTemplateParameter
{
    /// <summary>The placeholder: "1", "2"... (positional) or "primer_nombre" (named).</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>header, body, header_media or button:{index}.</summary>
    public string Component { get; set; } = "body";
    /// <summary>field (a contact variable such as total_vencido) or static (a fixed value).</summary>
    public string Source { get; set; } = "field";
    public string? Field { get; set; }
    public string? StaticValue { get; set; }
    /// <summary>Sample value: required by Meta to review the template, and used in previews.</summary>
    public string? Example { get; set; }
}

public sealed class WhatsAppTemplateStatusChange
{
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string Source { get; set; } = "sync";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A WhatsApp message template of a tenant's Meta account, kept in sync with Meta.</summary>
[BsonIgnoreExtraElements]
public sealed class WhatsAppTemplateDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string? BusinessAccountId { get; set; }
    public string? MetaTemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "es";
    /// <summary>UTILITY, MARKETING or AUTHENTICATION.</summary>
    public string Category { get; set; } = "UTILITY";
    public string Status { get; set; } = WhatsAppTemplateStatuses.Pending;
    public string? RejectedReason { get; set; }
    public string? QualityScore { get; set; }
    /// <summary>POSITIONAL ({{1}}) or NAMED ({{primer_nombre}}).</summary>
    public string ParameterFormat { get; set; } = "POSITIONAL";
    public List<WhatsAppTemplateComponent> Components { get; set; } = [];
    /// <summary>How each placeholder is filled. Kept across syncs.</summary>
    public List<WhatsAppTemplateParameter> Parameters { get; set; } = [];
    /// <summary>Goes up each time the content changes (in AgentFlow or in Meta).</summary>
    public int Version { get; set; } = 1;
    public string ContentHash { get; set; } = string.Empty;
    /// <summary>meta (found by a sync) or agentflow (created here).</summary>
    public string Origin { get; set; } = "meta";
    public List<WhatsAppTemplateStatusChange> StatusHistory { get; set; } = [];
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>The content of a template as it was in one version.</summary>
[BsonIgnoreExtraElements]
public sealed class WhatsAppTemplateVersionDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<WhatsAppTemplateComponent> Components { get; set; } = [];
    public string ContentHash { get; set; } = string.Empty;
    /// <summary>create, edit or sync.</summary>
    public string Source { get; set; } = "sync";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
}

public interface IWhatsAppTemplateStore
{
    Task<IReadOnlyList<WhatsAppTemplateDocument>> ListAsync(string tenantId, CancellationToken ct = default);
    Task<WhatsAppTemplateDocument?> GetAsync(string tenantId, string id, CancellationToken ct = default);
    Task<WhatsAppTemplateDocument?> FindByMetaIdAsync(string tenantId, string metaTemplateId, CancellationToken ct = default);
    Task SaveAsync(WhatsAppTemplateDocument template, CancellationToken ct = default);
    Task DeleteAsync(string tenantId, string id, CancellationToken ct = default);
    Task AddVersionAsync(WhatsAppTemplateVersionDocument version, CancellationToken ct = default);
    Task<IReadOnlyList<WhatsAppTemplateVersionDocument>> GetVersionsAsync(string tenantId, string templateId, CancellationToken ct = default);
    /// <summary>Tenants that have templates (for the periodic sync).</summary>
    Task<IReadOnlyList<string>> GetTenantIdsAsync(CancellationToken ct = default);
}

public sealed class MongoWhatsAppTemplateStore : IWhatsAppTemplateStore
{
    private readonly IMongoCollection<WhatsAppTemplateDocument> _templates;
    private readonly IMongoCollection<WhatsAppTemplateVersionDocument> _versions;

    public MongoWhatsAppTemplateStore(IMongoDatabase database)
    {
        _templates = database.GetCollection<WhatsAppTemplateDocument>("whatsapp_templates");
        _versions = database.GetCollection<WhatsAppTemplateVersionDocument>("whatsapp_template_versions");
        _templates.Indexes.CreateOne(new CreateIndexModel<WhatsAppTemplateDocument>(
            Builders<WhatsAppTemplateDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.ConnectionId).Ascending(x => x.Name).Ascending(x => x.Language)));
        _templates.Indexes.CreateOne(new CreateIndexModel<WhatsAppTemplateDocument>(
            Builders<WhatsAppTemplateDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.MetaTemplateId)));
        _versions.Indexes.CreateOne(new CreateIndexModel<WhatsAppTemplateVersionDocument>(
            Builders<WhatsAppTemplateVersionDocument>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.TemplateId).Descending(x => x.Version)));
    }

    public async Task<IReadOnlyList<WhatsAppTemplateDocument>> ListAsync(string tenantId, CancellationToken ct = default)
        => await _templates.Find(x => x.TenantId == tenantId).SortBy(x => x.Name).ThenBy(x => x.Language).ToListAsync(ct);

    public async Task<WhatsAppTemplateDocument?> GetAsync(string tenantId, string id, CancellationToken ct = default)
        => await _templates.Find(x => x.TenantId == tenantId && x.Id == id).FirstOrDefaultAsync(ct);

    public async Task<WhatsAppTemplateDocument?> FindByMetaIdAsync(string tenantId, string metaTemplateId, CancellationToken ct = default)
        => await _templates.Find(x => x.TenantId == tenantId && x.MetaTemplateId == metaTemplateId).FirstOrDefaultAsync(ct);

    public Task SaveAsync(WhatsAppTemplateDocument template, CancellationToken ct = default)
        => _templates.ReplaceOneAsync(x => x.Id == template.Id && x.TenantId == template.TenantId, template, new ReplaceOptions { IsUpsert = true }, ct);

    public Task DeleteAsync(string tenantId, string id, CancellationToken ct = default)
        => _templates.DeleteOneAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public Task AddVersionAsync(WhatsAppTemplateVersionDocument version, CancellationToken ct = default)
        => _versions.InsertOneAsync(version, cancellationToken: ct);

    public async Task<IReadOnlyList<WhatsAppTemplateVersionDocument>> GetVersionsAsync(string tenantId, string templateId, CancellationToken ct = default)
        => await _versions.Find(x => x.TenantId == tenantId && x.TemplateId == templateId).SortByDescending(x => x.Version).Limit(50).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> GetTenantIdsAsync(CancellationToken ct = default)
        => await (await _templates.DistinctAsync(x => x.TenantId, FilterDefinition<WhatsAppTemplateDocument>.Empty, cancellationToken: ct)).ToListAsync(ct);
}
