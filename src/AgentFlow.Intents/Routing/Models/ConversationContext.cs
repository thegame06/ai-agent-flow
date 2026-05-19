namespace AgentFlow.Intents.Routing.Models;

/// <summary>
/// Encapsulates the full context of a conversation required for routing decisions.
/// Includes tenant isolation, channel information, ownership state, and user identity.
/// </summary>
/// <remarks>
/// <para>
/// This record provides all necessary context for the Routing Orchestrator to make
/// informed decisions about message routing, conflict detection, and lock acquisition.
/// </para>
/// <para><b>Key Responsibilities:</b></para>
/// <list type="bullet">
///   <item><description>Multi-tenant isolation via TenantId</description></item>
///   <item><description>Channel-specific routing (WhatsApp, web, voice)</description></item>
///   <item><description>User identity for audit and compliance</description></item>
///   <item><description>Current ownership state for conflict detection</description></item>
/// </list>
/// <para><b>Usage Example:</b></para>
/// <code>
/// var context = new ConversationContext
/// {
///     ConversationId = "conv-456",
///     TenantId = "tenant-banco-xyz",
///     Channel = "whatsapp",
///     UserIdentifier = "+50581143874",
///     CurrentOwnerAgentId = null,  // No active agent
///     IsLocked = false
/// };
/// </code>
/// </remarks>
public sealed record ConversationContext
{
    /// <summary>
    /// The unique identifier for this conversation.
    /// Used for ownership lock scoping and audit correlation.
    /// </summary>
    /// <remarks>
    /// <b>Format:</b> Typically a UUID or channel-specific ID (e.g., WhatsApp conversation ID).
    /// <b>Scope:</b> Unique within tenant.
    /// </remarks>
    public required string ConversationId { get; init; }

    /// <summary>
    /// The tenant identifier (for multi-tenant isolation).
    /// All routing operations are scoped to this tenant.
    /// </summary>
    /// <remarks>
    /// <b>Security:</b> Critical for data isolation in regulated multi-tenant environments.
    /// Prevents cross-tenant access to conversations and intents.
    /// </remarks>
    public required string TenantId { get; init; }

    /// <summary>
    /// The communication channel (e.g., "whatsapp", "web", "voice").
    /// Used for channel-specific routing rules and analytics.
    /// </summary>
    /// <remarks>
    /// <b>Examples:</b>
    /// <list type="bullet">
    ///   <item><c>whatsapp</c> - WhatsApp Business API</item>
    ///   <item><c>web</c> - Web chat widget</item>
    ///   <item><c>voice</c> - Voice call (Twilio, VAPI)</item>
    ///   <item><c>email</c> - Email channel</item>
    /// </list>
    /// </remarks>
    public required string Channel { get; init; }

    /// <summary>
    /// The user's identifier (phone number, email, or customer ID).
    /// Used for audit trails and user-specific analytics.
    /// </summary>
    /// <remarks>
    /// <b>Format:</b> Channel-dependent.
    /// <list type="bullet">
    ///   <item>WhatsApp: E.164 phone number (e.g., "+50581143874")</item>
    ///   <item>Web: Session ID or customer ID</item>
    ///   <item>Voice: Caller ID</item>
    /// </list>
    /// <b>Privacy:</b> Must be pseudonymized or hashed in audit logs per GDPR/local privacy laws.
    /// </remarks>
    public required string UserIdentifier { get; init; }

    /// <summary>
    /// The agent ID currently owning this conversation (if locked).
    /// Null if no agent has acquired ownership yet.
    /// </summary>
    /// <remarks>
    /// <b>Usage:</b> For conflict detection. If a new agent attempts routing while
    /// another agent owns the conversation, the orchestrator will reject with <see cref="RoutingAction.Reject"/>.
    /// </remarks>
    public string? CurrentOwnerAgentId { get; init; }

    /// <summary>
    /// Whether the conversation is currently locked by an agent.
    /// If true, routing may be rejected if the requesting agent differs from <see cref="CurrentOwnerAgentId"/>.
    /// </summary>
    /// <remarks>
    /// <b>Golden Rule:</b> Only 1 agent AI active per conversation at any time.
    /// This flag enforces that rule.
    /// </remarks>
    public bool IsLocked { get; init; }

    /// <summary>
    /// UTC timestamp when the current lock expires (if locked).
    /// After this time, ownership becomes available for acquisition.
    /// </summary>
    /// <remarks>
    /// <b>Auto-Cleanup:</b> Locks expire automatically after TTL to prevent orphaned locks
    /// in case of agent crashes or network failures.
    /// </remarks>
    public DateTimeOffset? LockedUntil { get; init; }
}
