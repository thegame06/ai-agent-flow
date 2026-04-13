using AgentFlow.Abstractions;
using AgentFlow.Application.Memory;
using AgentFlow.Domain.Repositories;
using AgentFlow.Evaluation;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}")]
[Authorize]
public sealed class AgentExecutionsController : ControllerBase
{
    private readonly IAgentExecutor _executor;
    private readonly IAgentDefinitionRepository _agentRepository;
    private readonly IAgentExecutionRepository _executionRepository;
    private readonly ICanaryRoutingService _canaryRouting;
    private readonly ISegmentRoutingService _segmentRouting;
    private readonly IAgentAuthorizationService _authz;
    private readonly IAgentHandoffExecutor _handoffExecutor;
    private readonly IManagerHandoffPolicy _handoffPolicy;
    private readonly IAuditMemory _auditMemory;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ILogger<AgentExecutionsController> _logger;

    public AgentExecutionsController(
        IAgentExecutor executor,
        IAgentDefinitionRepository agentRepository,
        IAgentExecutionRepository executionRepository,
        ICanaryRoutingService canaryRouting,
        ISegmentRoutingService segmentRouting,
        IAgentAuthorizationService authz,
        IAgentHandoffExecutor handoffExecutor,
        IManagerHandoffPolicy handoffPolicy,
        IAuditMemory auditMemory,
        ITenantContextAccessor tenantContext,
        ILogger<AgentExecutionsController> logger)
    {
        _executor = executor;
        _agentRepository = agentRepository;
        _executionRepository = executionRepository;
        _canaryRouting = canaryRouting;
        _segmentRouting = segmentRouting;
        _authz = authz;
        _handoffExecutor = handoffExecutor;
        _handoffPolicy = handoffPolicy;
        _auditMemory = auditMemory;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// List all executions for the tenant.
    /// </summary>
    [HttpGet("executions")]
    public async Task<IActionResult> GetAllExecutionsAsync(
        [FromRoute] string tenantId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        // Assuming the repository has a way to get all (we might need to add it or use GetByAgentId with null/empty)
        // For now let's say we filter by running or just get recent.
        var history = await _executionRepository.GetRunningAsync(tenantId, ct); 
        // NOTE: In a real scenario we'd have a GetRecentAsync(tenantId, limit)
        return Ok(history);
    }

    /// <summary>
    /// List execution history for a specific agent.
    /// </summary>
    [HttpGet("agents/{agentId}/executions")]
    public async Task<IActionResult> GetHistoryAsync(
        [FromRoute] string tenantId,
        [FromRoute] string agentId,
        [FromQuery] int limit = 20,
        [FromQuery] string? threadId = null,
        [FromQuery] string? sessionId = null,
        CancellationToken ct = default)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var fetchLimit = Math.Max(limit, 100);
        var history = await _executionRepository.GetByAgentIdAsync(agentId, tenantId, fetchLimit, ct);
        
        // Materialize data from MongoDB before projecting (avoid "Expression not supported" error)
        var executions = history.ToList();

        // Optional session/thread filtering (threadId and sessionId map to CorrelationId)
        if (!string.IsNullOrWhiteSpace(threadId))
        {
            executions = executions.Where(e => e.CorrelationId == threadId).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(sessionId))
        {
            executions = executions.Where(e => e.CorrelationId == sessionId).ToList();
        }

        executions = executions.Take(limit).ToList();
        
        return Ok(executions.Select(e => new
        {
            e.Id,
            Status = e.Status.ToString(),
            e.CreatedAt,
            DurationMs = e.GetDuration()?.TotalMilliseconds,
            TotalSteps = e.Steps.Count,
            TotalTokensUsed = e.Output?.TotalTokensUsed ?? 0,
            e.AgentDefinitionId,
            e.CorrelationId
        }));
    }

