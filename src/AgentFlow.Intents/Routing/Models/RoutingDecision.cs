namespace AgentFlow.Intents.Routing.Models;

/// <summary>
/// Represents the final routing decision made by the orchestrator.
/// Contains action, target workflow/agent, reasoning, and audit metadata.
/// </summary>
/// <remarks>
/// <para>
/// This is the **output contract** of the Routing Orchestrator. It provides
/// a complete, auditable record of what action was decided, why, and what
/// to execute next.
/// </para>
/// <para><b>Decision Transparency:</b></para>
/// <list type="bullet">
///   <item><description><see cref="Action"/> - What to do (Route, Queue, Reject, Fallback)</description></item>
///   <item><description><see cref="ReasonCode"/> - Machine-readable reason (e.g., "matched", "low_confidence", "agent_conflict")</description></item>
///   <item><description><see cref="ExplanationJson"/> - Full human-readable explanation with score breakdown</description></item>
/// </list>
/// <para><b>Usage Example:</b></para>
/// <code>
/// var decision = await orchestrator.RouteMessageAsync(classification, context);
/// 
/// switch (decision.Action)
/// {
///     case RoutingAction.Route:
///         await workflowEngine.ExecuteAsync(decision.WorkflowDefinitionId);
///         break;
///     case RoutingAction.Queue:
///         await humanQueue.EnqueueAsync(decision);
///         break;
///     case RoutingAction.Reject:
///         return new ConflictResult(decision.ExplanationJson);
///     case RoutingAction.Fallback:
///         await fallbackHandler.HandleAsync(decision);
///         break;
/// }
/// </code>
/// </remarks>
public sealed record RoutingDecision
{
    /// <summary>
    /// The intent key that was matched (or "unknown" if no match).
    /// </summary>
    /// <remarks>
    /// <b>Examples:</b> "loan_application", "balance_inquiry", "fraud_report"
    /// <b>Traceability:</b> Links back to intent catalog for debugging.
    /// </remarks>
    public required string IntentKey { get; init; }

    /// <summary>
    /// The workflow definition ID to execute (if action is Route).
    /// Null if no workflow is configured or action is not Route.
    /// </summary>
    /// <remarks>
    /// <b>Format:</b> MongoDB ObjectId or custom workflow ID.
    /// <b>Usage:</b> Pass this to the workflow execution engine to trigger the flow.
    /// </remarks>
    public string? WorkflowDefinitionId { get; init; }

    /// <summary>
    /// The target agent ID (if intent routes to a specific AI agent).
    /// Null if intent routes directly to a workflow without agent context.
    /// </summary>
    /// <remarks>
    /// <b>Examples:</b> "agent-loan-officer", "agent-fraud-specialist"
    /// <b>Ownership:</b> If present, this agent acquires conversation ownership via lock.
    /// </remarks>
    public string? TargetAgentId { get; init; }

    /// <summary>
    /// The routing action decided by the orchestrator.
    /// Determines next steps in the message processing pipeline.
    /// </summary>
    public required RoutingAction Action { get; init; }

    /// <summary>
    /// Machine-readable reason code for the decision.
    /// </summary>
    /// <remarks>
    /// <b>Standard Codes:</b>
    /// <list type="bullet">
    ///   <item><c>matched</c> - Intent matched successfully</item>
    ///   <item><c>low_confidence</c> - Match found but confidence below auto-route threshold</item>
    ///   <item><c>no_match</c> - No viable intent match found</item>
    ///   <item><c>no_workflow_configured</c> - Intent matched but workflow not configured</item>
    ///   <item><c>agent_conflict</c> - Another agent owns the conversation</item>
    ///   <item><c>lock_failed</c> - Failed to acquire conversation ownership lock</item>
    /// </list>
    /// <b>Convention:</b> Always use snake_case for consistency.
    /// </remarks>
    public required string ReasonCode { get; init; }

    /// <summary>
    /// Full JSON explanation of the decision for audit, debugging, and compliance.
    /// Must be valid JSON parseable by observability tools.
    /// </summary>
    /// <remarks>
    /// <b>Structure Example:</b>
    /// <code>
    /// {
    ///   "intent": "loan_application",
    ///   "confidence": 0.92,
    ///   "confidence_level": "High",
    ///   "workflow": "Loan Application Flow",
    ///   "agent": "agent-loan-officer",
    ///   "lock_acquired": true,
    ///   "lock_id": "lock-abc123",
    ///   "reason": "matched",
    ///   "score_breakdown": {
    ///     "semantic": 0.95,
    ///     "keyword": 0.80,
    ///     "priority": 0.50
    ///   }
    /// }
    /// </code>
    /// <b>Compliance:</b> This field is logged to audit trail for regulatory review.
    /// </remarks>
    public required string ExplanationJson { get; init; }

    /// <summary>
    /// UTC timestamp when this decision was made.
    /// Used for audit trails and performance metrics.
    /// </summary>
    public required DateTimeOffset DecidedAt { get; init; }

    /// <summary>
    /// The conversation ownership lock ID (if acquired).
    /// Null if no lock was acquired (e.g., Queue, Reject, Fallback actions).
    /// </summary>
    /// <remarks>
    /// <b>Usage:</b> Store this to release the lock later via:
    /// <code>
    /// await ownershipManager.ReleaseLockAsync(decision.LockId);
    /// </code>
    /// <b>Critical:</b> Failure to release locks leads to orphaned locks and blocked conversations.
    /// Always release in a <c>finally</c> block.
    /// </remarks>
    public string? LockId { get; init; }
}
