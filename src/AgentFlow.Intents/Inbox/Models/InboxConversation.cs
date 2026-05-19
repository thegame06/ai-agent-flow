using AgentFlow.Intents.Classification.Models;

namespace AgentFlow.Intents.Inbox.Models;

/// <summary>
/// Represents a conversation stored in the inbox for human review or tracking.
/// Contains all metadata required for conversation management and routing decisions.
/// </summary>
/// <remarks>
/// <para><b>Tenant Isolation:</b> All queries must filter by TenantId to ensure data isolation.</para>
/// <para><b>Immutability:</b> This is a record type. Create new instances for updates.</para>
/// <para><b>Audit Trail:</b> CreatedAt, UpdatedAt, ResolvedAt track conversation lifecycle.</para>
/// </remarks>
public sealed record InboxConversation
{
    /// <summary>
    /// Unique identifier for the conversation.
    /// Should match the conversation ID from the Channel Gateway.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Tenant identifier for multi-tenant isolation.
    /// All operations must be scoped to this tenant.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Channel where the conversation originated (e.g., "whatsapp", "sms", "webchat").
    /// Used for routing and response formatting.
    /// </summary>
    public required string Channel { get; init; }

    /// <summary>
    /// User identifier from the channel (phone number, email, user ID, etc.).
    /// Used for user context and conversation continuity.
    /// </summary>
    public required string UserIdentifier { get; init; }

    /// <summary>
    /// The last message received from the user.
    /// Used for display in the inbox UI and re-classification.
    /// </summary>
    public required string LastMessage { get; init; }

    /// <summary>
    /// Current state of the conversation in the inbox lifecycle.
    /// Determines available actions and filtering.
    /// </summary>
    public required ConversationState State { get; init; }

    /// <summary>
    /// Confidence level of the intent classification (if classified).
    /// Used to prioritize conversations requiring review.
    /// </summary>
    public required ConfidenceLevel Confidence { get; init; }

    /// <summary>
    /// The detected intent key (if classified).
    /// Null if State is AwaitingClassification or NoMatch.
    /// </summary>
    public string? DetectedIntentKey { get; init; }

    /// <summary>
    /// ID of the agent assigned to handle this conversation.
    /// Populated when a workflow is initiated.
    /// </summary>
    public string? AssignedAgentId { get; init; }

    /// <summary>
    /// ID of the workflow execution handling this conversation.
    /// Links to AgentFlow.Core.Engine execution tracking.
    /// </summary>
    public string? WorkflowExecutionId { get; init; }

    /// <summary>
    /// Timestamp when the conversation first entered the inbox.
    /// Used for SLA tracking and aging reports.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Timestamp of the last state change or message update.
    /// Used for sorting and staleness detection.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Flag indicating if this conversation requires human review.
    /// Set automatically for Low confidence or explicit escalation.
    /// </summary>
    public required bool RequiresHumanReview { get; init; }

    /// <summary>
    /// Human-entered notes or comments about the conversation.
    /// Updated when state changes or during manual review.
    /// </summary>
    public string? ReviewNotes { get; init; }

    /// <summary>
    /// Identifier of the user/agent who resolved the conversation.
    /// Populated when State transitions to Resolved or Escalated.
    /// </summary>
    public string? ResolvedBy { get; init; }

    /// <summary>
    /// Timestamp when the conversation was marked as Resolved.
    /// Used for resolution metrics and SLA compliance.
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; init; }
}
