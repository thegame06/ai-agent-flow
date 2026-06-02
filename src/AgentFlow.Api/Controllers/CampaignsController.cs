using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tenants/{tenantId}")]
public sealed class CampaignsController : ControllerBase
{
    private readonly ICampaignStore _store;
    private readonly ICampaignExecutionService _executionService;
    private readonly ITenantContextAccessor _tenantContext;

    public CampaignsController(ICampaignStore store, ICampaignExecutionService executionService, ITenantContextAccessor tenantContext)
    {
        _store = store;
        _executionService = executionService;
        _tenantContext = tenantContext;
    }

    [HttpGet("campaigns")]
    public async Task<IActionResult> GetCampaigns([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _store.GetCampaignsAsync(tenantId, ct));
    }

    [HttpPost("campaigns")]
    public async Task<IActionResult> CreateCampaign([FromRoute] string tenantId, [FromBody] UpsertCampaignRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var now = DateTimeOffset.UtcNow;
        var actor = _tenantContext.Current!.UserId;
        var contract = ToContract(tenantId, request, Guid.NewGuid().ToString("N"), null, now, actor, _executionService.ComputeNextRunAt);
        var saved = await _store.UpsertCampaignAsync(contract, ct);
        return Ok(saved);
    }

    [HttpGet("campaigns/{campaignId}")]
    public async Task<IActionResult> GetCampaign([FromRoute] string tenantId, [FromRoute] string campaignId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var item = await _store.GetCampaignAsync(tenantId, campaignId, ct);
        return item is null ? NotFound(new { message = "Campaign not found." }) : Ok(item);
    }

    [HttpPut("campaigns/{campaignId}")]
    public async Task<IActionResult> UpdateCampaign([FromRoute] string tenantId, [FromRoute] string campaignId, [FromBody] UpsertCampaignRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var existing = await _store.GetCampaignAsync(tenantId, campaignId, ct);
        if (existing is null) return NotFound(new { message = "Campaign not found." });
        var actor = _tenantContext.Current!.UserId;
        var contract = ToContract(tenantId, request, campaignId, existing, DateTimeOffset.UtcNow, actor, _executionService.ComputeNextRunAt);
        return Ok(await _store.UpsertCampaignAsync(contract, ct));
    }

    [HttpPost("campaigns/{campaignId}/publish")]
    public async Task<IActionResult> PublishCampaign([FromRoute] string tenantId, [FromRoute] string campaignId, CancellationToken ct)
        => await SetStatusAsync(tenantId, campaignId, CampaignStatus.Published, true, ct);

    [HttpPost("campaigns/{campaignId}/pause")]
    public async Task<IActionResult> PauseCampaign([FromRoute] string tenantId, [FromRoute] string campaignId, CancellationToken ct)
        => await SetStatusAsync(tenantId, campaignId, CampaignStatus.Paused, false, ct);

    [HttpPost("campaigns/{campaignId}/resume")]
    public async Task<IActionResult> ResumeCampaign([FromRoute] string tenantId, [FromRoute] string campaignId, CancellationToken ct)
        => await SetStatusAsync(tenantId, campaignId, CampaignStatus.Active, true, ct);

    [HttpPost("campaigns/{campaignId}/simulate")]
    public async Task<IActionResult> SimulateCampaign([FromRoute] string tenantId, [FromRoute] string campaignId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var campaign = await _store.GetCampaignAsync(tenantId, campaignId, ct);
        if (campaign is null) return NotFound(new { message = "Campaign not found." });
        return Ok(await _executionService.SimulateAsync(tenantId, campaign, ct));
    }

    [HttpPost("campaigns/{campaignId}/run-now")]
    public async Task<IActionResult> RunNow([FromRoute] string tenantId, [FromRoute] string campaignId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();
        return Ok(await _executionService.RunNowAsync(tenantId, campaignId, _tenantContext.Current!.UserId, CampaignRunTrigger.Manual, ct));
    }

