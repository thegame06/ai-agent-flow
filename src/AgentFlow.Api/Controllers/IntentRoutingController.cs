using AgentFlow.Application.Memory;
using AgentFlow.Intents.Classification;
using AgentFlow.Intents.Inbox;
using AgentFlow.Intents.Inbox.Models;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/intent-routing")]
[Authorize]
public sealed class IntentRoutingController : ControllerBase
{
    private readonly IIntentRoutingStore _store;
    private readonly IAuditMemory _auditMemory;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IIntentScoringEngine _intentScoring;
    private readonly IConversationInboxService _inboxService;

    public IntentRoutingController(
        IIntentRoutingStore store,
        IAuditMemory auditMemory,
        ITenantContextAccessor tenantContext,
        IIntentScoringEngine intentScoring,
        IConversationInboxService inboxService)
    {
        _store = store;
        _auditMemory = auditMemory;
        _tenantContext = tenantContext;
        _intentScoring = intentScoring;
        _inboxService = inboxService;
    }

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var rules = await _store.GetRulesAsync(tenantId, ct);
        return Ok(rules);
    }

    [HttpPost("rules")]
    public async Task<IActionResult> UpsertRule([FromRoute] string tenantId, [FromBody] UpsertIntentRuleRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();
        if (string.Equals(body.SourceAgentId, body.TargetAgentId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "sourceAgentId y targetAgentId no pueden ser el mismo agente. Configure un destino distinto para ejecutar la intencion."
            });
        }

        var saved = await _store.UpsertRuleAsync(new IntentRoutingRule
        {
            Id = string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("N") : body.Id,
            TenantId = tenantId,
            IntentKey = body.IntentKey,
            IntentDescription = body.IntentDescription ?? string.Empty,
            ExamplePhrases = body.ExamplePhrases ?? Array.Empty<string>(),
            SourceAgentId = body.SourceAgentId,
            TargetAgentId = body.TargetAgentId,
            WorkflowDefinitionId = body.WorkflowDefinitionId,
            WorkflowName = body.WorkflowName,
            Priority = body.Priority,
            Enabled = body.Enabled,
            Channel = body.Channel,
            ConditionsJson = body.ConditionsJson,
            HandoffPolicyJson = body.HandoffPolicyJson,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        return Ok(saved);
    }

    [HttpPut("rules/{ruleId}")]
    public async Task<IActionResult> UpdateRule([FromRoute] string tenantId, [FromRoute] string ruleId, [FromBody] UpsertIntentRuleRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();
        if (string.Equals(body.SourceAgentId, body.TargetAgentId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "sourceAgentId y targetAgentId no pueden ser el mismo agente. Configure un destino distinto para ejecutar la intencion."
            });
        }

        var existing = await _store.GetRuleByIdAsync(tenantId, ruleId, ct);
        if (existing is null) return NotFound();

        var saved = await _store.UpsertRuleAsync(existing with
        {
            IntentKey = body.IntentKey,
            IntentDescription = body.IntentDescription ?? existing.IntentDescription,
            ExamplePhrases = body.ExamplePhrases ?? existing.ExamplePhrases,
            SourceAgentId = body.SourceAgentId,
            TargetAgentId = body.TargetAgentId,
            WorkflowDefinitionId = body.WorkflowDefinitionId ?? existing.WorkflowDefinitionId,
            WorkflowName = body.WorkflowName ?? existing.WorkflowName,
            Priority = body.Priority,
            Enabled = body.Enabled,
            Channel = body.Channel,
            ConditionsJson = body.ConditionsJson,
            HandoffPolicyJson = body.HandoffPolicyJson,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        return Ok(saved);
    }

    [HttpPatch("rules/{ruleId}/enable")]
    public async Task<IActionResult> SetRuleEnabled([FromRoute] string tenantId, [FromRoute] string ruleId, [FromBody] SetEnabledRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var ok = await _store.SetRuleEnabledAsync(tenantId, ruleId, body.Enabled, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpDelete("rules/{ruleId}")]
    public async Task<IActionResult> DeleteRule([FromRoute] string tenantId, [FromRoute] string ruleId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var ok = await _store.DeleteRuleAsync(tenantId, ruleId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate([FromRoute] string tenantId, [FromBody] SimulateIntentRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var result = await _store.SimulateAsync(tenantId, body.SourceAgentId, body.Intent, body.Channel, ct);

        await _auditMemory.RecordAsync(new AuditEntry
        {
            ExecutionId = result.MatchedRuleId ?? "intent-routing-simulate",
            AgentId = body.SourceAgentId,
            TenantId = tenantId,
            UserId = context.UserId,
            EventType = AuditEventType.RoutingDecision,
            CorrelationId = $"routing:{tenantId}:{body.SourceAgentId}:{body.Intent}",
            EventJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                sourceAgentId = body.SourceAgentId,
                intent = body.Intent,
                channel = body.Channel,
                matchedRuleId = result.MatchedRuleId,
                selectedAgentId = result.SelectedAgentId,
                result.FallbackUsed,
                result.DecisionReason
            })
        }, ct);

        return Ok(result);
    }

    [HttpPost("classify")]
    public async Task<IActionResult> Classify([FromRoute] string tenantId, [FromBody] ClassifyIntentRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var startedAt = DateTimeOffset.UtcNow;
        var classification = await _intentScoring.ClassifyAsync(body.Message, tenantId, body.Channel, ct);
        var elapsedMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

        return Ok(new
        {
            best_match = classification.BestMatch == null
                ? null
                : new
                {
                    intent_key = classification.BestMatch.IntentKey,
                    intent_name = classification.BestMatch.Rule.IntentKey,
                    description = classification.BestMatch.Rule.IntentDescription
                },
            best_score = classification.BestScore,
            confidence = classification.Confidence.ToString(),
            all_candidates = classification.AllCandidates.Select(c => new
            {
                intent_key = c.IntentKey,
                intent_name = c.Rule.IntentKey,
                score = c.SimilarityScore,
                matched_features = c.Rule.ExamplePhrases.Take(3).ToArray()
            }).ToArray(),
            explanation_json = classification.ExplanationJson,
            processing_time_ms = elapsedMs
        });
    }

    [HttpGet("agents")]
    public async Task<IActionResult> GetAgents([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var agents = await _store.GetAgentsAsync(tenantId, ct);
        return Ok(agents);
    }

    [HttpPatch("agents/{agentId}")]
    public async Task<IActionResult> UpsertAgent([FromRoute] string tenantId, [FromRoute] string agentId, [FromBody] UpsertAgentRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        if (string.Equals(body.AgentType, "subagent", StringComparison.OrdinalIgnoreCase) && body.ExternalReplyAllowed)
            return BadRequest(new { error = "subagents cannot have externalReplyAllowed=true in production" });

        var saved = await _store.UpsertAgentAsync(new AgentRegistryEntry
        {
            Id = string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("N") : body.Id,
            TenantId = tenantId,
            AgentId = agentId,
            AgentType = body.AgentType,
            Enabled = body.Enabled,
            TestModeAllowed = body.TestModeAllowed,
            ExternalReplyAllowed = body.ExternalReplyAllowed,
            Capabilities = body.Capabilities ?? Array.Empty<string>(),
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        return Ok(saved);
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var result = await _inboxService.GetPendingAsync(tenantId, new InboxFilter { Page = 1, PageSize = 100 }, ct);
        return Ok(result.Items);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var stats = await _inboxService.GetStatsAsync(tenantId, ct);
        return Ok(new
        {
            total = stats.TotalConversations,
            awaiting_classification = stats.AwaitingClassification,
            classified = stats.ByState.TryGetValue(ConversationState.Classified, out var classified) ? classified : 0,
            in_progress = stats.InProgress,
            resolved_today = stats.ResolvedToday,
            avg_confidence = 0d,
            requires_review = stats.RequiresReview
        });
    }

    [HttpPost("conversations/{conversationId}/reassign")]
    public async Task<IActionResult> ReassignConversation(
        [FromRoute] string tenantId,
        [FromRoute] string conversationId,
        [FromBody] ReassignConversationRequest body,
        CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var note = string.IsNullOrWhiteSpace(body.NewIntent)
            ? "Manual reassignment requested."
            : $"Manual reassignment to intent '{body.NewIntent}'.";
        var ok = await _inboxService.UpdateStateAsync(tenantId, conversationId, ConversationState.Classified, note, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("conversations/{conversationId}/resolve")]
    public async Task<IActionResult> ResolveConversation([FromRoute] string tenantId, [FromRoute] string conversationId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var ok = await _inboxService.UpdateStateAsync(tenantId, conversationId, ConversationState.Resolved, "Resolved from inbox.", ct);
        return ok ? NoContent() : NotFound();
    }
}

public sealed record UpsertIntentRuleRequest
{
    public string? Id { get; init; }
    public required string IntentKey { get; init; }
    public string? IntentDescription { get; init; }
    public IReadOnlyList<string>? ExamplePhrases { get; init; }
    public required string SourceAgentId { get; init; }
    public required string TargetAgentId { get; init; }
    public string? WorkflowDefinitionId { get; init; }
    public string? WorkflowName { get; init; }
    public required int Priority { get; init; }
    public bool Enabled { get; init; } = true;
    public string? Channel { get; init; }
    public string? ConditionsJson { get; init; }
    public string? HandoffPolicyJson { get; init; }
}

public sealed record SetEnabledRequest(bool Enabled);

public sealed record SimulateIntentRequest
{
    public required string SourceAgentId { get; init; }
    public required string Intent { get; init; }
    public string? Channel { get; init; }
}

public sealed record ClassifyIntentRequest
{
    public required string Message { get; init; }
    public string? Channel { get; init; }
}

public sealed record ReassignConversationRequest
{
    public string? NewIntent { get; init; }
}

public sealed record UpsertAgentRequest
{
    public string? Id { get; init; }
    public required string AgentType { get; init; }
    public required bool Enabled { get; init; }
    public required bool TestModeAllowed { get; init; }
    public required bool ExternalReplyAllowed { get; init; }
    public IReadOnlyList<string>? Capabilities { get; init; }
}
