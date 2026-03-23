using AgentFlow.Domain.Enums;
using AgentFlow.Abstractions;

namespace AgentFlow.Domain.ValueObjects;

/// <summary>
/// Immutable configuration for the agent's LLM brain.
/// Encapsulates SK Kernel config without leaking SK types upward.
/// </summary>
public sealed record BrainConfiguration
{
    public string ModelId { get; init; } = string.Empty;       // e.g. "gpt-4o"
    public string Provider { get; init; } = string.Empty;      // e.g. "OpenAI", "AzureOpenAI"
    public string? Endpoint { get; init; }                     // Azure endpoint if applicable
    public string SystemPromptTemplate { get; init; } = string.Empty;
    public float Temperature { get; init; } = 0.7f;
    public float TopP { get; init; } = 1.0f;
    public int MaxResponseTokens { get; init; } = 2048;
    public bool RequiresToolExecution { get; init; } = true;
    public string[]? StopSequences { get; init; }

    // Anti-prompt-injection: system prompt is validated at save time
    public bool IsSystemPromptValidated { get; init; } = false;
}

/// <summary>
/// Controls the agent loop execution behavior.
/// Anti-infinite-loop: MaxIterations enforced at aggregate level.
/// </summary>
public sealed record AgentLoopConfig
{
    public int MaxIterations { get; init; } = 10;
    public TimeSpan MaxExecutionTime { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ToolCallTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryBackoffBase { get; init; } = TimeSpan.FromSeconds(2);
    public bool AllowParallelToolCalls { get; init; } = false;
    public HumanInTheLoopConfig HitlConfig { get; init; } = new();
    public PlannerType PlannerType { get; init; } = PlannerType.ReAct;
    public RuntimeMode RuntimeMode { get; init; } = RuntimeMode.Autonomous;
}

public sealed record HumanInTheLoopConfig
{
    public bool Enabled { get; init; } = false;
    public bool RequireReviewOnAllToolCalls { get; init; } = false;
    public bool RequireReviewOnPolicyEscalation { get; init; } = true;
    public double ConfidenceThresholdToReview { get; init; } = 0.7; // 0.0 - 1.0
}

/// <summary>
/// Memory configuration per agent.
/// Determines which memory stores are activated.
/// </summary>
public sealed record MemoryConfig
{
    public bool EnableWorkingMemory { get; init; } = true;
    public int WorkingMemoryTtlSeconds { get; init; } = 3600;
    public bool EnableLongTermMemory { get; init; } = false;
    public bool EnableVectorMemory { get; init; } = false;
    public string? VectorCollectionName { get; init; }
    public int VectorSearchTopK { get; init; } = 5;
    public float VectorMinRelevanceScore { get; init; } = 0.75f;
}

/// <summary>
/// Session/Thread configuration per agent.
/// Controls conversation continuity and persistence.
/// Unicorn Strategy: Enterprise-grade session management.
/// </summary>
public sealed record SessionConfig
{
    /// <summary>
    /// Enable persistent multi-turn conversations (ConversationThread).
    /// If false, each execution is stateless.
    /// </summary>
    public bool EnableThreads { get; init; } = false;

    /// <summary>
    /// Default thread expiration time. After this, threads auto-archive.
    /// Examples: 1 hour for chatbots, 7 days for support, 30 days for long-running workflows.
    /// </summary>
    public TimeSpan DefaultThreadTtl { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Maximum number of conversation turns per thread.
    /// Prevents infinite conversations and cost explosion.
    /// </summary>
    public int MaxTurnsPerThread { get; init; } = 100;

    /// <summary>
    /// Number of recent turns to include in LLM context window.
    /// Controls how much history is sent to the brain.
    /// </summary>
    public int ContextWindowSize { get; init; } = 10;

    /// <summary>
    /// Auto-create thread on first execution if none provided.
    /// If false, ThreadId must be explicitly provided.
    /// </summary>
    public bool AutoCreateThread { get; init; } = true;

    /// <summary>
    /// Enable conversation summarization for threads exceeding ContextWindowSize.
    /// Older turns are condensed to save tokens.
    /// </summary>
    public bool EnableSummarization { get; init; } = false;

    /// <summary>
    /// Thread naming pattern. Variables: {agentName}, {userId}, {date}, {guid}
    /// Example: "{agentName}-{userId}-{date}" → "support-agent-user123-2026-02-22"
    /// </summary>
    public string ThreadKeyPattern { get; init; } = "{agentName}-{guid}";
}

/// <summary>
/// Binding between an agent and an authorized tool.
/// Allows per-binding permission scoping beyond the base tool permissions.
/// </summary>
public sealed record ToolBinding
{
    public string ToolId { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string ToolVersion { get; init; } = string.Empty;
    public IReadOnlyList<string> GrantedPermissions { get; init; } = [];
    public bool IsEnabled { get; init; } = true;
    public int MaxCallsPerExecution { get; init; } = 10;
}

/// <summary>
/// Declarative workflow step used by the Agent Designer and sequential planner runtime.
/// </summary>
public sealed record WorkflowStep
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = "think"; // think | plan | act | observe | decide | aggregate | tool_call | human_review
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object> Config { get; init; } = new Dictionary<string, object>();
    public WorkflowPosition Position { get; init; } = new();
    public IReadOnlyList<string> Connections { get; init; } = [];
}

public sealed record WorkflowPosition
{
    public double X { get; init; }
    public double Y { get; init; }
}

/// <summary>
/// Represents an agent execution step.
/// Each step is a snapshot of one iteration in the Think→Plan→Act→Observe loop.
/// </summary>
public sealed record AgentStep
{
    public string Id { get; init; } = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
    public StepType StepType { get; init; }
    public int Iteration { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
    public long DurationMs { get; init; }
    public string? ToolId { get; init; }
    public string? ToolName { get; init; }
    public string? InputJson { get; init; }
    public string? OutputJson { get; init; }
    public string? LlmPrompt { get; init; }
    public string? LlmResponse { get; init; }
    public int? TokensUsed { get; init; }
    public bool IsSuccess { get; init; } = true;
    public string? ErrorMessage { get; init; }
    public string? ThinkingRationale { get; init; } // Captured from chain-of-thought
}

/// <summary>
/// Input to an agent execution. Sanitized before entering the engine.
/// </summary>
public sealed record ExecutionInput
{
    public string UserMessage { get; init; } = string.Empty;
    public string? ContextJson { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
    public string Language { get; init; } = "en";
}

/// <summary>
/// Output from a completed agent execution.
/// </summary>
public sealed record ExecutionOutput
{
    public string FinalResponse { get; init; } = string.Empty;
    public string? StructuredOutputJson { get; init; }
    public int TotalTokensUsed { get; init; }
    public int TotalToolCalls { get; init; }
    public int TotalIterations { get; init; }
}
