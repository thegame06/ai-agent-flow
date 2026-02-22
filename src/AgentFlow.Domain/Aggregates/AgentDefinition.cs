using AgentFlow.Abstractions;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.ValueObjects;
using MongoDB.Bson.Serialization.Attributes;

namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// AgentDefinition - Blueprint describing what an agent IS.
/// Separado del AgentExecution (lo que está HACIENDO).
/// This is the tenant-scoped "template" from which executions are spawned.
/// </summary>
public sealed class AgentDefinition : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public AgentStatus Status { get; private set; } = AgentStatus.Draft;

    // --- Brain Configuration ---
    public BrainConfiguration Brain { get; private set; } = default!;

    // --- Planning & Loop Config ---
    public AgentLoopConfig LoopConfig { get; private set; } = default!;

    // --- Authorized Tools ---
    public IReadOnlyList<ToolBinding> AuthorizedTools { get; private set; } = [];

    // --- Memory Configuration ---
    public MemoryConfig Memory { get; private set; } = default!;

    // --- Session Configuration (multi-turn conversations) ---
    public SessionConfig Session { get; private set; } = new();

    // --- Experimentation: Shadow & Canary ---
    public string? ShadowAgentId { get; private set; }
    public string? CanaryAgentId { get; private set; }
    public double CanaryWeight { get; private set; } = 0.0; // 0.0-1.0 (e.g., 0.10 = 10%)

    // --- RBAC ---
    public string OwnerUserId { get; private set; } = string.Empty;
    public IReadOnlyList<string> AllowedRoles { get; private set; } = [];
    public IReadOnlyList<string> Tags { get; private set; } = [];

    // For MongoDB deserialization
    private AgentDefinition() { }

    public static Result<AgentDefinition> Create(
        string tenantId,
        string name,
        string description,
        BrainConfiguration brain,
        AgentLoopConfig loopConfig,
        MemoryConfig memory,
        SessionConfig? session,
        string ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<AgentDefinition>.Failure(Error.Validation(nameof(name), "Agent name is required."));

        if (name.Length > 100)
            return Result<AgentDefinition>.Failure(Error.Validation(nameof(name), "Agent name cannot exceed 100 characters."));

        if (loopConfig.MaxIterations is < 1 or > 50)
            return Result<AgentDefinition>.Failure(Error.Validation(nameof(loopConfig), "MaxIterations must be between 1 and 50."));

        var agent = new AgentDefinition
        {
            TenantId = tenantId,
            Name = name,
            Description = description,
            Brain = brain,
            LoopConfig = loopConfig,
            Memory = memory,
            Session = session ?? new SessionConfig(),
            OwnerUserId = ownerUserId,
            CreatedBy = ownerUserId,
            UpdatedBy = ownerUserId
        };

        agent.AddDomainEvent(new AgentDefinitionCreatedEvent(agent.Id, tenantId, ownerUserId));
        return Result<AgentDefinition>.Success(agent);
    }

    public Result AddTool(ToolBinding tool)
    {
        if (AuthorizedTools.Any(t => t.ToolId == tool.ToolId))
            return Result.Failure(Error.Validation("Tool", $"Tool {tool.ToolId} is already bound to this agent."));

        var tools = new List<ToolBinding>(AuthorizedTools) { tool };
        AuthorizedTools = tools.AsReadOnly();
        MarkUpdated(UpdatedBy);
        return Result.Success();
    }

    /// <summary>
    /// Full update from the Agent Designer.
    /// Validates invariants and replaces all mutable configuration.
    /// </summary>
    public Result Update(
        string name,
        string description,
        BrainConfiguration brain,
        AgentLoopConfig loopConfig,
        MemoryConfig memory,
        SessionConfig? session,
        IReadOnlyList<ToolBinding> tools,
        IReadOnlyList<string> tags,
        string updatedBy,
        string? shadowAgentId = null,
        string? canaryAgentId = null,
        double canaryWeight = 0.0)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation(nameof(name), "Agent name is required."));

        if (name.Length > 100)
            return Result.Failure(Error.Validation(nameof(name), "Agent name cannot exceed 100 characters."));

        if (loopConfig.MaxIterations is < 1 or > 50)
            return Result.Failure(Error.Validation(nameof(loopConfig), "MaxIterations must be between 1 and 50."));

        if (canaryWeight is < 0.0 or > 1.0)
            return Result.Failure(Error.Validation(nameof(canaryWeight), "CanaryWeight must be between 0.0 and 1.0."));

        Name = name;
        Description = description;
        Brain = brain;
        LoopConfig = loopConfig;
        Memory = memory;
        Session = session ?? Session;
        AuthorizedTools = tools;
        Tags = tags;
        ShadowAgentId = shadowAgentId;
        CanaryAgentId = canaryAgentId;
        CanaryWeight = canaryWeight;
        MarkUpdated(updatedBy);

        return Result.Success();
    }

    /// <summary>
    /// Replace the tool set entirely (used by Designer full save).
    /// </summary>
    public void ReplaceTools(IReadOnlyList<ToolBinding> tools)
    {
        AuthorizedTools = tools;
    }

    /// <summary>
    /// Update tags for this agent.
    /// </summary>
    public void SetTags(IReadOnlyList<string> tags)
    {
        Tags = tags;
        MarkUpdated(UpdatedBy);
    }

    public Result Publish(string publishedBy)
    {
        if (Status == AgentStatus.Published)
            return Result.Failure(Error.Validation(nameof(Status), "Agent is already published."));

        if (!AuthorizedTools.Any() && Brain.RequiresToolExecution)
            return Result.Failure(Error.Validation("Tools", "Agent requires at least one tool to be published."));

        Status = AgentStatus.Published;
        MarkUpdated(publishedBy);
        AddDomainEvent(new AgentDefinitionPublishedEvent(Id, TenantId, publishedBy));
        return Result.Success();
    }

    /// <summary>
    /// Clone this agent as a new Draft with optional name override.
    /// Used by the Designer to duplicate an existing agent configuration.
    /// </summary>
    public static Result<AgentDefinition> Clone(
        AgentDefinition source,
        string newName,
        string? newDescription,
        string clonedBy)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return Result<AgentDefinition>.Failure(Error.Validation(nameof(newName), "Cloned agent name is required."));

        if (newName.Length > 100)
            return Result<AgentDefinition>.Failure(Error.Validation(nameof(newName), "Agent name cannot exceed 100 characters."));

        var cloned = new AgentDefinition
        {
            TenantId = source.TenantId,
            Name = newName,
            Description = newDescription ?? $"Cloned from {source.Name}",
            Status = AgentStatus.Draft, // Always start as Draft
            Brain = source.Brain,
            LoopConfig = source.LoopConfig,
            Memory = source.Memory,
            AuthorizedTools = new List<ToolBinding>(source.AuthorizedTools).AsReadOnly(),
            Tags = new List<string>(source.Tags).AsReadOnly(),
            OwnerUserId = clonedBy,
            CreatedBy = clonedBy,
            UpdatedBy = clonedBy,
            // Do NOT clone experimentation settings (shadow/canary) — must be configured explicitly
            ShadowAgentId = null,
            CanaryAgentId = null,
            CanaryWeight = 0.0
        };

        cloned.AddDomainEvent(new AgentDefinitionCreatedEvent(cloned.Id, cloned.TenantId, clonedBy));
        return Result<AgentDefinition>.Success(cloned);
    }
}

// Domain Events
public record AgentDefinitionCreatedEvent(string AgentId, string TenantId, string CreatedBy) : DomainEvent
{
    public AgentDefinitionCreatedEvent() : this(string.Empty, string.Empty, string.Empty) { }
}

public record AgentDefinitionPublishedEvent(string AgentId, string TenantId, string PublishedBy) : DomainEvent
{
    public AgentDefinitionPublishedEvent() : this(string.Empty, string.Empty, string.Empty) { }
}
