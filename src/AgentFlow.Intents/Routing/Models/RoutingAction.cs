namespace AgentFlow.Intents.Routing.Models;

/// <summary>
/// Defines the routing actions that can be taken after intent classification.
/// </summary>
/// <remarks>
/// <para>
/// This enum represents the core decision outcomes of the Routing Orchestrator.
/// Each action has specific implications for downstream processing and audit trails.
/// </para>
/// <para><b>Action Semantics:</b></para>
/// <list type="bullet">
///   <item><description><b>Route:</b> High confidence match with workflow available and lock acquired. Execute immediately.</description></item>
///   <item><description><b>Queue:</b> Match found but requires human review (low confidence or no workflow configured).</description></item>
///   <item><description><b>Reject:</b> Agent conflict detected. Another agent owns the conversation. Cannot proceed.</description></item>
///   <item><description><b>Fallback:</b> No viable intent match found. Send to default handler or human queue.</description></item>
/// </list>
/// <para><b>Decision Flow:</b></para>
/// <code>
/// Message → Classification → Orchestration →
///   • Route (execute workflow)
///   • Queue (human review)
///   • Reject (conflict detected)
///   • Fallback (no match)
/// </code>
/// </remarks>
public enum RoutingAction
{
    /// <summary>
    /// Execute the workflow immediately.
    /// Indicates high confidence match, workflow available, and lock acquired.
    /// </summary>
    /// <remarks>
    /// <b>Preconditions:</b>
    /// <list type="bullet">
    ///   <item>Confidence ≥ Medium (0.75+)</item>
    ///   <item>WorkflowDefinitionId or TargetAgentId is present</item>
    ///   <item>Conversation lock acquired successfully</item>
    ///   <item>No ownership conflicts detected</item>
    /// </list>
    /// <b>Next Step:</b> Trigger workflow execution engine.
    /// </remarks>
    Route,

    /// <summary>
    /// Enqueue for human review before execution.
    /// Indicates match found but confidence below auto-route threshold or no workflow configured.
    /// </summary>
    /// <remarks>
    /// <b>Triggers:</b>
    /// <list type="bullet">
    ///   <item>Low confidence (0.50-0.74)</item>
    ///   <item>Workflow not configured for matched intent</item>
    ///   <item>Ambiguous match (multiple high-scoring intents)</item>
    /// </list>
    /// <b>Next Step:</b> Send to human supervisor queue with classification context.
    /// </remarks>
    Queue,

    /// <summary>
    /// Reject routing due to agent conflict.
    /// Indicates another agent currently owns the conversation.
    /// </summary>
    /// <remarks>
    /// <b>Triggers:</b>
    /// <list type="bullet">
    ///   <item>Conversation locked by different agent</item>
    ///   <item>Lock acquisition failed</item>
    ///   <item>Ownership conflict detected</item>
    /// </list>
    /// <b>Next Step:</b> Return conflict response to caller. Do not execute.
    /// <b>Compliance Note:</b> Critical for preventing dual-agent scenarios in regulated environments.
    /// </remarks>
    Reject,

    /// <summary>
    /// Send to fallback handler (no match found).
    /// Indicates confidence below minimum threshold or no viable intents detected.
    /// </summary>
    /// <remarks>
    /// <b>Triggers:</b>
    /// <list type="bullet">
    ///   <item>NoMatch confidence level (&lt; 0.50)</item>
    ///   <item>No semantic or keyword matches above threshold</item>
    ///   <item>Message out of scope for all registered intents</item>
    /// </list>
    /// <b>Next Step:</b> Route to default fallback agent or generic help flow.
    /// </remarks>
    Fallback
}