    [HttpGet("campaigns/{campaignId}/runs")]
    public async Task<IActionResult> GetRuns([FromRoute] string tenantId, [FromRoute] string campaignId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _store.GetRunsAsync(tenantId, campaignId, 200, ct));
    }

    [HttpGet("campaigns/{campaignId}/metrics")]
    public async Task<IActionResult> GetMetrics([FromRoute] string tenantId, [FromRoute] string campaignId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _executionService.GetMetricsAsync(tenantId, campaignId, ct));
    }

    [HttpGet("campaign-runs")]
    public async Task<IActionResult> GetCampaignRuns([FromRoute] string tenantId, [FromQuery] string? campaignId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _store.GetRunsAsync(tenantId, campaignId, limit, ct));
    }

    [HttpGet("campaign-runs/{runId}")]
    public async Task<IActionResult> GetRun([FromRoute] string tenantId, [FromRoute] string runId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var run = await _store.GetRunAsync(tenantId, runId, ct);
        if (run is null) return NotFound(new { message = "Run not found." });
        var contacts = await _store.GetContactExecutionsAsync(tenantId, runId, ct);
        var callOutcomes = await _store.GetCallOutcomesByRunAsync(tenantId, runId, ct);
        return Ok(new { run, contacts, callOutcomes });
    }

    [HttpPost("campaign-runs/{runId}/retry-failures")]
    public async Task<IActionResult> RetryFailures([FromRoute] string tenantId, [FromRoute] string runId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();
        return Ok(await _executionService.RetryFailuresAsync(tenantId, runId, _tenantContext.Current!.UserId, ct));
    }

    [HttpGet("campaign-runs/{runId}/contacts")]
    public async Task<IActionResult> GetRunContacts([FromRoute] string tenantId, [FromRoute] string runId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _store.GetContactExecutionsAsync(tenantId, runId, ct));
    }

    private async Task<IActionResult> SetStatusAsync(string tenantId, string campaignId, CampaignStatus status, bool enabled, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var existing = await _store.GetCampaignAsync(tenantId, campaignId, ct);
        if (existing is null) return NotFound(new { message = "Campaign not found." });
        var updated = existing with
        {
            Status = status,
            Enabled = enabled,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = _tenantContext.Current!.UserId,
            NextRunAt = enabled ? _executionService.ComputeNextRunAt(existing with { Status = status, Enabled = enabled }, DateTimeOffset.UtcNow) : null
        };
        return Ok(await _store.UpsertCampaignAsync(updated, ct));
    }

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }

    private static CampaignContract ToContract(
        string tenantId,
        UpsertCampaignRequest request,
        string id,
        CampaignContract? existing,
        DateTimeOffset now,
        string actor,
        Func<CampaignContract, DateTimeOffset, DateTimeOffset?> nextRunResolver)
    {
        var draft = new CampaignContract
        {
            Id = id,
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Status = request.Status ?? existing?.Status ?? CampaignStatus.Draft,
            CampaignType = request.CampaignType ?? existing?.CampaignType ?? CampaignType.Custom,
            ExecutionMode = request.ExecutionMode ?? existing?.ExecutionMode ?? CampaignExecutionMode.Workflow,
            TriggerType = request.TriggerType ?? existing?.TriggerType ?? CampaignTriggerType.Schedule,
            ChannelAction = request.ChannelAction ?? existing?.ChannelAction ?? CampaignChannelAction.WorkflowStart,
            Channel = request.Channel ?? existing?.Channel ?? "whatsapp",
            Goal = request.Goal ?? existing?.Goal ?? string.Empty,
            PlaybookId = request.PlaybookId ?? existing?.PlaybookId,
            WorkflowDefinitionId = request.WorkflowDefinitionId ?? existing?.WorkflowDefinitionId,
            AssistantId = request.AssistantId ?? existing?.AssistantId,
            RuntimeModelProfileId = request.RuntimeModelProfileId ?? existing?.RuntimeModelProfileId,
            TemplateId = request.TemplateId ?? existing?.TemplateId,
            MessageDraft = request.MessageDraft ?? existing?.MessageDraft,
            CallScriptDraft = request.CallScriptDraft ?? existing?.CallScriptDraft,
            PromptOrigin = request.PromptOrigin ?? existing?.PromptOrigin,
            ScheduleType = request.ScheduleType ?? existing?.ScheduleType ?? CampaignScheduleType.Once,
            ScheduleExpression = request.ScheduleExpression ?? existing?.ScheduleExpression ?? string.Empty,
            Timezone = request.Timezone ?? existing?.Timezone ?? "America/Managua",
            StartAt = request.StartAt ?? existing?.StartAt ?? now,
            EndAt = request.EndAt ?? existing?.EndAt,
            ExecutionWindowJson = request.ExecutionWindowJson ?? existing?.ExecutionWindowJson,
            ThrottleJson = request.ThrottleJson ?? existing?.ThrottleJson,
            SegmentId = request.SegmentId ?? existing?.SegmentId,
            AudienceFilterJson = request.AudienceFilterJson ?? existing?.AudienceFilterJson ?? "{}",
            DedupePolicyJson = request.DedupePolicyJson ?? existing?.DedupePolicyJson,
            SuccessPolicyJson = request.SuccessPolicyJson ?? existing?.SuccessPolicyJson,
            FollowupPolicyJson = request.FollowupPolicyJson ?? existing?.FollowupPolicyJson,
            ResultMappingJson = request.ResultMappingJson ?? existing?.ResultMappingJson,
            Enabled = request.Enabled ?? existing?.Enabled ?? true,
            LastRunAt = existing?.LastRunAt,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            UpdatedBy = actor
        };

        return draft with
        {
            NextRunAt = draft.Enabled ? nextRunResolver(draft, now) : null
        };
    }
}

public sealed record UpsertCampaignRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public CampaignStatus? Status { get; init; }
    public CampaignType? CampaignType { get; init; }
    public CampaignExecutionMode? ExecutionMode { get; init; }
    public CampaignTriggerType? TriggerType { get; init; }
    public CampaignChannelAction? ChannelAction { get; init; }
    public string? Channel { get; init; }
    public string? Goal { get; init; }
    public string? PlaybookId { get; init; }
    public string? WorkflowDefinitionId { get; init; }
    public string? AssistantId { get; init; }
    public string? RuntimeModelProfileId { get; init; }
    public string? TemplateId { get; init; }
    public string? MessageDraft { get; init; }
    public string? CallScriptDraft { get; init; }
    public string? PromptOrigin { get; init; }
    public CampaignScheduleType? ScheduleType { get; init; }
    public string? ScheduleExpression { get; init; }
    public string? Timezone { get; init; }
    public DateTimeOffset? StartAt { get; init; }
    public DateTimeOffset? EndAt { get; init; }
    public string? ExecutionWindowJson { get; init; }
    public string? ThrottleJson { get; init; }
    public string? SegmentId { get; init; }
    public string? AudienceFilterJson { get; init; }
    public string? DedupePolicyJson { get; init; }
    public string? SuccessPolicyJson { get; init; }
    public string? FollowupPolicyJson { get; init; }
    public string? ResultMappingJson { get; init; }
    public bool? Enabled { get; init; }
}
