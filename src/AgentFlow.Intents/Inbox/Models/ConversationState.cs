namespace AgentFlow.Intents.Inbox.Models;

/// <summary>
/// Represents the lifecycle state of a conversation in the inbox.
/// Used to track conversations that require human review or are in progress.
/// </summary>
/// <remarks>
/// <para><b>State Transitions:</b></para>
/// <list type="bullet">
///   <item><description><b>AwaitingClassification</b> → Classified/NoMatch</description></item>
///   <item><description><b>Classified</b> → InProgress/PendingHumanReview</description></item>
///   <item><description><b>LowConfidence</b> → PendingHumanReview/InProgress</description></item>
///   <item><description><b>NoMatch</b> → PendingHumanReview/Escalated</description></item>
///   <item><description><b>InProgress</b> → Resolved/Escalated</description></item>
///   <item><description><b>PendingHumanReview</b> → Classified/Escalated/Resolved</description></item>
///   <item><description><b>Resolved</b> → Terminal state</description></item>
///   <item><description><b>Escalated</b> → Terminal state</description></item>
///   <item><description><b>Abandoned</b> → Terminal state (user timeout)</description></item>
///   <item><description><b>ConflictDetected</b> → PendingHumanReview (requires resolution)</description></item>
/// </list>
/// </remarks>
public enum ConversationState
{
    /// <summary>
    /// Message received but not yet classified by Intent Routing.
    /// Initial state for new conversations.
    /// </summary>
    AwaitingClassification = 0,

    /// <summary>
    /// Intent successfully detected with sufficient confidence.
    /// Waiting for workflow assignment.
    /// </summary>
    Classified = 1,

    /// <summary>
    /// Intent detected but confidence score is below auto-route threshold.
    /// Requires human verification before proceeding.
    /// </summary>
    LowConfidence = 2,

    /// <summary>
    /// No intent match found. Unable to route automatically.
    /// Requires human classification or fallback handling.
    /// </summary>
    NoMatch = 3,

    /// <summary>
    /// Workflow execution in progress.
    /// Agent is actively handling the conversation.
    /// </summary>
    InProgress = 4,

    /// <summary>
    /// Flagged for human review.
    /// May be due to low confidence, policy violation, or explicit escalation.
    /// </summary>
    PendingHumanReview = 5,

    /// <summary>
    /// Conversation successfully resolved.
    /// Terminal state. Workflow completed or human agent responded.
    /// </summary>
    Resolved = 6,

    /// <summary>
    /// Escalated to supervisor or specialized team.
    /// Terminal state. Handed off to external system or human supervisor.
    /// </summary>
    Escalated = 7,

    /// <summary>
    /// User did not respond within timeout window.
    /// Terminal state. Conversation marked as abandoned.
    /// </summary>
    Abandoned = 8,

    /// <summary>
    /// Ownership conflict detected (multiple agents trying to handle).
    /// Requires manual resolution to determine correct owner.
    /// </summary>
    ConflictDetected = 9
}
