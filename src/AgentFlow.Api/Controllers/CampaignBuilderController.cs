using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tenants/{tenantId}/campaign-builder")]
public sealed class CampaignBuilderController : ControllerBase
{
    private readonly ICampaignBuilderService _builder;
    private readonly ITenantContextAccessor _tenantContext;

    public CampaignBuilderController(ICampaignBuilderService builder, ITenantContextAccessor tenantContext)
    {
        _builder = builder;
        _tenantContext = tenantContext;
    }

    [HttpPost("draft-from-prompt")]
    public async Task<IActionResult> DraftFromPrompt([FromRoute] string tenantId, [FromBody] CampaignPromptRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { message = "Prompt is required." });

        return Ok(await _builder.DraftFromPromptAsync(tenantId, request.Prompt, _tenantContext.Current!.UserId, ct));
    }

    [HttpPost("refine")]
    public async Task<IActionResult> Refine([FromRoute] string tenantId, [FromBody] RefineCampaignDraftRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        if (request.Current is null)
            return BadRequest(new { message = "Current draft is required." });
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { message = "Prompt is required." });

        return Ok(await _builder.RefineAsync(tenantId, request.Current, request.Prompt, _tenantContext.Current!.UserId, ct));
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromRoute] string tenantId, [FromBody] ValidateCampaignDraftRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        if (request.Draft is null)
            return BadRequest(new { message = "Draft is required." });

        var warnings = await _builder.ValidateAsync(tenantId, request.Draft, ct);
        return Ok(new { valid = warnings.Count == 0, warnings });
    }

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }
}

public sealed record CampaignPromptRequest
{
    public string Prompt { get; init; } = string.Empty;
}

public sealed record RefineCampaignDraftRequest
{
    public CampaignBuilderDraftContract? Current { get; init; }
    public string Prompt { get; init; } = string.Empty;
}

public sealed record ValidateCampaignDraftRequest
{
    public CampaignBuilderDraftContract? Draft { get; init; }
}
