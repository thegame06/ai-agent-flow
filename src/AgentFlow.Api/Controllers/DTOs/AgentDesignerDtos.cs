namespace AgentFlow.Api.Controllers.DTOs;

// ─────────────────────────────────────────────
// CREATE / UPDATE DTOs — used by the Agent Designer UI
// ─────────────────────────────────────────────

/// <summary>
/// Full DTO for creating or updating an agent from the Designer.
/// Maps to AgentDefinition aggregate + its value objects.
/// </summary>
public sealed record AgentDesignerDto
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = "Draft";  // Draft | Published | Archived
    public string Version { get; init; } = "1.0.0";

    // Brain configuration
    public BrainConfigDto Brain { get; init; } = new();

    // Loop configuration 
    public LoopConfigDto Loop { get; init; } = new();

    // Memory configuration 
    public MemoryConfigDto Memory { get; init; } = new();

    // Session configuration
    public SessionConfigDto Session { get; init; } = new();

    // Designer steps (visual step builder)
    public IReadOnlyList<DesignerStepDto> Steps { get; init; } = [];

    // Tool bindings 
    public IReadOnlyList<ToolBindingDto> Tools { get; init; } = [];

    // Tags
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record BrainConfigDto
{
    public string PrimaryModel { get; init; } = "gpt-4o";
    public string FallbackModel { get; init; } = "gpt-4o-mini";
    public string Provider { get; init; } = "OpenAI";
    public string SystemPrompt { get; init; } = string.Empty;
    public float Temperature { get; init; } = 0.7f;
    public int MaxResponseTokens { get; init; } = 4096;
}

public sealed record LoopConfigDto
{
    public int MaxSteps { get; init; } = 25;
    public int TimeoutPerStepMs { get; init; } = 30000;
    public int MaxTokensPerExecution { get; init; } = 100000;
    public int MaxRetries { get; init; } = 3;
    public bool EnablePromptInjectionGuard { get; init; } = true;
    public bool EnablePIIProtection { get; init; } = true;
    public bool RequireHumanApproval { get; init; } = false;
    public string HumanApprovalThreshold { get; init; } = "high_risk";
}

public sealed record MemoryConfigDto
{
    public bool WorkingMemory { get; init; } = true;
    public bool LongTermMemory { get; init; } = false;
    public bool VectorMemory { get; init; } = false;
    public bool AuditMemory { get; init; } = true;
}

public sealed record SessionConfigDto
{
    public bool EnableThreads { get; init; } = false;
    public int DefaultThreadTtlHours { get; init; } = 168; // 7 days default
    public int MaxTurnsPerThread { get; init; } = 100;
    public int ContextWindowSize { get; init; } = 10;
    public bool AutoCreateThread { get; init; } = true;
    public bool EnableSummarization { get; init; } = false;
    public string ThreadKeyPattern { get; init; } = "{agentName}-{guid}";
}

/// <summary>
/// A visual step in the Agent Designer.
/// Each step represents one node in the agent loop graph.
/// </summary>
public sealed record DesignerStepDto
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;  // think | plan | act | observe | decide | tool_call | human_review
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Dictionary<string, object> Config { get; init; } = [];
    public PositionDto Position { get; init; } = new();
    public IReadOnlyList<string> Connections { get; init; } = [];
}

public sealed record PositionDto
{
    public double X { get; init; }
    public double Y { get; init; }
}

public sealed record ToolBindingDto
{
    public string ToolId { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = "Low";
    public IReadOnlyList<string> Permissions { get; init; } = [];
}

// ─────────────────────────────────────────────
// RESPONSE DTOs — what the API returns
// ─────────────────────────────────────────────

/// <summary>
/// Lightweight DTO for agent list views.
/// </summary>
public sealed record AgentListItemDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string Status { get; init; }
    public required long Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>
/// Full DTO for loading an agent in the Designer (includes all config).
/// </summary>
public sealed record AgentDetailDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string Status { get; init; }
    public required long Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string OwnerUserId { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];

    // Full configuration
    public BrainConfigDto Brain { get; init; } = new();
    public LoopConfigDto Loop { get; init; } = new();
    public MemoryConfigDto Memory { get; init; } = new();
    public SessionConfigDto Session { get; init; } = new();
    public IReadOnlyList<DesignerStepDto> Steps { get; init; } = [];
    public IReadOnlyList<ToolBindingDto> Tools { get; init; } = [];
}

// ─────────────────────────────────────────────
// CLONE REQUEST DTO
// ─────────────────────────────────────────────

public sealed record CloneAgentRequest
{
    public string NewName { get; init; } = string.Empty;
    public string? NewDescription { get; init; }
}
