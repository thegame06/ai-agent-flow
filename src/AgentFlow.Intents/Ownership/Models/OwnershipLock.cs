namespace AgentFlow.Intents.Ownership.Models;

/// <summary>
/// Represents an acquired ownership lock for a conversation.
/// Guarantees exclusive access for a single agent AI.
/// </summary>
/// <remarks>
/// Ownership locks enforce the golden rule: **only 1 agent AI active per conversation**.
/// This prevents race conditions, duplicate responses, and context loss.
/// </remarks>
public sealed record OwnershipLock
{
    /// <summary>
    /// Unique identifier for this lock instance.
    /// Format: {agentId}:{timestamp}:{guid}
    /// </summary>
    public required string LockId { get; init; }

    /// <summary>
    /// The conversation being locked.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// The agent that owns this conversation.
    /// Example: "workflow-brain-agent", "fallback-agent"
    /// </summary>
    public required string OwnerAgentId { get; init; }

    /// <summary>
    /// UTC timestamp when the lock was acquired.
    /// </summary>
    public required DateTimeOffset AcquiredAt { get; init; }

    /// <summary>
    /// UTC timestamp when the lock expires.
    /// After this time, another agent can acquire ownership.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