    /// <summary>
    /// Get full details of a specific execution by ID (without requiring agentId).
    /// </summary>
    [HttpGet("executions/{executionId}")]
    [AllowAnonymous] // TODO: Remove in production
    public async Task<IActionResult> GetExecutionByIdAsync(
        [FromRoute] string tenantId,
        [FromRoute] string executionId,
        CancellationToken ct = default)
    {
        var context = _tenantContext.Current ?? new TenantContext
        {
            TenantId = tenantId,
            UserId = "anonymous-user",
            IsPlatformAdmin = false,
            Roles = new[] { "developer" },
            Permissions = AgentFlow.Security.AgentFlowRoles.Developer.ToList()
        };

        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var execution = await _executionRepository.GetByIdAsync(executionId, tenantId, ct);
        if (execution == null) return NotFound();

        return Ok(execution);
    }

    /// <summary>
    /// Get full details of a specific execution, including steps (requires agentId for validation).
    /// </summary>
    [HttpGet("agents/{agentId}/executions/{executionId}")]
    public async Task<IActionResult> GetDetailsAsync(
        [FromRoute] string tenantId,
        [FromRoute] string agentId,
        [FromRoute] string executionId,
        CancellationToken ct = default)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var execution = await _executionRepository.GetByIdAsync(executionId, tenantId, ct);
        if (execution == null) return NotFound();

        // Safety check: ensure agentId matches
        if (execution.AgentDefinitionId != agentId) return BadRequest("Execution does not belong to the specified agent.");

