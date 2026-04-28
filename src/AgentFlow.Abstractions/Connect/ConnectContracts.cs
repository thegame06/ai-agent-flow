namespace AgentFlow.Abstractions.Connect;

public enum ConnectOperationalStatus
{
    Queued,
    Sent,
    Delivered,
    Read,
    Failed,
    Escalated
}

public sealed record ConnectTemplateContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string? PublishedWorkflowAgentId { get; init; }
    public string? PublishedWorkflowAgentName { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public sealed record ConnectCampaignContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string TemplateId { get; init; } = string.Empty;
    public string? PublishedWorkflowAgentId { get; init; }
    public DateTimeOffset ScheduledAt { get; init; } = DateTimeOffset.UtcNow;
    public bool Enabled { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public sealed record ConnectInboxMessageContract
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Recipient { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string? CampaignId { get; init; }
    public string? TemplateId { get; init; }
    public ConnectOperationalStatus Status { get; init; } = ConnectOperationalStatus.Queued;
    public string? LastError { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public sealed record ConnectMetricsRowContract
{
    public string Dimension { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public int Total { get; init; }
    public int Queued { get; init; }
    public int Sent { get; init; }
    public int Delivered { get; init; }
    public int Read { get; init; }
    public int Failed { get; init; }
    public int Escalated { get; init; }
    public double DeliveryRate { get; init; }
    public double ReadRate { get; init; }
    public double FailureRate { get; init; }
}
