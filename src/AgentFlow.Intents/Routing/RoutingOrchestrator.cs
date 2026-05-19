using System.Text.Json;
using AgentFlow.Application.Memory;
using AgentFlow.Intents.Classification.Models;
using AgentFlow.Intents.Ownership;
using AgentFlow.Intents.Ownership.Models;
using AgentFlow.Intents.Routing.Models;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Intents.Routing;

/// <summary>
/// Production implementation of the Routing Orchestrator.
/// Coordinates intent classification, ownership management, and routing decisions.
/// </summary>
/// <remarks>
/// <para>
/// This is the **core decision-making component** of the Intent Routing system.
/// It enforces:
/// </para>
/// <list type="bullet">
///   <item><description><b>Confidence Thresholds:</b> Routes only when confidence is sufficient.</description></item>
///   <item><description><b>Single-Agent Rule:</b> Prevents multiple agents from owning the same conversation.</description></item>
///   <item><description><b>Audit Compliance:</b> Logs every decision for regulatory review.</description></item>
/// </list>
/// <para><b>Dependencies:</b></para>
/// <list type="bullet">
///   <item><see cref="IConversationOwnershipManager"/> - For distributed lock management</item>
///   <item><see cref="IAuditMemory"/> - For decision audit trails</item>
///   <item><see cref="ILogger{TCategoryName}"/> - For operational logging</item>
/// </list>
/// <para><b>Error Handling:</b></para>
/// <list type="bullet">
///   <item>Audit failures are logged but do not break routing (resilience pattern)</item>
///   <item>Lock acquisition failures result in <see cref="RoutingAction.Reject"/></item>
///   <item>Missing workflow configuration results in <see cref="RoutingAction.Queue"/></item>
/// </list>
/// </remarks>
public sealed class RoutingOrchestrator : IRoutingOrchestrator
{
    private readonly IConversationOwnershipManager _ownershipManager;
    private readonly IAuditMemory _auditMemory;
    private readonly ILogger<RoutingOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingOrchestrator"/> class.
    /// </summary>
    /// <param name="ownershipManager">The conversation ownership manager.</param>
    /// <param name="auditMemory">The audit memory for decision logging.</param>
    /// <param name="logger">The logger for operational diagnostics.</param>
    public RoutingOrchestrator(
        IConversationOwnershipManager ownershipManager,
        IAuditMemory auditMemory,
        ILogger<RoutingOrchestrator> logger)
    {
        _ownershipManager = ownershipManager ?? throw new ArgumentNullException(nameof(ownershipManager));
        _auditMemory = auditMemory ?? throw new ArgumentNullException(nameof(auditMemory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RoutingDecision> RouteMessageAsync(
        IntentClassificationResult classification,
        ConversationContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(context);

        _logger.LogInformation(
            "Routing message for conversation {ConvId} (tenant: {TenantId}, channel: {Channel})",
            context.ConversationId,
            context.TenantId,
            context.Channel);

        // ========================================
        // STEP 1: VALIDATE CONFIDENCE
        // ========================================

        // No match found - send to fallback
        if (classification.Confidence == ConfidenceLevel.NoMatch)
        {
            _logger.LogInformation(
                "No intent match for conversation {ConvId} (score: {Score:F3})",
                context.ConversationId,
                classification.BestScore);

            var fallbackDecision = BuildFallbackDecision("no_match", classification);
            await AuditDecisionAsync(fallbackDecision, context, classification, ct);
            return fallbackDecision;
        }

        // Low confidence - requires human review
        if (classification.RequiresHumanReview)
        {
            _logger.LogInformation(
                "Low confidence for conversation {ConvId} (intent: {Intent}, score: {Score:F3})",
                context.ConversationId,
                classification.BestMatch?.IntentKey ?? "unknown",
                classification.BestScore);

            var queueDecision = BuildQueueDecision("low_confidence", classification);
            await AuditDecisionAsync(queueDecision, context, classification, ct);
            return queueDecision;
        }

        var bestMatch = classification.BestMatch!;

        // ========================================
        // STEP 2: VALIDATE WORKFLOW/AGENT CONFIGURATION
        // ========================================

        // Intent matched but no workflow or agent configured
        if (string.IsNullOrEmpty(bestMatch.Rule.WorkflowDefinitionId) &&
            string.IsNullOrEmpty(bestMatch.Rule.TargetAgentId))
        {
            _logger.LogWarning(
                "Intent {IntentKey} matched but has no workflow or agent configured (conversation: {ConvId})",
                bestMatch.IntentKey,
                context.ConversationId);

            var queueDecision = BuildQueueDecision("no_workflow_configured", classification);
            await AuditDecisionAsync(queueDecision, context, classification, ct);
            return queueDecision;
        }

        // ========================================
        // STEP 3: VERIFY OWNERSHIP STATE
        // ========================================

        var ownershipState = await _ownershipManager.GetStateAsync(
            context.TenantId,
            context.ConversationId,
            ct);

        // Agent conflict detected - another agent owns the conversation
        if (ownershipState.IsLocked &&
            ownershipState.CurrentOwnerAgentId != bestMatch.Rule.TargetAgentId)
        {
            _logger.LogWarning(
                "Agent conflict detected for conversation {ConvId}: {CurrentOwner} owns, {NewAgent} attempted routing",
                context.ConversationId,
                ownershipState.CurrentOwnerAgentId,
                bestMatch.Rule.TargetAgentId);

            var rejectDecision = BuildRejectDecision("agent_conflict", classification, ownershipState);
            await AuditDecisionAsync(rejectDecision, context, classification, ct);
            return rejectDecision;
        }

        // ========================================
        // STEP 4: ACQUIRE CONVERSATION LOCK
        // ========================================

        OwnershipLock? lockAcquired = null;

        // Only acquire lock if a target agent is specified
        if (!string.IsNullOrEmpty(bestMatch.Rule.TargetAgentId))
        {
            lockAcquired = await _ownershipManager.TryAcquireLockAsync(
                context.TenantId,
                context.ConversationId,
                bestMatch.Rule.TargetAgentId,
                TimeSpan.FromMinutes(5), // Default TTL: 5 minutes
                ct);

            if (lockAcquired == null)
            {
                _logger.LogWarning(
                    "Failed to acquire lock for conversation {ConvId} (agent: {AgentId})",
                    context.ConversationId,
                    bestMatch.Rule.TargetAgentId);

                var rejectDecision = BuildRejectDecision("lock_failed", classification, ownershipState);
                await AuditDecisionAsync(rejectDecision, context, classification, ct);
                return rejectDecision;
            }

            _logger.LogInformation(
                "Lock acquired for conversation {ConvId} by agent {AgentId} (lock: {LockId})",
                context.ConversationId,
                bestMatch.Rule.TargetAgentId,
                lockAcquired.LockId);
        }

        // ========================================
        // STEP 5: BUILD ROUTING DECISION
        // ========================================

        var decision = new RoutingDecision
        {
            IntentKey = bestMatch.IntentKey,
            WorkflowDefinitionId = bestMatch.Rule.WorkflowDefinitionId,
            TargetAgentId = bestMatch.Rule.TargetAgentId,
            Action = RoutingAction.Route,
            ReasonCode = "matched",
            ExplanationJson = JsonSerializer.Serialize(new
            {
                intent = bestMatch.IntentKey,
                confidence = classification.BestScore,
                confidence_level = classification.Confidence.ToString(),
                workflow = bestMatch.Rule.WorkflowName,
                workflow_id = bestMatch.Rule.WorkflowDefinitionId,
                agent = bestMatch.Rule.TargetAgentId,
                lock_acquired = lockAcquired != null,
                lock_id = lockAcquired?.LockId,
                priority = bestMatch.Rule.Priority.ToString(),
                channel = context.Channel,
                decision_timestamp = DateTimeOffset.UtcNow
            }),
            DecidedAt = DateTimeOffset.UtcNow,
            LockId = lockAcquired?.LockId
        };

        // ========================================
        // STEP 6: AUDIT DECISION
        // ========================================

        await AuditDecisionAsync(decision, context, classification, ct);

        _logger.LogInformation(
            "Routing decision: {Action} for conversation {ConvId} (intent: {Intent}, workflow: {Workflow})",
            decision.Action,
            context.ConversationId,
            decision.IntentKey,
            decision.WorkflowDefinitionId ?? "none");

        return decision;
    }

    // ============================================================
    // HELPER METHODS - BUILD DECISION RECORDS
    // ============================================================

    /// <summary>
    /// Builds a Fallback decision (no viable match found).
    /// </summary>
    private RoutingDecision BuildFallbackDecision(
        string reasonCode,
        IntentClassificationResult classification)
    {
        return new RoutingDecision
        {
            IntentKey = classification.BestMatch?.IntentKey ?? "unknown",
            WorkflowDefinitionId = null,
            TargetAgentId = null,
            Action = RoutingAction.Fallback,
            ReasonCode = reasonCode,
            ExplanationJson = JsonSerializer.Serialize(new
            {
                reason = reasonCode,
                confidence = classification.BestScore,
                confidence_level = classification.Confidence.ToString(),
                message = classification.Message,
                requires_review = true,
                candidates_count = classification.AllCandidates.Count,
                decision = "fallback_to_default_handler"
            }),
            DecidedAt = DateTimeOffset.UtcNow,
            LockId = null
        };
    }

    /// <summary>
    /// Builds a Queue decision (requires human review).
    /// </summary>
    private RoutingDecision BuildQueueDecision(
        string reasonCode,
        IntentClassificationResult classification)
    {
        return new RoutingDecision
        {
            IntentKey = classification.BestMatch?.IntentKey ?? "unknown",
            WorkflowDefinitionId = classification.BestMatch?.Rule.WorkflowDefinitionId,
            TargetAgentId = classification.BestMatch?.Rule.TargetAgentId,
            Action = RoutingAction.Queue,
            ReasonCode = reasonCode,
            ExplanationJson = JsonSerializer.Serialize(new
            {
                reason = reasonCode,
                confidence = classification.BestScore,
                confidence_level = classification.Confidence.ToString(),
                intent = classification.BestMatch?.IntentKey,
                workflow = classification.BestMatch?.Rule.WorkflowName,
                requires_human_review = true,
                candidates = classification.AllCandidates.Select(c => new
                {
                    intent = c.IntentKey,
                    score = c.SimilarityScore
                }).Take(5).ToList()
            }),
            DecidedAt = DateTimeOffset.UtcNow,
            LockId = null
        };
    }

    /// <summary>
    /// Builds a Reject decision (agent conflict or lock acquisition failed).
    /// </summary>
    private RoutingDecision BuildRejectDecision(
        string reasonCode,
        IntentClassificationResult classification,
        ConversationOwnershipState ownershipState)
    {
        return new RoutingDecision
        {
            IntentKey = classification.BestMatch?.IntentKey ?? "unknown",
            WorkflowDefinitionId = null,
            TargetAgentId = null,
            Action = RoutingAction.Reject,
            ReasonCode = reasonCode,
            ExplanationJson = JsonSerializer.Serialize(new
            {
                reason = reasonCode,
                current_owner = ownershipState.CurrentOwnerAgentId,
                locked_until = ownershipState.LockedUntil,
                attempted_agent = classification.BestMatch?.Rule.TargetAgentId,
                attempted_intent = classification.BestMatch?.IntentKey,
                conflict_detected = true,
                message = "Cannot route - another agent owns the conversation"
            }),
            DecidedAt = DateTimeOffset.UtcNow,
            LockId = null
        };
    }

    // ============================================================
    // AUDIT TRAIL
    // ============================================================

    /// <summary>
    /// Records the routing decision to the audit trail.
    /// Failures are logged but do not break routing (resilience pattern).
    /// </summary>
    private async Task AuditDecisionAsync(
        RoutingDecision decision,
        ConversationContext context,
        IntentClassificationResult classification,
        CancellationToken ct)
    {
        try
        {
            await _auditMemory.RecordAsync(new AuditEntry
            {
                TenantId = context.TenantId,
                EventType = AuditEventType.RoutingDecision,
                CorrelationId = context.ConversationId,
                UserId = context.UserIdentifier,
                AgentId = decision.TargetAgentId ?? string.Empty,
                ExecutionId = string.Empty, // No execution ID yet (routing is pre-execution)
                EventJson = JsonSerializer.Serialize(new
                {
                    conversation_id = context.ConversationId,
                    channel = context.Channel,
                    intent_key = decision.IntentKey,
                    action = decision.Action.ToString(),
                    reason_code = decision.ReasonCode,
                    workflow_id = decision.WorkflowDefinitionId,
                    target_agent = decision.TargetAgentId,
                    confidence = classification.BestScore,
                    confidence_level = classification.Confidence.ToString(),
                    lock_id = decision.LockId,
                    decided_at = decision.DecidedAt,
                    message_preview = classification.Message.Length > 100
                        ? classification.Message.Substring(0, 100) + "..."
                        : classification.Message,
                    explanation = decision.ExplanationJson
                })
            }, ct);

            _logger.LogDebug(
                "Audit recorded for routing decision (conversation: {ConvId}, action: {Action})",
                context.ConversationId,
                decision.Action);
        }
        catch (Exception ex)
        {
            // Log but do not throw - audit failures should not break routing
            _logger.LogError(
                ex,
                "Failed to audit routing decision for conversation {ConvId} (action: {Action})",
                context.ConversationId,
                decision.Action);
        }
    }
}
