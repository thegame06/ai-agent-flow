namespace AgentFlow.Abstractions;

public sealed record HumanEscalationNotificationRequest
{
    public string TenantId { get; init; } = string.Empty;
    public string QueueId { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string LastMessage { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed record HumanEscalationNotificationResult
{
    public bool Delivered { get; init; }
    public string QueueId { get; init; } = string.Empty;
    public string QueueName { get; init; } = string.Empty;
    public int ActiveMembers { get; init; }
    public string TicketId { get; init; } = string.Empty;
    public string? Reason { get; init; }
}

public interface IHumanEscalationNotifier
{
    Task<HumanEscalationNotificationResult> NotifyAsync(
        HumanEscalationNotificationRequest request,
        CancellationToken ct = default);
}
