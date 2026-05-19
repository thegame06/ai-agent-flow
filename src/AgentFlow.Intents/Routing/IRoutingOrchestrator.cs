using AgentFlow.Intents.Classification.Models;
using AgentFlow.Intents.Routing.Models;

namespace AgentFlow.Intents.Routing;

/// <summary>
/// Orchestrates message routing decisions based on intent classification and conversation ownership.
/// The core decision-making component of the Intent Routing system.
/// </summary>
/// <remarks>
/// <para>
/// The Routing Orchestrator is responsible for:
/// </para>
/// <list type="number">
///   <item><description><b>Confidence Validation:</b> Determines if classification confidence is sufficient for auto-routing.</description></item>
///   <item><description><b>Conflict Detection:</b> Checks if another agent owns the conversation (golden rule: 1 agent per conversation).</description></item>
///   <item><description><b>Lock Acquisition:</b> Acquires distributed lock for conversation ownership if routing proceeds.</description></item>
///   <item><description><b>Decision Logic:</b> Decides whether to Route, Queue, Reject, or Fallback.</description></item>
///   <item><description><b>Audit Logging:</b> Records full decision reasoning for compliance and debugging.</description></item>
/// </list>
/// <para><b>Decision Flow:</b></para>
/// <code>
/// Classification Result
///   ↓
/// Validate Confidence
///   ↓
/// Check Workflow/Agent Configuration
///   ↓
/// Verify Ownership State
///   ↓
/// Acquire Lock (if needed)
///   ↓
/// Return RoutingDecision
///   ↓
/// Audit Decision
/// </code>
/// <para><b>Usage Pattern:</b></para>
/// <code>
/// var orchestrator = serviceProvider.GetRequiredService&lt;IRoutingOrchestrator&gt;();
/// 
/// // 1. Classify the message
/// var classification = await scoringEngine.ClassifyAsync(message, tenantId, channel);
/// 
/// // 2. Make routing decision
/// var decision = await orchestrator.RouteMessageAsync(
///     classification,
///     new ConversationContext
///     {
///         ConversationId = "conv-123",
///         TenantId = tenantId,
///         Channel = "whatsapp",
///         UserIdentifier = "+50581143874"
///     });
/// 
/// // 3. Act on decision
/// switch (decision.Action)
/// {
///     case RoutingAction.Route:
///         await workflowEngine.ExecuteAsync(decision.WorkflowDefinitionId);
///         break;
///     case RoutingAction.Queue:
///         await humanQueue.EnqueueAsync(decision);
///         break;
///     case RoutingAction.Reject:
///         _logger.LogWarning("Agent conflict: {Explanation}", decision.ExplanationJson);
///         break;
///     case RoutingAction.Fallback:
///         await fallbackHandler.HandleAsync(decision);
///         break;
/// }
/// </code>
/// <para><b>Enterprise Considerations:</b></para>
/// <list type="bullet">
///   <item><description><b>Idempotency:</b> Safe to call multiple times for the same message (lock acquisition is atomic).</description></item>
///   <item><description><b>Audit Compliance:</b> Every decision is logged with full reasoning for regulatory review.</description></item>
///   <item><description><b>Conflict Prevention:</b> Enforces single-agent rule to prevent dual-agent scenarios in banking/insurance.</description></item>
///   <item><description><b>Resilience:</b> Audit failures do not break routing (logged but not thrown).</description></item>
/// </list>
/// </remarks>
public interface IRoutingOrchestrator
{
    /// <summary>
    /// Routes a classified message based on intent, confidence, and conversation ownership.
    /// </summary>
    /// <param name="classification">The result of intent classification from the scoring engine.</param>
    /// <param name="context">The conversation context including tenant, channel, and ownership state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="RoutingDecision"/> indicating what action to take and why.</returns>
    /// <remarks>
    /// <para><b>Decision Matrix:</b></para>
    /// <list type="table">
    ///   <listheader>
    ///     <term>Scenario</term>
    ///     <description>Action</description>
    ///   </listheader>
    ///   <item>
    ///     <term>High/Medium confidence + workflow configured + lock acquired</term>
    ///     <description><see cref="RoutingAction.Route"/> - Execute immediately</description>
    ///   </item>
    ///   <item>
    ///     <term>Low confidence (0.50-0.74)</term>
    ///     <description><see cref="RoutingAction.Queue"/> - Human review required</description>
    ///   </item>
    ///   <item>
    ///     <term>No workflow configured for matched intent</term>
    ///     <description><see cref="RoutingAction.Queue"/> - Configuration needed</description>
    ///   </item>
    ///   <item>
    ///     <term>Another agent owns conversation</term>
    ///     <description><see cref="RoutingAction.Reject"/> - Conflict detected</description>
    ///   </item>
    ///   <item>
    ///     <term>Lock acquisition failed</term>
    ///     <description><see cref="RoutingAction.Reject"/> - Cannot acquire ownership</description>
    ///   </item>
    ///   <item>
    ///     <term>No match (confidence &lt; 0.50)</term>
    ///     <description><see cref="RoutingAction.Fallback"/> - Default handler</description>
    ///   </item>
    /// </list>
    /// <para><b>Thread Safety:</b> This method is thread-safe due to atomic lock acquisition via Redis.</para>
    /// <para><b>Performance:</b> Typical latency: 10-50ms (includes lock acquisition and audit logging).</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">If classification or context is null.</exception>
    Task<RoutingDecision> RouteMessageAsync(
        IntentClassificationResult classification,
        ConversationContext context,
        CancellationToken ct = default);
}
