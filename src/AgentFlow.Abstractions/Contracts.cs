namespace AgentFlow.Abstractions;

// =========================================================================
// AGENT EXECUTOR
// =========================================================================

/// <summary>
/// Central contract for executing an agent.
/// Implementations: AgentExecutionEngine (sync), WorkerAgentExecutor (async via queue)
/// </summary>
public interface IAgentExecutor
{
    Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken ct = default);

    Task<AgentExecutionResult> ResumeAsync(
        string executionId,
        string tenantId,
        CheckpointDecision decision,
        CancellationToken ct = default);

    Task<Result> CancelAsync(
        string executionId,
        string tenantId,
        string cancelledBy,
        CancellationToken ct = default);
}

/// <summary>
/// Request to execute an agent. Immutable once constructed.
/// </summary>
public sealed record AgentExecutionRequest
{
    public required string TenantId { get; init; }
    public required string AgentKey { get; init; }
    public string? AgentVersion { get; init; }      // null = latest Published
    public required string UserId { get; init; }
    public required string UserMessage { get; init; }
    public string? SessionId { get; init; }
    public string? ThreadId { get; init; }         // ✅ NEW: ConversationThread ID for multi-turn
    public string? CorrelationId { get; init; }
    public string? ParentExecutionId { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } 
        = new Dictionary<string, string>();
    public ExecutionPriority Priority { get; init; } = ExecutionPriority.Normal;
    
    // A2A (Agents as Tools) Support
    public int CallDepth { get; init; } = 0;        // Nesting level (0 = root, 1+ = delegated)
    public int TokenBudget { get; init; } = 100_000; // Max tokens for this execution chain
    public string? ContextJson { get; init; }      // Additional context
}

