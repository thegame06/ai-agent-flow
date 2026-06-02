using System.Text.Json;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tenants/{tenantId}/campaign-segments")]
public sealed class CampaignSegmentsController : ControllerBase
{
    private readonly ICampaignStore _store;
    private readonly ICampaignAudienceService _audienceService;
    private readonly ITenantContextAccessor _tenantContext;

    public CampaignSegmentsController(
        ICampaignStore store,
        ICampaignAudienceService audienceService,
        ITenantContextAccessor tenantContext)
    {
        _store = store;
        _audienceService = audienceService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetSegments([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _store.GetSegmentsAsync(tenantId, ct));
    }

    [HttpPost]
    public async Task<IActionResult> CreateSegment([FromRoute] string tenantId, [FromBody] UpsertCampaignSegmentRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var now = DateTimeOffset.UtcNow;
        var actor = _tenantContext.Current!.UserId;
        var preview = await _audienceService.PreviewAsync(tenantId, request.FilterJson ?? "{}", null, ct);

        var segment = await _store.UpsertSegmentAsync(new CampaignSegmentContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            SourceModules = request.SourceModules.Count == 0 ? ["commerce", "inbox", "audit", "threads"] : request.SourceModules,
            FilterJson = request.FilterJson ?? "{}",
            EstimatedCount = preview.EstimatedCount,
            SamplePreviewJson = JsonSerializer.Serialize(preview.Contacts),
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = actor
        }, ct);

        return Ok(segment);
    }

    [HttpGet("{segmentId}")]
    public async Task<IActionResult> GetSegment([FromRoute] string tenantId, [FromRoute] string segmentId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var segment = await _store.GetSegmentAsync(tenantId, segmentId, ct);
        return segment is null ? NotFound(new { message = "Segment not found." }) : Ok(segment);
    }

    [HttpPut("{segmentId}")]
    public async Task<IActionResult> UpdateSegment([FromRoute] string tenantId, [FromRoute] string segmentId, [FromBody] UpsertCampaignSegmentRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var existing = await _store.GetSegmentAsync(tenantId, segmentId, ct);
        if (existing is null) return NotFound(new { message = "Segment not found." });

        var preview = await _audienceService.PreviewAsync(tenantId, request.FilterJson ?? existing.FilterJson, null, ct);
        var actor = _tenantContext.Current!.UserId;
        var updated = await _store.UpsertSegmentAsync(existing with
        {
            Name = request.Name,
            Description = request.Description ?? existing.Description,
            SourceModules = request.SourceModules.Count == 0 ? existing.SourceModules : request.SourceModules,
            FilterJson = request.FilterJson ?? existing.FilterJson,
            EstimatedCount = preview.EstimatedCount,
            SamplePreviewJson = JsonSerializer.Serialize(preview.Contacts),
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = actor
        }, ct);

        return Ok(updated);
    }

    [HttpPost("preview")]
    public async Task<IActionResult> PreviewInline([FromRoute] string tenantId, [FromBody] PreviewCampaignSegmentRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _audienceService.PreviewAsync(tenantId, request.FilterJson ?? "{}", request.CampaignId, ct));
    }

    [HttpPost("{segmentId}/preview")]
    public async Task<IActionResult> PreviewSegment([FromRoute] string tenantId, [FromRoute] string segmentId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var segment = await _store.GetSegmentAsync(tenantId, segmentId, ct);
        if (segment is null) return NotFound(new { message = "Segment not found." });
        return Ok(await _audienceService.PreviewAsync(tenantId, segment.FilterJson, null, ct));
    }

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }
}

public sealed record UpsertCampaignSegmentRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IReadOnlyList<string> SourceModules { get; init; } = [];
    public string? FilterJson { get; init; }
}

public sealed record PreviewCampaignSegmentRequest
{
    public string? FilterJson { get; init; }
    public string? CampaignId { get; init; }
}
