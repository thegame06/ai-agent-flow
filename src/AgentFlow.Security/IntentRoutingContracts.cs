namespace AgentFlow.Security;

public sealed record IntentRoutingRule
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }

    /// <summary>
    /// Unique slug for the intent. snake_case, URL-safe.
    /// Examples: "loan_application", "account_support", "complaint"
    /// The Router LLM calls af_trigger_intent(IntentKey) when it detects this intent.
    /// </summary>
    public required string IntentKey { get; init; }

    /// <summary>
    /// Human-readable description injected into the Router's system prompt.
    /// The LLM uses this to classify customer messages against the intent.
    /// Be specific: "Customer wants to apply for a personal or car loan"
    /// </summary>
    public string IntentDescription { get; init; } = string.Empty;
    public string Category { get; init; } = "General";

    /// <summary>
    /// Example phrases that represent this intent (fed to the Router LLM as few-shot examples).
    /// The more specific the examples, the better the classification accuracy.
    /// </summary>
    public IReadOnlyList<string> ExamplePhrases { get; init; } = [];

    /// <summary>
    /// The agent that LISTENS for incoming messages on the channel (usually the Router).
    /// </summary>
    public required string SourceAgentId { get; init; }

    /// <summary>
    /// The agent that EXECUTES the conversation when this intent is detected (usually the WorkflowBrain).
    /// </summary>
    public required string TargetAgentId { get; init; }

    /// <summary>
    /// Optional: the WorkflowDefinition to trigger when this intent is matched.
    /// If null the platform only does an agent handoff without starting a workflow.
    /// </summary>
    public string? WorkflowDefinitionId { get; init; }

    /// <summary>Snapshot of the workflow name for display.</summary>
    public string? WorkflowName { get; init; }

    public required int Priority { get; init; }
    public required bool Enabled { get; init; }

    /// <summary>Channel slug this rule applies to (null = all channels).</summary>
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
    Task<bool> DeleteRuleAsync(string tenantId, string ruleId, CancellationToken ct = default);
    Task<bool> SetRuleEnabledAsync(string tenantId, string ruleId, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Returns all enabled rules for a specific channel.
    /// Used by the Router agent to build its intent classification prompt.
    /// </summary>
    Task<IReadOnlyList<IntentRoutingRule>> GetRulesByChannelAsync(string tenantId, string channel, CancellationToken ct = default);

    Task<IReadOnlyList<AgentRegistryEntry>> GetAgentsAsync(string tenantId, CancellationToken ct = default);
    Task<AgentRegistryEntry> UpsertAgentAsync(AgentRegistryEntry agent, CancellationToken ct = default);

    Task<IntentRuleSimulationResult> SimulateAsync(
        string tenantId,
        string sourceAgentId,
        string intent,
        string? channel,
        CancellationToken ct = default);
}