public sealed record AgentExecutionResult
{
    public required string ExecutionId { get; init; }
    public required string AgentKey { get; init; }
    public required string AgentVersion { get; init; }
    public required ExecutionStatus Status { get; init; }
    public string? FinalResponse { get; init; }
    public int TotalSteps { get; init; }
    public int TotalTokensUsed { get; init; }
    public long DurationMs { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public AgentRuntimeSnapshot RuntimeSnapshot { get; init; } = new();
    
    /// <summary>
    /// Thread ID for multi-turn conversations.
    /// Populated when the execution is part of a persistent conversation thread.
    /// </summary>
    public string? ThreadId { get; init; }
}

/// <summary>
/// Exact capture of runtime config at execution start.
/// Enables full reproducibility of any execution.
/// </summary>
public sealed record AgentRuntimeSnapshot
{
    public string AgentVersion { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public double Temperature { get; init; }
    public string PolicySetId { get; init; } = string.Empty;
    public string PromptProfileId { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum ExecutionStatus { Pending, Running, Completed, Failed, Cancelled, HumanReviewPending }
public enum ExecutionPriority { Low = 0, Normal = 1, High = 2, Critical = 3 }
public enum RuntimeMode { Autonomous, Deterministic }
public enum PlannerType { ReAct, Sequential, TreeOfThought }

// =========================================================================
// AGENT BRAIN (LLM)
// =========================================================================

/// <summary>
/// The cognitive layer. Only responsible for LLM calls.
/// Does NOT control state, auth, memory, or tools.
/// </summary>
public interface IAgentBrain
{
    Task<ThinkResult> ThinkAsync(ThinkContext context, CancellationToken ct = default);
    Task<ObserveResult> ObserveAsync(ObserveContext context, CancellationToken ct = default);
}

public sealed record ThinkContext
{
    public required string TenantId { get; init; }
    public required string ExecutionId { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserMessage { get; init; }
    public int Iteration { get; init; }
    public IReadOnlyList<object> History { get; init; } = [];
    public string WorkingMemoryJson { get; init; } = "{}";
    public IReadOnlyList<AvailableToolDescriptor> AvailableTools { get; init; } = [];
    public string? RuntimeMode { get; init; }
    
    // ✅ NEW: Thread context for multi-turn conversations
    public ChatHistorySnapshot? ThreadSnapshot { get; init; }
}

/// <summary>
/// Snapshot of recent chat history for LLM context.
/// Loaded from ConversationThread (if exists).
/// </summary>
public sealed record ChatHistorySnapshot
{
    public required string ThreadId { get; init; }
    public required IReadOnlyList<ConversationTurn> RecentTurns { get; init; }
    public int TotalTurns { get; init; }
    public string? OlderContextSummary { get; init; }
}

/// <summary>
/// A single turn in the conversation.
/// </summary>
public sealed record ConversationTurn
{
    public required string UserMessage { get; init; }
    public string? AssistantResponse { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

public sealed record ThinkResult
{
    public required ThinkDecision Decision { get; init; }
    public string? Rationale { get; init; }
    public string? DetectedIntent { get; init; }
    public string? NextToolName { get; init; }
    public string? NextToolInputJson { get; init; }
    public string? FinalAnswer { get; init; }
    public int TokensUsed { get; init; }
    public bool PromptInjectionDetected { get; init; }
}

public enum ThinkDecision { UseTool, ProvideFinalAnswer, Checkpoint, RequestMoreContext }

/// <summary>
/// Supported Brain providers in AgentFlow.
/// </summary>
public enum BrainProvider
{
    /// <summary>Microsoft Semantic Kernel.</summary>
    SemanticKernel,
    /// <summary>Microsoft Agent Framework (MAF).</summary>
    MicrosoftAgentFramework
}


public sealed record ObserveContext
{
    public required string TenantId { get; init; }
    public required string ToolName { get; init; }
    public required string ToolOutputJson { get; init; }
    public bool ToolSucceeded { get; init; }
    public required string UserGoal { get; init; }
    public IReadOnlyList<object> History { get; init; } = [];
}

public sealed record ObserveResult
{
    public required string Summary { get; init; }
    public bool GoalAchieved { get; init; }
    public int TokensUsed { get; init; }
}

public sealed record AvailableToolDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string InputSchemaJson { get; init; } = "{}";
    public string? OutputSchemaJson { get; init; }
}

// =========================================================================
// EXECUTION PLANNING
// =========================================================================

public interface IExecutionPlanner
{
    Task<ExecutionPlan> CreatePlan(PlannerCreateContext context, CancellationToken ct = default);
    Task<ExecutionPlan> RevisePlan(PlannerReviseContext context, CancellationToken ct = default);
    PlanNextStepResult NextStep(PlannerNextStepContext context);
}

public sealed record PlannerCreateContext
{
    public required string TenantId { get; init; }
    public required string ExecutionId { get; init; }
    public required string Goal { get; init; }
    public required string SystemPrompt { get; init; }
    public PlannerType PlannerType { get; init; } = PlannerType.ReAct;
    public int MaxSteps { get; init; } = 10;
    public int TokenBudget { get; init; } = 100_000;
    public IReadOnlyList<AvailableToolDescriptor> AvailableTools { get; init; } = [];
}

public sealed record PlannerReviseContext
{
    public required PlannerCreateContext BaseContext { get; init; }
    public required ExecutionPlan CurrentPlan { get; init; }
    public required string FailureReason { get; init; }
    public int CompletedSteps { get; init; }
}

public sealed record PlannerNextStepContext
{
    public required ExecutionPlan Plan { get; init; }
    public int CompletedSteps { get; init; }
    public int RemainingTokenBudget { get; init; }
    public int MaxSteps { get; init; }
}

public sealed record ExecutionPlan
{
    public string PlanId { get; init; } = Guid.NewGuid().ToString("N");
    public int Revision { get; init; }
    public string Goal { get; init; } = string.Empty;
    public IReadOnlyList<PlannedExecutionStep> Steps { get; init; } = [];
    public string? StopCriteria { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PlannedExecutionStep
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Description { get; init; } = string.Empty;
    public string? SuggestedToolName { get; init; }
    public string? SuccessCriteria { get; init; }
}

public sealed record PlanNextStepResult
{
    public bool ShouldStop { get; init; }
    public string? StopReason { get; init; }
    public PlannedExecutionStep? Step { get; init; }
}

// =========================================================================
// TOOL PLUGIN
// =========================================================================

/// <summary>
/// Contract for all AgentFlow tools. Register via DI or assembly scan.
/// </summary>
/// <summary>
/// Contract for all AgentFlow tools. Register via DI or assembly scan.
/// </summary>
public interface IToolPlugin : IAgentFlowExtension
{
    string Name { get; } // Display name / Friendly name
    string Description { get; }
    string InputSchemaJson { get; }
    string? OutputSchemaJson { get; }
    ToolRiskLevel RiskLevel { get; }
    IReadOnlyList<string> RequiredPermissions { get; }

    Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default);
}

public sealed record ToolExecutionContext
{
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public required string ExecutionId { get; init; }
    public required string StepId { get; init; }
    public required string CorrelationId { get; init; }
    public required string InputJson { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
    
    // A2A (Agents as Tools) Support
    public int CallDepth { get; init; } = 0;               // Nesting level for circuit breaker
    public int TokenBudget { get; init; } = 100_000;       // Total tokens allowed for chain
    public int TokensUsedSoFar { get; init; } = 0;         // Tokens consumed in parent chain
    public DateTimeOffset ExecutionStartedAt { get; init; } = DateTimeOffset.UtcNow; // For timeout enforcement
}

public sealed record ToolResult
{
    public bool IsSuccess { get; init; }
    public string? OutputJson { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public long DurationMs { get; init; }
    public bool WasSandboxed { get; init; }

    public static ToolResult Success(string outputJson, long durationMs = 0) =>
        new() { IsSuccess = true, OutputJson = outputJson, DurationMs = durationMs };

    public static ToolResult Failure(string errorCode, string errorMessage, long durationMs = 0) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage, DurationMs = durationMs };
}

public enum ToolRiskLevel { Low = 0, Medium = 1, High = 2, Critical = 3 }

// =========================================================================
// TOOL ORCHESTRATOR
// =========================================================================

/// <summary>
/// Higher-level service that orchestrates tool execution (auth + sandbox + execution).
/// Used by AgentExecutionEngine to run tools.
/// </summary>
/// <summary>
/// Model Context Protocol (MCP) gateway to execute tools on remote servers.
/// </summary>
public interface IMcpToolGateway
{
    /// <summary>
    /// Executes a tool on a remote MCP server.
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        string serverName, 
        string toolName, 
        ToolExecutionContext context, 
        CancellationToken ct = default);
}

public sealed record TenantMcpSettings
{
    public required string TenantId { get; init; }
    public bool Enabled { get; init; } = false;
    public string Runtime { get; init; } = "MicrosoftAgentFramework";
    public int TimeoutSeconds { get; init; } = 20;
    public int RetryCount { get; init; } = 1;
    public IReadOnlyList<string> AllowedServers { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; init; }
}

public interface ITenantMcpSettingsStore
{
    Task<TenantMcpSettings> GetAsync(string tenantId, CancellationToken ct = default);
    Task<TenantMcpSettings> SaveAsync(TenantMcpSettings settings, CancellationToken ct = default);
}

/// <summary>
/// Core tool execution engine.
/// </summary>
public interface IToolExecutor
{
    Task<ToolExecutionResult> ExecuteToolAsync(
        ToolInvocationRequest request, 
        CancellationToken ct = default);

    Task<bool> CanExecuteAsync(
        string toolId, 
        string tenantId, 
        string userId, 
        CancellationToken ct = default);
}

public sealed record ToolInvocationRequest
{
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public required string ExecutionId { get; init; }
    public required string StepId { get; init; }
    public required string ToolId { get; init; }
    public required string ToolName { get; init; }
    public required string InputJson { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ToolExecutionResult
{
    public bool IsSuccess { get; init; }
    public string? OutputJson { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public long DurationMs { get; init; }
    public bool WasSandboxed { get; init; }
}

// =========================================================================
// TOOL REGISTRY
// =========================================================================

public interface IToolRegistry
{
    void Register(IToolPlugin tool);
    IToolPlugin? Resolve(string name, string? version = null);
    IReadOnlyList<IToolPlugin> GetAll(string tenantId);
    Task<bool> IsAuthorizedAsync(string toolName, string tenantId, string userId, CancellationToken ct = default);
}

// =========================================================================
// POLICY ENGINE
// =========================================================================

/// <summary>
/// Transversal policy evaluation at 6 checkpoints throughout the agent loop.
/// </summary>
public interface IPolicyEngine
{
    Task<PolicyResult> EvaluateAsync(
        PolicyCheckpoint checkpoint,
        PolicyEvaluationContext context,
        CancellationToken ct = default);
}

public sealed record PolicyDefinition
{
    public required string PolicyId { get; init; }
    public required string Description { get; init; }
    public required PolicyCheckpoint AppliesAt { get; init; }
    public required string PolicyType { get; init; }
    public required PolicyAction Action { get; init; }
    public required PolicySeverity Severity { get; init; }
    public bool IsEnabled { get; init; } = true;
    public IReadOnlyDictionary<string, string> Config { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> TargetSegments { get; init; } = [];
}

public enum PolicyAction { Allow, Block, Warn, Escalate, Shadow }

public sealed record PolicySetDefinition
{
    public required string PolicySetId { get; init; }
    public required string Version { get; init; }
    public required string TenantId { get; init; }
    public bool IsPublished { get; init; }
    public IReadOnlyList<PolicyDefinition> Policies { get; init; } = [];
}

public interface IPolicyEvaluator : IAgentFlowExtension
{
    string PolicyType { get; }
    Task<(bool Violated, string? Evidence)> EvaluateAsync(
        PolicyDefinition policy,
        PolicyEvaluationContext context, 
        CancellationToken ct = default);
}

public enum PolicyCheckpoint
{
    PreAgent = 0,
    PreLLM = 1,
    PostLLM = 2,
    PreTool = 3,
    PostTool = 4,
    PreResponse = 5
}

public sealed record PolicyEvaluationContext
{
    public required string TenantId { get; init; }
    public required string AgentKey { get; init; }
    public required string AgentVersion { get; init; }
    public required string PolicySetId { get; init; }
    public required string ExecutionId { get; init; }
    public required string UserId { get; init; }
    public PolicyCheckpoint Checkpoint { get; init; }
    public string? ToolName { get; init; }
    public string? LlmResponse { get; init; }
    public string? FinalResponse { get; init; }
    public string? UserMessage { get; init; }
    public string? ToolInputJson { get; init; }
    public string? ToolOutputJson { get; init; }
    public IReadOnlyList<string> UserSegments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record PolicyResult
{
    public PolicyDecision Decision { get; init; }
    public IReadOnlyList<PolicyViolation> Violations { get; init; } = [];
    public bool HasViolations => Violations.Count > 0;

    public static PolicyResult Allow() => new() { Decision = PolicyDecision.Allow };
    public static PolicyResult Block(string code, string reason) =>
        new()
        {
            Decision = PolicyDecision.Block,
            Violations = [new PolicyViolation { Code = code, Description = reason, Severity = PolicySeverity.Critical }]
        };
}

public enum PolicyDecision { Allow, Block, Warn, Escalate }

public sealed record PolicyViolation
{
    public required string Code { get; init; }
    public required string Description { get; init; }
    public PolicySeverity Severity { get; init; }
    public string? PolicyId { get; init; }
    public string? RemediationHint { get; init; }
}

public enum PolicySeverity { Info, Warning, High, Critical }

// =========================================================================
// EVALUATION ENGINE
// =========================================================================

public interface IAgentEvaluator
{
    Task<EvaluationResult> EvaluateAsync(
        EvaluationRequest request,
        CancellationToken ct = default);
}

public sealed record EvaluationRequest
{
    public required string ExecutionId { get; init; }
    public required string TenantId { get; init; }
    public required string AgentKey { get; init; }
    public required string AgentVersion { get; init; }
    public required string UserMessage { get; init; }
    public required string FinalResponse { get; init; }
    public required IReadOnlyList<ToolCallRecord> ToolCalls { get; init; }
    public required EvaluationConfig Config { get; init; }
    public bool IsShadowEvaluation { get; init; }
    public TestCase? ApplicableTestCase { get; init; }
}

public sealed record ToolCallRecord
{
    public required string ToolName { get; init; }
    public required string InputJson { get; init; }
    public string? OutputJson { get; init; }
    public bool IsSuccess { get; init; }
    public int OrderIndex { get; init; }
}

public sealed record EvaluationConfig
{
    public bool EnableQualityScoring { get; init; } = true;
    public bool EnableHallucinationDetection { get; init; } = true;
    public double RequireHumanReviewOnScoreBelow { get; init; } = 0.70;
    public double QualityThreshold { get; init; } = 0.80;
    public string? JudgeLlmModelId { get; init; }
    public EvaluationMode Mode { get; init; } = EvaluationMode.Observing;
}

public enum EvaluationMode { Observing, Blocking }

public sealed record EvaluationResult
{
    public required string ExecutionId { get; init; }
    public required string TenantId { get; init; }
    public double QualityScore { get; init; }
    public double PolicyComplianceScore { get; init; }
    public HallucinationRisk HallucinationRisk { get; init; }
    public double? ToolUsageAccuracy { get; init; }
    public bool RequiresHumanReview { get; init; }
    public HumanReviewPriority ReviewPriority { get; init; }
    public string EvaluationRationale { get; init; } = string.Empty;
    public IReadOnlyList<EvaluationViolation> Violations { get; init; } = [];
    public bool IsShadowEvaluation { get; init; }
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum HallucinationRisk { None, Low, Medium, High, Critical }
public enum HumanReviewPriority { Low, Medium, High, Immediate }

public sealed record EvaluationViolation
{
    public required string Code { get; init; }
    public required string Description { get; init; }
    public EvaluationSeverity Severity { get; init; }
    public string? Evidence { get; init; }
}

public enum EvaluationSeverity { Info, Warning, High, Critical }

// =========================================================================
// TEST RUNNER
// =========================================================================

public interface IAgentTestRunner
{
    Task<TestSuiteResult> RunSuiteAsync(
        TestSuiteRequest request,
        CancellationToken ct = default);

    Task<TestCaseResult> RunCaseAsync(
        TestCaseRequest request,
        CancellationToken ct = default);
}

public sealed record TestSuiteRequest
{
    public required string TenantId { get; init; }
    public required string AgentKey { get; init; }
    public required string AgentVersion { get; init; }
    public string[]? Tags { get; init; }  // null = run all
}

public sealed record TestCase
{
    public required string Name { get; init; }
    public required string Input { get; init; }
    public string? ExpectedIntent { get; init; }
    public IReadOnlyList<string> ExpectedTools { get; init; } = [];
    public IReadOnlyList<string> ExpectedOutputContains { get; init; } = [];
    public IReadOnlyList<string> ExpectedOutputNotContains { get; init; } = [];
    public int MaxDurationMs { get; init; } = 10_000;
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record TestSuiteResult
{
    public required string AgentKey { get; init; }
    public required string AgentVersion { get; init; }
    public required int TotalCases { get; init; }
    public required int Passed { get; init; }
    public required int Failed { get; init; }
    public required int Warned { get; init; }
    public required long TotalDurationMs { get; init; }
    public TestSuiteVerdict Verdict { get; init; }
    public IReadOnlyList<TestCaseResult> Results { get; init; } = [];
}

public enum TestSuiteVerdict { Pass, Fail, Warning }

public sealed record TestCaseResult
{
    public required string CaseName { get; init; }
    public required TestCaseVerdict Verdict { get; init; }
    public required long DurationMs { get; init; }
    public double? QualityScore { get; init; }
    public string? ExecutionId { get; init; }
    public IReadOnlyList<string> FailureReasons { get; init; } = [];
}

public sealed record TestCaseRequest
{
    public required string TenantId { get; init; }
    public required string AgentKey { get; init; }
    public required string AgentVersion { get; init; }
    public required TestCase TestCase { get; init; }
}

public enum TestCaseVerdict { Pass, Fail, Warning, Skipped }

// =========================================================================
// MODEL ROUTING
// =========================================================================

public interface IModelRouter
{
    Task<ModelSelection> SelectModelAsync(
        ModelRoutingRequest request,
        CancellationToken ct = default);
}

public interface IModelProvider
{
    string ProviderId { get; }
    string ModelId { get; }
    ModelMetadata Metadata { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}

public sealed record ModelMetadata
{
    public required string DisplayName { get; init; }
    public double CostPer1KTokens { get; init; }
    public int MaxContextTokens { get; init; }
    public string Tier { get; init; } = "Secondary"; // Primary, Secondary, Fallback
}

public sealed record ModelRoutingRequest
{
    public required string TenantId { get; init; }
    public required string AgentKey { get; init; }
    public required ModelRoutingConfig Config { get; init; }
    public string? TaskType { get; init; }
    public IReadOnlyDictionary<string, string> PolicyContext { get; init; }
        = new Dictionary<string, string>();
}

public sealed record ModelRoutingConfig
{
    public ModelRoutingStrategy Strategy { get; init; } = ModelRoutingStrategy.Static;
    public required string DefaultModelId { get; init; }
    public IReadOnlyList<string> FallbackChain { get; init; } = [];
    public IReadOnlyList<TaskRoutingRule> RoutingRules { get; init; } = [];
}

public enum ModelRoutingStrategy { Static, TaskBased, PolicyBased, FallbackChain }

public sealed record TaskRoutingRule
{
    public required string TaskType { get; init; }
    public required string ModelId { get; init; }
}

public sealed record ModelSelection
{
    public required string ModelId { get; init; }
    public required IModelProvider Provider { get; init; }
    public bool IsFallback { get; init; }
    public string? FallbackReason { get; init; }
    public string? Reason { get; init; }
}

public sealed record LlmRequest
{
    public required string SystemPrompt { get; init; }
    public required string UserMessage { get; init; }
    public double Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public IReadOnlyList<LlmMessage> History { get; init; } = [];
}

public sealed record LlmMessage
{
    public required string Role { get; init; } // "user" | "assistant" | "system"
    public required string Content { get; init; }
}

public sealed record LlmResponse
{
    public required string Content { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int TotalTokens => InputTokens + OutputTokens;
    public string? FinishReason { get; init; }
    public string ModelId { get; init; } = string.Empty;
    public double? EstimatedCostUsd { get; init; }
}

// =========================================================================
// EVENT TRANSPORT
// =========================================================================

public interface IAgentEventSource
{
    string SourceType { get; }
    IAsyncEnumerable<AgentEvent> StreamAsync(CancellationToken ct = default);
}

public interface IAgentEventTransport
{
    Task PublishAsync(AgentEvent @event, CancellationToken ct = default);
    Task<IAsyncDisposable> SubscribeAsync(
        string agentKey,
        Func<AgentEvent, Task> handler,
        CancellationToken ct = default);
}

public sealed record AgentEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public required string EventType { get; init; }
    public required string TenantId { get; init; }
    public required string AgentKey { get; init; }
    public required string Payload { get; init; } // JSON
    public string? CorrelationId { get; init; }
    public string? SessionId { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>();
}

// =========================================================================
// MEMORY
// =========================================================================

public interface IWorkingMemoryStore
{
    Task SetAsync(string executionId, string key, string valueJson, TimeSpan expiry, CancellationToken ct = default);
    Task<string?> GetAsync(string executionId, string key, CancellationToken ct = default);
    Task<string> GetSummaryAsync(string executionId, CancellationToken ct = default);
    Task ClearAsync(string executionId, CancellationToken ct = default);
}

public interface IDistributedLockService
{
    Task<IAsyncDisposable?> TryAcquireAsync(string lockKey, TimeSpan expiry, CancellationToken ct = default);
}

// =========================================================================
// CHECKPOINTS (Human-In-The-Loop)
// =========================================================================

public interface ICheckpointStore
{
    Task SaveAsync(AgentCheckpoint checkpoint, CancellationToken ct = default);
    Task<AgentCheckpoint?> GetAsync(string executionId, string tenantId, CancellationToken ct = default);
    Task DeleteAsync(string executionId, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentCheckpoint>> GetPendingAsync(string tenantId, int limit = 50, CancellationToken ct = default);
}

public sealed record AgentCheckpoint
{
    public required string ExecutionId { get; init; }
    public required string TenantId { get; init; }
    public required string AgentKey { get; init; }
    public required string CheckpointId { get; init; }
    public required string Reason { get; init; }
    public string? ToolName { get; init; }
    public string? ToolInputJson { get; init; }
    public string? LlmRationale { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();
}

public sealed record CheckpointDecision
{
    public required string CheckpointId { get; init; }
    public required bool Approved { get; init; }
    public string? Feedback { get; init; }
    public string? ModifiedInputJson { get; init; }
    public string? ApprovedBy { get; init; }
}

// =========================================================================
// EXTENSION SYSTEM
// =========================================================================
//
// AgentFlow supports exactly 5 extension types. No more.
// Extensible business middleware is NOT supported (by design).
//
// ┌──────────────────┬───────────────────────────────────────────────────┐
// │ Extension Type   │ Contract                                          │
// ├──────────────────┼───────────────────────────────────────────────────┤
// │ Tool             │ IToolPlugin           — concrete action / IO      │
// │ Policy Evaluator │ IPolicyEvaluator      — in AgentFlow.Policy       │
// │ Model Provider   │ IModelProvider        — LLM backend               │
// │ Memory Provider  │ IWorkingMemoryStore   — custom memory backends    │
// │ Evaluator        │ IAgentEvaluator       — quality / compliance      │
// └──────────────────┴───────────────────────────────────────────────────┘
//
// NOTE: Isolation mode is NOT a generic concern.
// Tool execution isolation is determined per-tool by IToolPlugin.RiskLevel:
//   Low/Medium  → In-process, timeout enforced by engine
//   High        → In-process w/ strict resource limits
//   Critical    → Out-of-process (future: sidecar/subprocess)

/// <summary>
/// Minimal lifecycle contract for all AgentFlow extensions.
/// All 5 extension types implement this as their base.
/// </summary>
public interface IAgentFlowExtension
{
    /// <summary>Unique identifier for this extension (stable across versions).</summary>
    string ExtensionId { get; }

    /// <summary>Semver of this extension implementation.</summary>
    string Version { get; }

    /// <summary>Called once at startup. Use for warm-up, validation, connection checks.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Health probe. Called by the platform health check system.</summary>
    Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default);
}

public sealed record ExtensionHealthStatus
{
    public bool IsHealthy { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;

    public static ExtensionHealthStatus Healthy(string? message = null) =>
        new() { IsHealthy = true, Message = message };

    public static ExtensionHealthStatus Unhealthy(string reason) =>
        new() { IsHealthy = false, Message = reason };
}

// =========================================================================
// AGENT HANDOFF (Manager -> Subagent)
// =========================================================================

public sealed record AgentHandoffRequest
{
    public required string TenantId { get; init; }
    public required string SessionId { get; init; }
    public required string CorrelationId { get; init; }
    public required string SourceAgentKey { get; init; }
    public required string TargetAgentKey { get; init; }
    public required string Intent { get; init; }
    public required string PayloadJson { get; init; }
    public IReadOnlyDictionary<string, string> PolicyContext { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record AgentHandoffToolCall
{
    public required string ContractAction { get; init; }
    public required string Provider { get; init; }
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record AgentHandoffResponse
{
    public required bool Ok { get; init; }
    public string? ResultJson { get; init; }
    public string? ErrorCode { get; init; }
    public bool Retryable { get; init; }
    public IReadOnlyDictionary<string, string> StatePatch { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<AgentHandoffToolCall> ToolCalls { get; init; } = new List<AgentHandoffToolCall>();
}

public interface IAgentHandoffExecutor
{
    Task<AgentHandoffResponse> ExecuteAsync(AgentHandoffRequest request, CancellationToken ct = default);
}

// =========================================================================
// RESULT PATTERN (shared)
// =========================================================================

public sealed record Result<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public Error? Error { get; init; }

    public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static Result<T> Failure(Error error) => new() { IsSuccess = false, Error = error };

    public Result<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        IsSuccess
            ? Result<TOut>.Success(mapper(Value!))
            : Result<TOut>.Failure(Error!);

    public async Task<Result<TOut>> BindAsync<TOut>(Func<T, Task<Result<TOut>>> binder) =>
        IsSuccess
            ? await binder(Value!)
            : Result<TOut>.Failure(Error!);
}

public sealed record Result
{
    public bool IsSuccess { get; init; }
    public Error? Error { get; init; }

    public static Result Success() => new() { IsSuccess = true };
    public static Result Failure(Error error) => new() { IsSuccess = false, Error = error };
}

public sealed record Error(string Code, string Message, ErrorCategory Category = ErrorCategory.General)
{
    public static Error NotFound(string resource) => new($"{resource}.NotFound", $"{resource} was not found.", ErrorCategory.NotFound);
    public static Error Unauthorized(string reason) => new("Auth.Unauthorized", reason, ErrorCategory.Security);
    public static Error Forbidden(string action) => new("Auth.Forbidden", $"Action '{action}' is forbidden.", ErrorCategory.Security);
    public static Error Validation(string field, string message) => new($"Validation.{field}", message, ErrorCategory.Validation);
    public static Error TenantViolation(string context) => new("Tenant.Violation", $"Cross-tenant access attempt: {context}", ErrorCategory.Security);
    public static Error EngineError(string message) => new("Engine.Error", message, ErrorCategory.Engine);
    public static Error ToolError(string toolName, string message) => new($"Tool.{toolName}.Error", message, ErrorCategory.Tool);
}

public enum ErrorCategory
{
    General,
    NotFound,
    Validation,
    Security,
    Engine,
    Tool,
    Infrastructure
}
