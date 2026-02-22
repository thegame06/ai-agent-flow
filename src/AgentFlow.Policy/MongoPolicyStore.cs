using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Policy;

// =========================================================================
// MONGO POLICY STORE — Production-grade policy persistence
// =========================================================================

/// <summary>
/// MongoDB-backed policy store for PolicySet definitions.
/// 
/// Design decisions:
/// - PolicySets are tenant-scoped (multi-tenancy at data level)
/// - Published PolicySets are cached per tenant for runtime performance
/// - Version history is preserved (never deleted, only deprecated)
/// - Index: { TenantId: 1, PolicySetId: 1, Version: -1 } ensures fast lookup
/// </summary>
public sealed class MongoPolicyStore : IPolicyStore
{
    private readonly IMongoCollection<PolicySetDocument> _collection;
    private readonly ILogger<MongoPolicyStore> _logger;

    public MongoPolicyStore(IMongoDatabase database, ILogger<MongoPolicyStore> logger)
    {
        _collection = database.GetCollection<PolicySetDocument>("policy_sets");
        _logger = logger;

        // Ensure indices for tenant-scoped lookups
        var indexBuilder = Builders<PolicySetDocument>.IndexKeys;
        _collection.Indexes.CreateMany([
            new CreateIndexModel<PolicySetDocument>(
                indexBuilder.Ascending(d => d.TenantId)
                    .Ascending(d => d.PolicySetId)
                    .Descending(d => d.Version),
                new CreateIndexOptions { Name = "idx_tenant_policyset_version" }),
            new CreateIndexModel<PolicySetDocument>(
                indexBuilder.Ascending(d => d.TenantId)
                    .Ascending(d => d.IsPublished),
                new CreateIndexOptions { Name = "idx_tenant_published" })
        ]);
    }

    /// <summary>
    /// Gets the latest published version of a PolicySet for a tenant.
    /// </summary>
    public async Task<PolicySetDefinition?> GetPolicySetAsync(
        string policySetId, string tenantId, CancellationToken ct = default)
    {
        var filter = Builders<PolicySetDocument>.Filter.And(
            Builders<PolicySetDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<PolicySetDocument>.Filter.Eq(d => d.PolicySetId, policySetId),
            Builders<PolicySetDocument>.Filter.Eq(d => d.IsPublished, true)
        );

        var document = await _collection
            .Find(filter)
            .SortByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (document is null)
        {
            _logger.LogDebug("PolicySet '{PolicySetId}' not found for tenant '{TenantId}'",
                policySetId, tenantId);
            return null;
        }

        return MapToDefinition(document);
    }

    /// <summary>
    /// Gets all published PolicySet IDs for a tenant (used by DSL validation).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetPublishedPolicySetIdsAsync(
        string tenantId, CancellationToken ct = default)
    {
        var filter = Builders<PolicySetDocument>.Filter.And(
            Builders<PolicySetDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<PolicySetDocument>.Filter.Eq(d => d.IsPublished, true)
        );

        var documents = await _collection.Find(filter)
            .Project(d => d.PolicySetId)
            .ToListAsync(ct);

        return documents.Distinct().ToList();
    }

    /// <summary>
    /// Saves a new PolicySet version.
    /// </summary>
    public async Task<Result> SavePolicySetAsync(
        PolicySetDefinition definition, CancellationToken ct = default)
    {
        try
        {
            var document = MapToDocument(definition);
            await _collection.InsertOneAsync(document, cancellationToken: ct);

            _logger.LogInformation(
                "PolicySet '{PolicySetId}' v{Version} saved for tenant '{TenantId}' [Published={IsPublished}]",
                definition.PolicySetId, definition.Version, definition.TenantId, definition.IsPublished);

            return Result.Success();
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return Result.Failure(Error.Validation("PolicySet",
                $"PolicySet '{definition.PolicySetId}' v{definition.Version} already exists."));
        }
    }

    /// <summary>
    /// Publishes a draft PolicySet. Once published, it becomes immutable.
    /// </summary>
    public async Task<Result> PublishPolicySetAsync(
        string policySetId, string version, string tenantId, CancellationToken ct = default)
    {
        var filter = Builders<PolicySetDocument>.Filter.And(
            Builders<PolicySetDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<PolicySetDocument>.Filter.Eq(d => d.PolicySetId, policySetId),
            Builders<PolicySetDocument>.Filter.Eq(d => d.Version, version),
            Builders<PolicySetDocument>.Filter.Eq(d => d.IsPublished, false)
        );

        var update = Builders<PolicySetDocument>.Update
            .Set(d => d.IsPublished, true)
            .Set(d => d.PublishedAt, DateTimeOffset.UtcNow);

        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount == 0)
        {
            return Result.Failure(Error.Validation("PolicySet",
                $"PolicySet '{policySetId}' v{version} not found or already published."));
        }

        _logger.LogInformation(
            "PolicySet '{PolicySetId}' v{Version} published for tenant '{TenantId}'",
            policySetId, version, tenantId);

        return Result.Success();
    }

    /// <summary>
    /// Gets version history for a PolicySet.
    /// </summary>
    public async Task<IReadOnlyList<PolicySetDefinition>> GetVersionHistoryAsync(
        string policySetId, string tenantId, int limit = 10, CancellationToken ct = default)
    {
        var filter = Builders<PolicySetDocument>.Filter.And(
            Builders<PolicySetDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<PolicySetDocument>.Filter.Eq(d => d.PolicySetId, policySetId)
        );

        var documents = await _collection.Find(filter)
            .SortByDescending(d => d.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);

        return documents.Select(MapToDefinition).ToList();
    }

    // --- Mapping ---

    private static PolicySetDefinition MapToDefinition(PolicySetDocument doc) => new()
    {
        PolicySetId = doc.PolicySetId,
        Version = doc.Version,
        TenantId = doc.TenantId,
        IsPublished = doc.IsPublished,
        Policies = doc.Policies.Select(p => new PolicyDefinition
        {
            PolicyId = p.PolicyId,
            Description = p.Description,
            AppliesAt = Enum.Parse<PolicyCheckpoint>(p.AppliesAt, ignoreCase: true),
            PolicyType = p.PolicyType,
            Action = Enum.Parse<PolicyAction>(p.Action, ignoreCase: true),
            Severity = Enum.Parse<PolicySeverity>(p.Severity, ignoreCase: true),
            IsEnabled = p.IsEnabled,
            Config = p.Config
        }).ToList()
    };

    private static PolicySetDocument MapToDocument(PolicySetDefinition def) => new()
    {
        PolicySetId = def.PolicySetId,
        Version = def.Version,
        TenantId = def.TenantId,
        IsPublished = def.IsPublished,
        Policies = def.Policies.Select(p => new PolicyDocEntry
        {
            PolicyId = p.PolicyId,
            Description = p.Description,
            AppliesAt = p.AppliesAt.ToString(),
            PolicyType = p.PolicyType,
            Action = p.Action.ToString(),
            Severity = p.Severity.ToString(),
            IsEnabled = p.IsEnabled,
            Config = p.Config.ToDictionary(kv => kv.Key, kv => kv.Value)
        }).ToList()
    };
}

// =========================================================================
// MONGODB DOCUMENTS (internal to persistence layer)
// =========================================================================

internal sealed class PolicySetDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string PolicySetId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public List<PolicyDocEntry> Policies { get; set; } = [];
}

internal sealed class PolicyDocEntry
{
    public string PolicyId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AppliesAt { get; set; } = string.Empty;
    public string PolicyType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Dictionary<string, string> Config { get; set; } = new();
}
