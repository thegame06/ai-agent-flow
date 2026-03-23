namespace AgentFlow.Domain.Enums;

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
