namespace AgentFlow.Domain.Enums;

/// <summary>
/// Defines the functional role of an agent within the platform.
/// Custom = user-defined agent with no system responsibility.
/// System agents are seeded automatically and control platform behavior.
/// </summary>
public enum AgentSystemRole
{
    /// <summary>
    /// Standard user-created agent. No special platform privileges.
    /// </summary>
    Custom = 0,

    /// <summary>
    /// Receives all incoming channel messages, detects intentions and routes to
    /// the appropriate workflow or agent. One per tenant channel, pre-seeded.
    /// </summary>
    Router = 1,

    /// <summary>
    /// The "brain" of a specific workflow. Talks directly with the customer,
    /// executes business logic, calls tools/integrations, and returns structured
    /// JSON output for downstream workflow nodes. Assigned per workflow.
    /// </summary>
    WorkflowBrain = 2,

    /// <summary>
    /// Platform assistant that guides the user in creating and configuring
    /// workflows and agents through natural language conversation.
    /// Detects missing configs, suggests node structures, scaffolds workflows.
    /// </summary>
    ConfigAssistant = 3,
}

public enum AgentStatus
{
    Draft,
    Published,
    Deprecated,
    Suspended
}


// ExecutionStatus, ToolRiskLevel, and ExecutionPriority moved to AgentFlow.Abstractions

public enum StepType
{
    Think,      // LLM reasoning/reflection step
    Plan,       // Planning/structured output step
    Act,        // Tool invocation step
    Observe,    // Processing tool output step
    Decision,   // Conditional gate / branch evaluation
    Aggregate,  // Fan-in aggregation of parallel branches
    Memory,     // Memory read/write step
    Checkpoint, // Explicit human-in-the-loop step
}

public enum ToolScope
{
    Platform,   // Available to all tenants
    Tenant,     // Tenant-specific tool
    Agent       // Agent-scoped, not shareable
}

public enum ToolStatus
{
    Active,
    Deprecated,
    Disabled
}

public enum MemoryType
{
    Working,    // Redis: short-lived, per-execution
    LongTerm,   // MongoDB: persisted per agent
    Vector,     // Vector DB: semantic similarity search
    Audit       // MongoDB: immutable, append-only
}

public enum TenantTier
{
    Free,
    Professional,
    Enterprise,
    Platform // Internal pseudo-tenant
}
