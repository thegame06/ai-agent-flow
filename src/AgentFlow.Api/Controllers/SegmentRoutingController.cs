using AgentFlow.Evaluation;
using AgentFlow.Observability;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentFlow.Api.Controllers;

/// <summary>
/// API for managing segment-based routing in the Experimentation Layer.
/// Routes users to different agent versions based on their segments.
/// </summary>
[ApiController]
[Route("api/v1/tenants/{tenantId}/segment-routing")]
[Authorize]
public sealed class SegmentRoutingController : ControllerBase
{
    private readonly ISegmentRoutingService _segmentRouting;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ILogger<SegmentRoutingController> _logger;

    public SegmentRoutingController(
        ISegmentRoutingService segmentRouting,
        ITenantContextAccessor tenantContext,
        ILogger<SegmentRoutingController> logger)
    {
        _segmentRouting = segmentRouting;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Preview which agent would be selected for a given user context.
    /// </summary>
    [HttpPost("agents/{agentId}/preview")]
    [ProducesResponseType(typeof(SegmentRoutingPreviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewRoutingAsync(
        [FromRoute] string tenantId,
        [FromRoute] string agentId,
        [FromBody] SegmentRoutingPreviewRequest request,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin)
            return Forbid();

        var context = new SegmentRoutingContext
        {
            UserId = request.UserId,
            UserSegments = request.UserSegments,
            Metadata = request.Metadata ?? new Dictionary<string, string>()
        };

        var decision = await _segmentRouting.SelectAgentForSegmentAsync(
            tenantId, agentId, context, ct);

        sw.Stop();
        var primarySegment = request.UserSegments.FirstOrDefault() ?? "unknown";
        AgentFlowTelemetry.ApiEndpointLatency.Record(sw.Elapsed.TotalMilliseconds, new TagList
        {
            { "controller", "SegmentRoutingController" },
            { "action", "PreviewRoutingAsync" }
        });
        AgentFlowTelemetry.SegmentRoutingDecisions.Add(1, new TagList
        {
            { "tenant_id", tenantId },
            { "agent_id", agentId },
            { "segment", primarySegment },
            { "was_routed", decision.WasRouted.ToString().ToLowerInvariant() }
        });
        AgentFlowTelemetry.CanaryAssignments.Add(1, new TagList
        {
            { "tenant_id", tenantId },
            { "agent_id", agentId },
            { "segment", primarySegment },
            { "variant", decision.SelectedAgentId == agentId ? "champion" : "challenger" }
        });

        return Ok(new SegmentRoutingPreviewResponse
        {
            OriginalAgentId = agentId,
            SelectedAgentId = decision.SelectedAgentId,
            WasRouted = decision.WasRouted,
            MatchedRuleName = decision.MatchedRule?.RuleName,
            Reason = decision.Reason,
            EvaluatedSegments = decision.EvaluatedSegments
        });
    }

    /// <summary>
    /// Get current segment routing configuration for an agent.
    /// </summary>
    [HttpGet("agents/{agentId}")]
    [ProducesResponseType(typeof(SegmentRoutingConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSegmentRoutingAsync(
        [FromRoute] string tenantId,
        [FromRoute] string agentId,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin)
            return Forbid();

        var config = await _segmentRouting.GetSegmentRoutingAsync(tenantId, agentId, ct);

        if (config is null)
            return NotFound(new { error = "No segment routing configured for this agent" });

        sw.Stop();
        AgentFlowTelemetry.ApiEndpointLatency.Record(sw.Elapsed.TotalMilliseconds, new TagList
        {
            { "controller", "SegmentRoutingController" },
            { "action", "GetSegmentRoutingAsync" }
        });

        return Ok(new SegmentRoutingConfigurationDto
        {
            AgentId = config.AgentId,
            IsEnabled = config.IsEnabled,
            Rules = config.Rules.Select(r => new SegmentRoutingRuleDto
            {
                RuleName = r.RuleName,
                MatchSegments = r.MatchSegments,
                TargetAgentId = r.TargetAgentId,
                Priority = r.Priority,
                RequireAllSegments = r.RequireAllSegments
            }).ToList(),
            DefaultTargetAgentId = config.DefaultTargetAgentId,
            CreatedAt = config.CreatedAt,
            CreatedBy = config.CreatedBy
        });
    }

    /// <summary>
    /// Configure or update segment routing for an agent.
    /// </summary>
    [HttpPut("agents/{agentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetSegmentRoutingAsync(
        [FromRoute] string tenantId,
        [FromRoute] string agentId,
        [FromBody] SegmentRoutingUpdateRequest request,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin)
            return Forbid();

        var config = new SegmentRoutingConfiguration
        {
            AgentId = agentId,
            TenantId = tenantId,
            IsEnabled = request.IsEnabled,
            Rules = request.Rules.Select(r => new SegmentRoutingRule
            {
                RuleName = r.RuleName,
                MatchSegments = r.MatchSegments,
                TargetAgentId = r.TargetAgentId,
                Priority = r.Priority,
                RequireAllSegments = r.RequireAllSegments
            }).ToList(),
            DefaultTargetAgentId = request.DefaultTargetAgentId,
            CreatedBy = ctx.UserId
        };

        var result = await _segmentRouting.SetSegmentRoutingAsync(tenantId, agentId, config, ct);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error!.Message });

        _logger.LogInformation(
            "Segment routing configured for agent {AgentId} by {UserId}. Enabled={IsEnabled}, Rules={RuleCount}",
            agentId, ctx.UserId, request.IsEnabled, request.Rules.Count);
        sw.Stop();
        AgentFlowTelemetry.ApiEndpointLatency.Record(sw.Elapsed.TotalMilliseconds, new TagList
        {
            { "controller", "SegmentRoutingController" },
            { "action", "SetSegmentRoutingAsync" }
        });

        return Ok(new { message = "Segment routing configured successfully" });
    }

    /// <summary>
    /// Disable segment routing for an agent (without deleting configuration).
    /// </summary>
    [HttpPost("agents/{agentId}/disable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisableSegmentRoutingAsync(
        [FromRoute] string tenantId,
        [FromRoute] string agentId,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin)
            return Forbid();

        var existing = await _segmentRouting.GetSegmentRoutingAsync(tenantId, agentId, ct);
        
        if (existing is null)
            return NotFound(new { error = "No segment routing configured for this agent" });

        var updated = existing with { IsEnabled = false };
        await _segmentRouting.SetSegmentRoutingAsync(tenantId, agentId, updated, ct);

        _logger.LogInformation(
            "Segment routing disabled for agent {AgentId} by {UserId}",
            agentId, ctx.UserId);
        sw.Stop();
        AgentFlowTelemetry.ApiEndpointLatency.Record(sw.Elapsed.TotalMilliseconds, new TagList
        {
            { "controller", "SegmentRoutingController" },
            { "action", "DisableSegmentRoutingAsync" }
        });

        return Ok(new { message = "Segment routing disabled" });
    }
}

// --- DTOs ---

public sealed record SegmentRoutingPreviewRequest
{
    public required string UserId { get; init; }
    public required IReadOnlyList<string> UserSegments { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record SegmentRoutingPreviewResponse
{
    public required string OriginalAgentId { get; init; }
    public required string SelectedAgentId { get; init; }
    public required bool WasRouted { get; init; }
    public string? MatchedRuleName { get; init; }
    public required string Reason { get; init; }
    public required IReadOnlyList<string> EvaluatedSegments { get; init; }
}

public sealed record SegmentRoutingConfigurationDto
{
    public required string AgentId { get; init; }
    public required bool IsEnabled { get; init; }
    public required IReadOnlyList<SegmentRoutingRuleDto> Rules { get; init; }
    public string? DefaultTargetAgentId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
}

public sealed record SegmentRoutingRuleDto
{
    public required string RuleName { get; init; }
    public required IReadOnlyList<string> MatchSegments { get; init; }
    public required string TargetAgentId { get; init; }
    public required int Priority { get; init; }
    public required bool RequireAllSegments { get; init; }
}

public sealed record SegmentRoutingUpdateRequest
{
    public required bool IsEnabled { get; init; }
    public required IReadOnlyList<SegmentRoutingRuleDto> Rules { get; init; }
    public string? DefaultTargetAgentId { get; init; }
}
