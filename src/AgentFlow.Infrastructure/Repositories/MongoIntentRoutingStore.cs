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
            IntentDescription = rule.IntentDescription,
            Category = string.IsNullOrWhiteSpace(rule.Category) ? "General" : rule.Category,
            ExamplePhrases = rule.ExamplePhrases.ToList(),
            SourceAgentId = rule.SourceAgentId,
            TargetAgentId = rule.TargetAgentId,
            WorkflowDefinitionId = rule.WorkflowDefinitionId,
            WorkflowName = rule.WorkflowName,
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

    public async Task<bool> DeleteRuleAsync(string tenantId, string ruleId, CancellationToken ct = default)
    {
        var result = await _rules.DeleteOneAsync(x => x.TenantId == tenantId && x.Id == ruleId, ct);
        return result.DeletedCount > 0;
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

    public async Task<IReadOnlyList<IntentRoutingRule>> GetRulesByChannelAsync(
        string tenantId, string channel, CancellationToken ct = default)
    {
        var normalizedChannel = NormalizeChannel(channel);
        var docs = await _rules
            .Find(x => x.TenantId == tenantId && x.Enabled)
            .SortBy(x => x.Priority)
            .ThenBy(x => x.IntentKey)
            .ToListAsync(ct);

        var filtered = docs
            .Where(x =>
            {
                var ruleChannel = NormalizeChannel(x.Channel);
                return string.IsNullOrEmpty(ruleChannel) ||
                       string.Equals(ruleChannel, normalizedChannel, StringComparison.OrdinalIgnoreCase);
            })
            .Select(ToModel)
            .ToList();

        return filtered;
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
        var normalizedChannel = NormalizeChannel(channel);
        var rules = await _rules.Find(x =>
                x.TenantId == tenantId &&
                x.SourceAgentId == sourceAgentId &&
                x.Enabled)
            .SortBy(x => x.Priority)
            .ThenBy(x => x.UpdatedAt)
            .ToListAsync(ct);

        rules = rules
            .Where(x =>
            {
                var ruleChannel = NormalizeChannel(x.Channel);
                return string.IsNullOrEmpty(ruleChannel) ||
                       string.Equals(ruleChannel, normalizedChannel, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

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

        var exact = rules.FirstOrDefault(x => string.Equals(x.IntentKey, intent, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new IntentRuleSimulationResult
            {
                IntentDetected = intent,
                MatchedRuleId = exact.Id,
                SelectedAgentId = exact.TargetAgentId,
                FallbackUsed = false,
                DecisionReason = "rule_selected_by_priority"
            };
        }

        var probe = NormalizeText(intent);
        var selected = rules
            .Select(x => new { Rule = x, Score = ScoreRule(x, probe) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Rule.Priority)
            .ThenByDescending(x => x.Rule.UpdatedAt)
            .FirstOrDefault();

        if (selected is null)
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

        return new IntentRuleSimulationResult
        {
            IntentDetected = intent,
            MatchedRuleId = selected.Rule.Id,
            SelectedAgentId = selected.Rule.TargetAgentId,
            FallbackUsed = false,
            DecisionReason = "rule_matched_by_examples"
        };
    }

    private static int ScoreRule(IntentRoutingRuleDocument rule, string probe)
    {
        if (string.IsNullOrWhiteSpace(probe))
            return 0;

        var best = 0;
        best = Math.Max(best, ScoreText(rule.IntentKey, probe, 100));
        best = Math.Max(best, ScoreText(rule.IntentDescription, probe, 60));
        foreach (var example in rule.ExamplePhrases)
            best = Math.Max(best, ScoreText(example, probe, 80));
        return best;
    }

    private static int ScoreText(string? candidate, string probe, int exactWeight)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return 0;

        var normalized = NormalizeText(candidate);
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;
        if (string.Equals(normalized, probe, StringComparison.Ordinal))
            return exactWeight;
        if (normalized.Contains(probe, StringComparison.Ordinal) || probe.Contains(normalized, StringComparison.Ordinal))
            return Math.Max(1, exactWeight / 2);

        var probeTokens = probe.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidateTokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (probeTokens.Length == 0 || candidateTokens.Length == 0)
            return 0;

        var overlap = probeTokens.Intersect(candidateTokens, StringComparer.Ordinal).Count();
        return overlap == 0 ? 0 : overlap * 10;
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ')
            .ToArray();
        return string.Join(' ', new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string NormalizeChannel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().ToLowerInvariant();
    }

    private static IntentRoutingRule ToModel(IntentRoutingRuleDocument x) => new()
    {
        Id = x.Id,
        TenantId = x.TenantId,
        IntentKey = x.IntentKey,
        IntentDescription = x.IntentDescription,
        Category = string.IsNullOrWhiteSpace(x.Category) ? "General" : x.Category,
        ExamplePhrases = x.ExamplePhrases.AsReadOnly(),
        SourceAgentId = x.SourceAgentId,
        TargetAgentId = x.TargetAgentId,
        WorkflowDefinitionId = x.WorkflowDefinitionId,
        WorkflowName = x.WorkflowName,
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
        public string IntentDescription { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public List<string> ExamplePhrases { get; set; } = [];
        public string SourceAgentId { get; set; } = string.Empty;
        public string TargetAgentId { get; set; } = string.Empty;
        public string? WorkflowDefinitionId { get; set; }
        public string? WorkflowName { get; set; }
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
