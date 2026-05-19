namespace AgentFlow.Intents.Ownership.Models;

/// <summary>
/// Represents the current ownership state of a conversation.
/// Used for debugging, auditing, and conflict resolution.
/// </summary>
public sealed record ConversationOwnershipState
{
    /// <summary>
    /// The conversation identifier.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// Whether the conversation is currently locked by an agent.
    /// </summary>
    public required bool IsLocked { get; init; }

    /// <summary>
    /// The agent currently owning this conversation (if locked).
    /// Null if <see cref="IsLocked"/> is false.
    /// </summary>
    public string? CurrentOwnerAgentId { get; init; }

    /// <summary>
    /// UTC timestamp when the lock expires (if locked).
    /// After this time, ownership becomes available.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; init; }

    /// <summary>
    /// The workflow execution ID associated with the current owner (optional).
    /// Used for tracing and debugging.
    /// </summary>
    public string? WorkflowExecutionId { get; init; }
}
