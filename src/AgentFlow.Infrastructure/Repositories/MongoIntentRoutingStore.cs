using AgentFlow.Security;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Infrastructure.Repositories;

public sealed class MongoIntentRoutingStore : IIntentRoutingStore
{
    private readonly IMongoCollection<IntentRoutingRuleDocument> _rules;
    private readonly IMongoCollection<AgentRegistryDocument> _agents;
    private readonly ILogger<MongoIntentRoutingStore> _logger;

    public MongoIntentRoutingStore(IMongoDatabase database, ILogger<MongoIntentRoutingStore> logger)
    {
        _rules = database.GetCollection<IntentRoutingRuleDocument>("intent_rules");
        _agents = database.GetCollection<AgentRegistryDocument>("agent_registry");
        _logger = logger;
    }

    public async Task<IReadOnlyList<IntentRoutingRule>> GetRulesAsync(string tenantId, CancellationToken ct = default)
    {
        var docs = await _rules.Find(x => x.TenantId == tenantId)
            .SortBy(x => x.IntentKey)
            .ThenBy(x => x.Priority)
            .ToListAsync(ct);

        return docs.Select(ToModel).ToList();
    }

    public async Task<IntentRoutingRule?> GetRuleByIdAsync(string tenantId, string ruleId, CancellationToken ct = default)
    {
        var doc = await _rules.Find(x => x.TenantId == tenantId && x.Id == ruleId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToModel(doc);
    }

    public async Task<IntentRoutingRule> UpsertRuleAsync(IntentRoutingRule rule, CancellationToken ct = default)
    {
        var existing = await _rules.Find(x => x.TenantId == rule.TenantId && x.Id == rule.Id).FirstOrDefaultAsync(ct);
        var version = existing is null ? 1 : existing.Version + 1;

        var doc = new IntentRoutingRuleDocument
        {
            Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id,
            TenantId = rule.TenantId,
            IntentKey = rule.IntentKey,
            SourceAgentId = rule.SourceAgentId,
            TargetAgentId = rule.TargetAgentId,
            Priority = rule.Priority,
            Enabled = rule.Enabled,
            Channel = rule.Channel,
            ConditionsJson = rule.ConditionsJson,
            HandoffPolicyJson = rule.HandoffPolicyJson,
            Version = version,
            CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _rules.ReplaceOneAsync(
            x => x.TenantId == doc.TenantId && x.Id == doc.Id,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return ToModel(doc);
    }

    public async Task<bool> SetRuleEnabledAsync(string tenantId, string ruleId, bool enabled, CancellationToken ct = default)
    {
        var update = Builders<IntentRoutingRuleDocument>.Update
            .Set(x => x.Enabled, enabled)
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow)
            .Inc(x => x.Version, 1);

        var result = await _rules.UpdateOneAsync(x => x.TenantId == tenantId && x.Id == ruleId, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<IReadOnlyList<AgentRegistryEntry>> GetAgentsAsync(string tenantId, CancellationToken ct = default)
    {
        var docs = await _agents.Find(x => x.TenantId == tenantId)
            .SortBy(x => x.AgentId)
            .ToListAsync(ct);

        return docs.Select(ToModel).ToList();
    }

    public async Task<AgentRegistryEntry> UpsertAgentAsync(AgentRegistryEntry agent, CancellationToken ct = default)
    {
        var doc = new AgentRegistryDocument
        {
            Id = string.IsNullOrWhiteSpace(agent.Id) ? Guid.NewGuid().ToString("N") : agent.Id,
            TenantId = agent.TenantId,
            AgentId = agent.AgentId,
            AgentType = agent.AgentType,
            Enabled = agent.Enabled,
            TestModeAllowed = agent.TestModeAllowed,
            ExternalReplyAllowed = agent.ExternalReplyAllowed,
            Capabilities = agent.Capabilities.ToList(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _agents.ReplaceOneAsync(
            x => x.TenantId == doc.TenantId && x.AgentId == doc.AgentId,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return ToModel(doc);
    }

    public async Task<IntentRuleSimulationResult> SimulateAsync(string tenantId, string sourceAgentId, string intent, string? channel, CancellationToken ct = default)
    {
        var rules = await _rules.Find(x =>
                x.TenantId == tenantId &&
                x.SourceAgentId == sourceAgentId &&
                x.IntentKey == intent &&
                x.Enabled &&
                (x.Channel == null || x.Channel == "" || x.Channel == channel))
            .SortBy(x => x.Priority)
            .ThenBy(x => x.UpdatedAt)
            .ToListAsync(ct);

        if (rules.Count == 0)
        {
            return new IntentRuleSimulationResult
            {
                IntentDetected = intent,
                MatchedRuleId = null,
                SelectedAgentId = sourceAgentId,
                FallbackUsed = true,
                DecisionReason = "no_matching_rule"
            };
        }

        var selected = rules[0];
        return new IntentRuleSimulationResult
        {
            IntentDetected = intent,
            MatchedRuleId = selected.Id,
            SelectedAgentId = selected.TargetAgentId,
            FallbackUsed = false,
            DecisionReason = "rule_selected_by_priority"
        };
    }

    private static IntentRoutingRule ToModel(IntentRoutingRuleDocument x) => new()
    {
        Id = x.Id,
        TenantId = x.TenantId,
        IntentKey = x.IntentKey,
        SourceAgentId = x.SourceAgentId,
        TargetAgentId = x.TargetAgentId,
        Priority = x.Priority,
        Enabled = x.Enabled,
        Channel = x.Channel,
        ConditionsJson = x.ConditionsJson,
        HandoffPolicyJson = x.HandoffPolicyJson,
        Version = x.Version,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };

    private static AgentRegistryEntry ToModel(AgentRegistryDocument x) => new()
    {
        Id = x.Id,
        TenantId = x.TenantId,
        AgentId = x.AgentId,
        AgentType = x.AgentType,
        Enabled = x.Enabled,
        TestModeAllowed = x.TestModeAllowed,
        ExternalReplyAllowed = x.ExternalReplyAllowed,
        Capabilities = x.Capabilities,
        UpdatedAt = x.UpdatedAt
    };

    private sealed class IntentRoutingRuleDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string TenantId { get; set; } = string.Empty;
        public string IntentKey { get; set; } = string.Empty;
        public string SourceAgentId { get; set; } = string.Empty;
        public string TargetAgentId { get; set; } = string.Empty;
        public int Priority { get; set; }
        public bool Enabled { get; set; } = true;
        public string? Channel { get; set; }
        public string? ConditionsJson { get; set; }
        public string? HandoffPolicyJson { get; set; }
        public int Version { get; set; } = 1;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class AgentRegistryDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string TenantId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string AgentType { get; set; } = "subagent";
        public bool Enabled { get; set; } = true;
        public bool TestModeAllowed { get; set; } = false;
        public bool ExternalReplyAllowed { get; set; } = false;
        public List<string> Capabilities { get; set; } = new();
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
