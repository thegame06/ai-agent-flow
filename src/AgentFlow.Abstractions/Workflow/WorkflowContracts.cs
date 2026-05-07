namespace AgentFlow.Abstractions.Workflow;

public enum WorkflowDefinitionStatus
{
    Draft,
    Published,
    Archived
}

public enum WorkflowExecutionStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

public sealed record WorkflowActivityCatalogContract
{
    public string TypeName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = "General";
    public string Description { get; init; } = string.Empty;
    public Dictionary<string, string> InputSchema { get; init; } = new();
    public Dictionary<string, string> OutputSchema { get; init; } = new();
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public sealed record WorkflowEventCatalogContract
{
    public string EventName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Entity { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public sealed record WorkflowTemplateContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string TriggerEventName { get; init; } = string.Empty;
    public string DefinitionJson { get; init; } = "{}";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public sealed record WorkflowDefinitionContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TriggerEventName { get; init; } = string.Empty;
    public int Version { get; init; } = 1;
    public WorkflowDefinitionStatus Status { get; init; } = WorkflowDefinitionStatus.Draft;
    public string DefinitionJson { get; init; } = "{}";
    public Dictionary<string, string> Metadata { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public sealed record WorkflowExecutionContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string WorkflowDefinitionId { get; init; } = string.Empty;
    public string TriggerEventName { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = "{}";
    public string ContextJson { get; init; } = "{}";
    public WorkflowExecutionStatus Status { get; init; } = WorkflowExecutionStatus.Queued;
    public string? Error { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string RequestedBy { get; init; } = string.Empty;
}

public sealed record WorkflowExecutionStepLogContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
    public string ActivityType { get; init; } = string.Empty;
    public string ActivityName { get; init; } = string.Empty;
    public WorkflowExecutionStatus Status { get; init; } = WorkflowExecutionStatus.Running;
    public string InputJson { get; init; } = "{}";
    public string? OutputJson { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
}