        return Ok(execution);
    }

    /// <summary>
    /// Trigger a new agent execution.
    /// </summary>
    [HttpPost("agents/{agentId}/trigger")]
    [AllowAnonymous] // TODO: Remove in production - for development testing only
    [ProducesResponseType(typeof(TriggerExecutionResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TriggerAsync(
        [FromRoute] string tenantId,
        [FromRoute] string agentId,
        [FromBody] TriggerExecutionRequest body,
        CancellationToken ct)
    {
        // For development: allow anonymous access with default context
        var context = _tenantContext.Current ?? new TenantContext
        {
            TenantId = tenantId,
            UserId = "anonymous-user",
            IsPlatformAdmin = false,
            Roles = new[] { "developer" },
            Permissions = AgentFlow.Security.AgentFlowRoles.Developer.ToList()
        };

        if (context.TenantId != tenantId && !context.IsPlatformAdmin)
        {
            _logger.LogWarning(
                "SECURITY: Cross-tenant execution attempt by {UserId} targeting {TargetTenant}",
                context.UserId, tenantId);

            return Forbid();
        }

        if (!await _authz.CanTriggerExecutionAsync(context, agentId, ct))
            return Forbid();

        // ─────────────────────────────────────────────────────────────────────
        // EXPERIMENTATION LAYER: Segment Routing → Canary Routing
        // ─────────────────────────────────────────────────────────────────────
        var agentDef = await _agentRepository.GetByIdAsync(agentId, tenantId, ct);
        if (agentDef is null)
            return NotFound(new { error = $"Agent {agentId} not found in tenant {tenantId}" });

        string selectedAgentId = agentId;
        string routingStrategy = "original";
        var routingMetadata = new Dictionary<string, string>();

        // 1. SEGMENT ROUTING (Priority 1: Most specific)
        if (body.UserSegments is not null && body.UserSegments.Count > 0)
        {
            var segmentContext = new SegmentRoutingContext
            {
                UserId = context.UserId,
                UserSegments = body.UserSegments,
                Metadata = new Dictionary<string, string>()
            };

            var segmentDecision = await _segmentRouting.SelectAgentForSegmentAsync(
                tenantId, agentId, segmentContext, ct);

            if (segmentDecision.WasRouted)
            {
                selectedAgentId = segmentDecision.SelectedAgentId;
                routingStrategy = "segment";
                routingMetadata["SegmentRoutingRule"] = segmentDecision.MatchedRule?.RuleName ?? "default";
                routingMetadata["SegmentRoutingReason"] = segmentDecision.Reason;
                routingMetadata["EvaluatedSegments"] = string.Join(",", segmentDecision.EvaluatedSegments);

                _logger.LogInformation(
                    "Segment routing: {OriginalAgent} → {SelectedAgent} (Rule: {Rule})",
                    agentId, selectedAgentId, segmentDecision.MatchedRule?.RuleName);
            }
        }

        // 2. CANARY ROUTING (Priority 2: Fallback for gradual rollout)
        // Only if segment routing didn't already change the agent
        if (selectedAgentId == agentId && _canaryRouting.IsCanaryActive(agentDef.CanaryAgentId, agentDef.CanaryWeight))
        {
            var requestId = Guid.NewGuid().ToString(); // Unique per request
            var canarySelectedId = _canaryRouting.SelectAgentForExecution(
                agentId,
                agentDef.CanaryAgentId,
                agentDef.CanaryWeight,
                requestId);

            if (canarySelectedId != agentId)
            {
                selectedAgentId = canarySelectedId;
                routingStrategy = "canary";
                routingMetadata["CanaryWeight"] = agentDef.CanaryWeight.ToString("F2");

                _logger.LogInformation(
                    "Canary routing: {OriginalAgent} → {CanaryAgent} (Weight: {Weight})",
                    agentId, selectedAgentId, agentDef.CanaryWeight);
            }
        }

        // 3. BUILD EXECUTION REQUEST
        var metadata = new Dictionary<string, string>(routingMetadata)
        {
            ["RoutingStrategy"] = routingStrategy,
            ["OriginalAgentId"] = agentId
        };

        var request = new AgentExecutionRequest
        {
            TenantId = context.TenantId,
            AgentKey = selectedAgentId,
            UserId = context.UserId,
            UserMessage = body.Message,
            SessionId = body.SessionId, // ✅ Session continuity support
            ThreadId = body.ThreadId,   // ✅ Thread continuity support
            CorrelationId = body.ThreadId ?? body.SessionId, // Group executions by active conversation when available
            Priority = Enum.TryParse<ExecutionPriority>(body.Priority ?? "Normal", true, out var p) ? p : ExecutionPriority.Normal,
            Metadata = metadata
        };

        var result = await _executor.ExecuteAsync(request, ct);

        if (result.Status == ExecutionStatus.Failed && result.ErrorCode == "NotFound")
        {
            return NotFound(new { error = result.ErrorMessage });
        }

        if (result.Status == ExecutionStatus.Failed)
        {
            return StatusCode(500, new { error = result.ErrorMessage, code = result.ErrorCode });
        }

        return Accepted(new TriggerExecutionResponse
        {
            ExecutionId = result.ExecutionId,
            Status = result.Status.ToString(),
            CreatedAt = DateTimeOffset.UtcNow, // Simplified for DTO
            Duration = result.DurationMs,
            ThreadId = result.ThreadId // ✅ Thread ID for multi-turn conversations
        });
    }


    /// <summary>
    /// Returns effective allowed target subagents for a manager agent in a tenant.
    /// </summary>
    [HttpGet("agents/{agentId}/handoff/allowed-targets")]
    [ProducesResponseType(typeof(HandoffAllowedTargetsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetAllowedHandoffTargets(
        [FromRoute] string tenantId,
        [FromRoute] string agentId)
    {
        var context = _tenantContext.Current!;

        if (context.TenantId != tenantId && !context.IsPlatformAdmin)
            return Forbid();

        var targets = _handoffPolicy.GetAllowedTargets(tenantId, agentId);

        return Ok(new HandoffAllowedTargetsResponse
        {
            TenantId = tenantId,
            SourceAgentId = agentId,
            Targets = targets
        });
    }

    /// <summary>
    /// Evaluates handoff policy decision for source->target without executing a handoff.
    /// </summary>
    [HttpGet("agents/{agentId}/handoff/decision")]
    [ProducesResponseType(typeof(HandoffPolicyDecisionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetHandoffDecision(
        [FromRoute] string tenantId,
        [FromRoute] string agentId,
        [FromQuery] string targetAgentId)
    {
        var context = _tenantContext.Current!;

        if (context.TenantId != tenantId && !context.IsPlatformAdmin)
            return Forbid();

        if (string.IsNullOrWhiteSpace(targetAgentId))
            return BadRequest(new { error = "targetAgentId is required." });

        var decision = _handoffPolicy.Evaluate(tenantId, agentId, targetAgentId);

        return Ok(new HandoffPolicyDecisionResponse
        {
            TenantId = tenantId,
            SourceAgentId = agentId,
            TargetAgentId = targetAgentId,
            Allowed = decision.Allowed,
            Reason = decision.Reason,
            HasExplicitPolicy = decision.HasExplicitPolicy,
            AllowedTargets = decision.AllowedTargets
        });
    }

    /// <summary>
    /// Internal handoff from a manager agent to a specialist subagent.
    /// </summary>
    [HttpPost("agents/{agentId}/handoff")]
    [ProducesResponseType(typeof(HandoffExecutionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> HandoffAsync(
        [FromRoute] string tenantId,
        [FromRoute] string agentId,
        [FromBody] HandoffExecutionRequest body,
        CancellationToken ct)
    {
        var context = _tenantContext.Current!;

        if (context.TenantId != tenantId && !context.IsPlatformAdmin)
            return Forbid();

        if (!await _authz.CanHandoffExecutionAsync(context, agentId, body.TargetAgentId, ct))
            return Forbid();

        if (!_handoffPolicy.IsAllowed(tenantId, agentId, body.TargetAgentId))
        {
            await _auditMemory.RecordAsync(new AuditEntry
            {
                ExecutionId = body.SessionId,
                AgentId = agentId,
                TenantId = tenantId,
                UserId = context.UserId,
                EventType = AuditEventType.SecurityViolation,
                CorrelationId = body.CorrelationId ?? body.SessionId,
                EventJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    reason = "handoff_target_not_allowed",
                    sourceAgent = agentId,
                    targetAgent = body.TargetAgentId,
                    body.Intent,
                    body.SessionId
                })
            }, ct);

            return Forbid();
        }

        if (!TryValidateHandoffPayload(body, out var validationError))
            return BadRequest(new { error = validationError });

        var handoff = new AgentHandoffRequest
        {
            TenantId = tenantId,
            SessionId = body.SessionId,
            ThreadId = body.ThreadId,
            CorrelationId = body.CorrelationId ?? body.SessionId,
            ContractVersion = body.ContractVersion,
            SourceAgentKey = agentId,
            TargetAgentKey = body.TargetAgentId,
            Intent = body.Intent,
            PayloadJson = body.PayloadJson,
            PolicyContext = body.PolicyContext ?? new Dictionary<string, string>(),
            Metadata = body.Metadata ?? new Dictionary<string, string>()
        };

        await _auditMemory.RecordAsync(new AuditEntry
        {
            ExecutionId = handoff.SessionId,
            AgentId = handoff.SourceAgentKey,
            TenantId = tenantId,
            UserId = context.UserId,
            EventType = AuditEventType.HandoffRequested,
            CorrelationId = handoff.CorrelationId,
            EventJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                sourceAgent = handoff.SourceAgentKey,
                targetAgent = handoff.TargetAgentKey,
                handoff.Intent,
                handoff.SessionId
            })
        }, ct);

        var result = await _handoffExecutor.ExecuteAsync(handoff, ct);

        await _auditMemory.RecordAsync(new AuditEntry
        {
            ExecutionId = handoff.SessionId,
            AgentId = handoff.SourceAgentKey,
            TenantId = tenantId,
            UserId = context.UserId,
            EventType = result.Ok ? AuditEventType.HandoffCompleted : AuditEventType.HandoffFailed,
            CorrelationId = handoff.CorrelationId,
            EventJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                sourceAgent = handoff.SourceAgentKey,
                targetAgent = handoff.TargetAgentKey,
                handoff.Intent,
                handoff.SessionId,
                result.Ok,
                result.ErrorCode,
                result.Retryable
            })
        }, ct);

        return Ok(new HandoffExecutionResponse
        {
            ContractVersion = result.ContractVersion,
            SessionId = result.SessionId,
            ThreadId = result.ThreadId,
            CorrelationId = result.CorrelationId,
            Ok = result.Ok,
            ErrorCode = result.ErrorCode,
            Retryable = result.Retryable,
            ResultJson = result.ResultJson,
            StatePatch = result.StatePatch,
            ToolCalls = result.ToolCalls
        });
    }


    private static bool TryValidateHandoffPayload(HandoffExecutionRequest body, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(body.SessionId))
        {
            error = "sessionId is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(body.TargetAgentId))
        {
            error = "targetAgentId is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(body.ThreadId))
        {
            error = "threadId is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(body.Intent))
        {
            error = "intent is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(body.PayloadJson))
        {
            error = "payloadJson is required.";
            return false;
        }

        if (body.PayloadJson.Length > 50_000)
        {
            error = "payloadJson exceeds max allowed size (50k).";
            return false;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body.PayloadJson);
            if (doc.RootElement.ValueKind is not (System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array))
            {
                error = "payloadJson must be a JSON object or array.";
                return false;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            error = "payloadJson must be valid JSON.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Cancel a running execution.
    /// </summary>
    [HttpDelete("{executionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CancelAsync(
        [FromRoute] string tenantId,
        [FromRoute] string agentId,
        [FromRoute] string executionId,
        CancellationToken ct)
    {
        var context = _tenantContext.Current!;

        if (context.TenantId != tenantId && !context.IsPlatformAdmin)
            return Forbid();

        if (!await _authz.CanCancelExecutionAsync(context, executionId, ct))
            return Forbid();

        var result = await _executor.CancelAsync(executionId, context.TenantId, context.UserId, ct);

        return result.IsSuccess
            ? NoContent()
            : NotFound(new { error = result.Error!.Message });
    }

    /// <summary>
    /// Preview/Dry-Run: Execute an agent without persisting the result.
    /// Useful for testing agents in Draft status or validating changes before deployment.
    /// </summary>
    [HttpPost("agents/{agentId}/preview")]
    [ProducesResponseType(typeof(PreviewExecutionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewAsync(
        [FromRoute] string tenantId,
        [FromRoute] string agentId,
        [FromBody] PreviewExecutionRequest body,
        CancellationToken ct)
    {
        var context = _tenantContext.Current!;

        if (context.TenantId != tenantId && !context.IsPlatformAdmin)
        {
            _logger.LogWarning(
                "SECURITY: Cross-tenant preview attempt by {UserId} targeting {TargetTenant}",
                context.UserId, tenantId);

            return Forbid();
        }

        // Verify agent exists (allow Draft for preview)
        var agentDef = await _agentRepository.GetByIdAsync(agentId, tenantId, ct);
        if (agentDef is null)
            return NotFound(new { error = $"Agent {agentId} not found in tenant {tenantId}" });

        // Build execution request with preview metadata
        var metadata = new Dictionary<string, string>
        {
            ["IsPreview"] = "true",
            ["PreviewMode"] = "dry-run",
            ["OriginalAgentId"] = agentId
        };

        var request = new AgentExecutionRequest
        {
            TenantId = context.TenantId,
            AgentKey = agentId,
            UserId = context.UserId,
            UserMessage = body.Message,
            Priority = ExecutionPriority.Normal,
            Metadata = metadata
        };

        _logger.LogInformation(
            "Preview execution started: Agent {AgentId} by {UserId} in tenant {TenantId}",
            agentId, context.UserId, tenantId);

        var result = await _executor.ExecuteAsync(request, ct);

        if (result.Status == ExecutionStatus.Failed)
        {
            return Ok(new PreviewExecutionResponse
            {
                Success = false,
                ExecutionId = result.ExecutionId,
                Status = result.Status.ToString(),
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                TotalSteps = result.TotalSteps,
                TotalTokensUsed = result.TotalTokensUsed,
                DurationMs = result.DurationMs
            });
        }

        return Ok(new PreviewExecutionResponse
        {
            Success = true,
            ExecutionId = result.ExecutionId,
            Status = result.Status.ToString(),
            FinalResponse = result.FinalResponse,
            TotalSteps = result.TotalSteps,
            TotalTokensUsed = result.TotalTokensUsed,
            DurationMs = result.DurationMs,
            RuntimeSnapshot = new RuntimeSnapshotDto
            {
                AgentVersion = result.RuntimeSnapshot.AgentVersion,
                ModelId = result.RuntimeSnapshot.ModelId,
                Temperature = result.RuntimeSnapshot.Temperature
            }
        });
    }
}

// --- DTOs ---

public sealed record TriggerExecutionRequest
{
    public required string Message { get; init; }
    public string? ContextJson { get; init; }
    public Dictionary<string, string>? Variables { get; init; }
    public string? Language { get; init; }
    public string? Priority { get; init; }
    
    /// <summary>
    /// Stable session identifier for conversation continuity.
    /// When provided, backend will reuse the same auto-created thread for this session.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Thread ID for multi-turn conversations.
    /// If provided, the execution will continue the existing thread.
    /// If null and agent has AutoCreateThread=true, a new thread will be created/reused (by SessionId when available).
    /// </summary>
    public string? ThreadId { get; init; }
    
    /// <summary>
    /// User segments for segment-based routing (e.g., "premium", "beta", "enterprise").
    /// If provided, segment routing will be evaluated before canary routing.
    /// </summary>
    public IReadOnlyList<string>? UserSegments { get; init; }
}

public sealed record TriggerExecutionResponse
{
    public required string ExecutionId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public double? Duration { get; init; }
    
    /// <summary>
    /// Thread ID for multi-turn conversations.
    /// Returned when the agent has thread persistence enabled.
    /// Use this ID in subsequent requests to continue the conversation.
    /// </summary>
    public string? ThreadId { get; init; }
}

public sealed record PreviewExecutionRequest
{
    public required string Message { get; init; }
    public Dictionary<string, string>? Variables { get; init; }
}

public sealed record PreviewExecutionResponse
{
    public required bool Success { get; init; }
    public required string ExecutionId { get; init; }
    public required string Status { get; init; }
    public string? FinalResponse { get; init; }
    public int TotalSteps { get; init; }
    public int TotalTokensUsed { get; init; }
    public long DurationMs { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public RuntimeSnapshotDto? RuntimeSnapshot { get; init; }
}


public sealed record HandoffAllowedTargetsResponse
{
    public required string TenantId { get; init; }
    public required string SourceAgentId { get; init; }
    public IReadOnlyList<string> Targets { get; init; } = Array.Empty<string>();
}

public sealed record HandoffPolicyDecisionResponse
{
    public required string TenantId { get; init; }
    public required string SourceAgentId { get; init; }
    public required string TargetAgentId { get; init; }
    public required bool Allowed { get; init; }
    public required string Reason { get; init; }
    public required bool HasExplicitPolicy { get; init; }
    public IReadOnlyList<string> AllowedTargets { get; init; } = Array.Empty<string>();
}

public sealed record HandoffExecutionRequest
{
    public string ContractVersion { get; init; } = AgentHandoffRequest.CurrentContractVersion;
    public required string SessionId { get; init; }
    public required string ThreadId { get; init; }
    public string? CorrelationId { get; init; }
    public required string TargetAgentId { get; init; }
    public required string Intent { get; init; }
    public required string PayloadJson { get; init; }
    public Dictionary<string, string>? PolicyContext { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record HandoffExecutionResponse
{
    public string ContractVersion { get; init; } = AgentHandoffRequest.CurrentContractVersion;
    public required string SessionId { get; init; }
    public required string ThreadId { get; init; }
    public required string CorrelationId { get; init; }
    public required bool Ok { get; init; }
    public string? ResultJson { get; init; }
    public string? ErrorCode { get; init; }
    public bool Retryable { get; init; }
    public IReadOnlyDictionary<string, string> StatePatch { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<AgentHandoffToolCall> ToolCalls { get; init; } = new List<AgentHandoffToolCall>();
}

public sealed record RuntimeSnapshotDto
{
    public string AgentVersion { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public double Temperature { get; init; }
}
