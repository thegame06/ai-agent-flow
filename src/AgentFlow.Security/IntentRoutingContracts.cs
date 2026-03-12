namespace AgentFlow.Security;

public sealed record IntentRoutingRule
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string IntentKey { get; init; }
    public required string SourceAgentId { get; init; }
    public required string TargetAgentId { get; init; }
    public required int Priority { get; init; }
    public required bool Enabled { get; init; }
    public string? Channel { get; init; }
    public string? ConditionsJson { get; init; }
    public string? HandoffPolicyJson { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record AgentRegistryEntry
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentType { get; init; } // manager|subagent
    public required bool Enabled { get; init; }
    public required bool TestModeAllowed { get; init; }
    public required bool ExternalReplyAllowed { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record IntentRuleSimulationResult
{
    public required string IntentDetected { get; init; }
    public string? MatchedRuleId { get; init; }
    public required string SelectedAgentId { get; init; }
    public required bool FallbackUsed { get; init; }
    public required string DecisionReason { get; init; }
}

public interface IIntentRoutingStore
{
    Task<IReadOnlyList<IntentRoutingRule>> GetRulesAsync(string tenantId, CancellationToken ct = default);
    Task<IntentRoutingRule?> GetRuleByIdAsync(string tenantId, string ruleId, CancellationToken ct = default);
    Task<IntentRoutingRule> UpsertRuleAsync(IntentRoutingRule rule, CancellationToken ct = default);
    Task<bool> SetRuleEnabledAsync(string tenantId, string ruleId, bool enabled, CancellationToken ct = default);

    Task<IReadOnlyList<AgentRegistryEntry>> GetAgentsAsync(string tenantId, CancellationToken ct = default);
    Task<AgentRegistryEntry> UpsertAgentAsync(AgentRegistryEntry agent, CancellationToken ct = default);

    Task<IntentRuleSimulationResult> SimulateAsync(
        string tenantId,
        string sourceAgentId,
        string intent,
        string? channel,
        CancellationToken ct = default);
}
