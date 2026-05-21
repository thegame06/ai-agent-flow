using AgentFlow.Application.Memory;
using AgentFlow.Intents.Classification;
using AgentFlow.Intents.Inbox;
using AgentFlow.Intents.Inbox.Models;
using AgentFlow.Api.Workflow;
using AgentFlow.Security;
using AgentFlow.Domain.Repositories;
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
    private readonly IWorkflowStudioStore _workflowStore;
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly IChannelDefinitionRepository _channelRepo;

    public IntentRoutingController(
        IIntentRoutingStore store,
        IAuditMemory auditMemory,
        ITenantContextAccessor tenantContext,
        IIntentScoringEngine intentScoring,
        IConversationInboxService inboxService,
        IWorkflowStudioStore workflowStore,
        IChannelSessionRepository sessionRepo,
        IChannelDefinitionRepository channelRepo)
    {
        _store = store;
        _auditMemory = auditMemory;
        _tenantContext = tenantContext;
        _intentScoring = intentScoring;
        _inboxService = inboxService;
        _workflowStore = workflowStore;
        _sessionRepo = sessionRepo;
        _channelRepo = channelRepo;
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
        var normalizedIntentKey = NormalizeIntentKey(body.IntentKey);
        if (string.IsNullOrWhiteSpace(normalizedIntentKey))
            return BadRequest(new { message = "intentKey invalido. Usa letras, numeros y guiones bajos." });
        if (string.Equals(body.SourceAgentId, body.TargetAgentId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "sourceAgentId y targetAgentId no pueden ser el mismo agente. Configure un destino distinto para ejecutar la intencion."
            });
        }

        if (string.IsNullOrWhiteSpace(body.WorkflowDefinitionId) && string.IsNullOrWhiteSpace(body.TargetAgentId))
        {
            return BadRequest(new { message = "La regla debe definir un workflow destino o un agente de respaldo." });
        }

        var allRules = await _store.GetRulesAsync(tenantId, ct);
        var duplicate = allRules.FirstOrDefault(r =>
            string.Equals(r.IntentKey, normalizedIntentKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Channel ?? string.Empty, body.Channel ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
            return Conflict(new { message = $"Ya existe una intención '{body.IntentKey}' para el canal '{body.Channel ?? "todos"}'." });

        string? workflowName = body.WorkflowName;
        if (!string.IsNullOrWhiteSpace(body.WorkflowDefinitionId))
        {
            var wf = await _workflowStore.GetDefinitionAsync(tenantId, body.WorkflowDefinitionId, ct);
            if (wf is null) return BadRequest(new { message = $"Workflow '{body.WorkflowDefinitionId}' no existe." });
            workflowName = wf.Name;
        }

        var saved = await _store.UpsertRuleAsync(new IntentRoutingRule
        {
            Id = string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("N") : body.Id,
            TenantId = tenantId,
            IntentKey = normalizedIntentKey,
            IntentDescription = body.IntentDescription ?? string.Empty,
            Category = string.IsNullOrWhiteSpace(body.Category) ? "General" : body.Category!,
            ExamplePhrases = body.ExamplePhrases ?? Array.Empty<string>(),
            SourceAgentId = body.SourceAgentId,
            TargetAgentId = body.TargetAgentId ?? body.SourceAgentId,
            WorkflowDefinitionId = body.WorkflowDefinitionId,
            WorkflowName = workflowName,
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
        var normalizedIntentKey = NormalizeIntentKey(body.IntentKey);
        if (string.IsNullOrWhiteSpace(normalizedIntentKey))
            return BadRequest(new { message = "intentKey invalido. Usa letras, numeros y guiones bajos." });
        if (string.Equals(body.SourceAgentId, body.TargetAgentId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "sourceAgentId y targetAgentId no pueden ser el mismo agente. Configure un destino distinto para ejecutar la intencion."
            });
        }

        if (string.IsNullOrWhiteSpace(body.WorkflowDefinitionId) && string.IsNullOrWhiteSpace(body.TargetAgentId))
        {
            return BadRequest(new { message = "La regla debe definir un workflow destino o un agente de respaldo." });
        }

        var existing = await _store.GetRuleByIdAsync(tenantId, ruleId, ct);
        if (existing is null) return NotFound();

        var allRules = await _store.GetRulesAsync(tenantId, ct);
        var duplicate = allRules.FirstOrDefault(r =>
            !string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.IntentKey, normalizedIntentKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Channel ?? string.Empty, body.Channel ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
            return Conflict(new { message = $"Ya existe una intención '{body.IntentKey}' para el canal '{body.Channel ?? "todos"}'." });

        string? workflowName = body.WorkflowName ?? existing.WorkflowName;
        if (!string.IsNullOrWhiteSpace(body.WorkflowDefinitionId))
        {
            var wf = await _workflowStore.GetDefinitionAsync(tenantId, body.WorkflowDefinitionId, ct);
            if (wf is null) return BadRequest(new { message = $"Workflow '{body.WorkflowDefinitionId}' no existe." });
            workflowName = wf.Name;
        }

        var saved = await _store.UpsertRuleAsync(existing with
        {
            IntentKey = normalizedIntentKey,
            IntentDescription = body.IntentDescription ?? existing.IntentDescription,
            Category = string.IsNullOrWhiteSpace(body.Category) ? existing.Category : body.Category!,
            ExamplePhrases = body.ExamplePhrases ?? existing.ExamplePhrases,
            SourceAgentId = body.SourceAgentId,
            TargetAgentId = body.TargetAgentId ?? existing.TargetAgentId,
            WorkflowDefinitionId = body.WorkflowDefinitionId ?? existing.WorkflowDefinitionId,
            WorkflowName = workflowName,
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

    private static string NormalizeIntentKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray());

        while (cleaned.Contains("__", StringComparison.Ordinal))
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);

        return cleaned.Trim('_');
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

    [HttpGet("diagnostics/sessions/{sessionId}")]
    public async Task<IActionResult> GetSessionDiagnostics(
        [FromRoute] string tenantId,
        [FromRoute] string sessionId,
        [FromQuery] string? message,
        CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session is null) return NotFound(new { message = $"Sesion '{sessionId}' no existe." });

        var channel = await _channelRepo.GetByIdAsync(session.ChannelId, tenantId, ct);
        if (channel is null) return NotFound(new { message = $"Canal '{session.ChannelId}' no existe." });

        var channelKey = channel.Type.ToString().ToLowerInvariant();
        var sourceAgentId = !string.IsNullOrWhiteSpace(channel.RouterAgentId)
            ? channel.RouterAgentId
            : channel.Config.GetValueOrDefault("DefaultAgentId") ?? string.Empty;

        var rules = await _store.GetRulesByChannelAsync(tenantId, channelKey, ct);
        var rulesForSource = string.IsNullOrWhiteSpace(sourceAgentId)
            ? rules
            : rules.Where(r => string.Equals(r.SourceAgentId, sourceAgentId, StringComparison.OrdinalIgnoreCase)).ToList();

        object? classification = null;
        if (!string.IsNullOrWhiteSpace(message))
        {
            var result = await _intentScoring.ClassifyAsync(message, tenantId, channelKey, ct);
            classification = new
            {
                input = message,
                best_intent = result.BestMatch?.IntentKey,
                best_score = result.BestScore,
                confidence = result.Confidence.ToString(),
                requires_human_review = result.RequiresHumanReview,
                top_candidates = result.AllCandidates
                    .Take(3)
                    .Select(c => new
                    {
                        intent_key = c.IntentKey,
                        score = c.SimilarityScore,
                        workflow_id = c.Rule.WorkflowDefinitionId,
                        target_agent_id = c.Rule.TargetAgentId,
                        source_agent_id = c.Rule.SourceAgentId
                    })
                    .ToArray()
            };
        }

        return Ok(new
        {
            session = new
            {
                id = session.Id,
                channel_id = session.ChannelId,
                channel = channelKey,
                identifier = session.Identifier,
                owner_agent_id = session.AgentId,
                thread_id = session.ThreadId,
                status = session.Status.ToString(),
                last_activity_at = session.LastActivityAt,
                last_inbound_at = session.Metadata.TryGetValue("last_incoming_at", out var lastIn) ? lastIn : null,
                last_outbound_at = session.Metadata.TryGetValue("last_outgoing_at", out var lastOut) ? lastOut : null
            },
            routing = new
            {
                router_agent_id = channel.RouterAgentId,
                default_agent_id = channel.Config.GetValueOrDefault("DefaultAgentId"),
                source_agent_id_used_for_rules = sourceAgentId,
                channel_rule_count = rules.Count,
                source_rule_count = rulesForSource.Count,
                rules = rulesForSource
                    .OrderBy(r => r.Priority)
                    .Select(r => new
                    {
                        r.Id,
                        r.IntentKey,
                        r.Category,
                        r.Enabled,
                        r.Priority,
                        r.Channel,
                        r.SourceAgentId,
                        r.TargetAgentId,
                        r.WorkflowDefinitionId
                    })
                    .ToArray()
            },
            classification
        });
    }
}

public sealed record UpsertIntentRuleRequest
{
    public string? Id { get; init; }
    public required string IntentKey { get; init; }
    public string? IntentDescription { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string>? ExamplePhrases { get; init; }
    public required string SourceAgentId { get; init; }
    public string? TargetAgentId { get; init; }
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
