using AgentFlow.Api.Connect;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

/// <summary>
/// The Meta (WhatsApp Business) account of a WhatsApp channel. Channels own the account: the
/// channel settings and the templates page edit it through these endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/tenants/{tenantId}/channels")]
public sealed class WhatsAppChannelMetaController : ControllerBase
{
    private readonly IWhatsAppChannelMetaService _meta;
    private readonly ITenantContextAccessor _tenantContext;

    public WhatsAppChannelMetaController(IWhatsAppChannelMetaService meta, ITenantContextAccessor tenantContext)
    {
        _meta = meta;
        _tenantContext = tenantContext;
    }

    /// <summary>Every WhatsApp channel on the Business API, with its Meta account (no secrets).</summary>
    [HttpGet("whatsapp/meta")]
    public async Task<IActionResult> List([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _meta.ListAsync(tenantId, ct));
    }

    [HttpGet("{channelId}/meta")]
    public async Task<IActionResult> Get([FromRoute] string tenantId, [FromRoute] string channelId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var view = await _meta.GetAsync(tenantId, channelId, ct);
        return view is null ? NotFound() : Ok(view);
    }

    [HttpPut("{channelId}/meta")]
    public async Task<IActionResult> Save([FromRoute] string tenantId, [FromRoute] string channelId, [FromBody] WhatsAppChannelMetaRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var (view, error) = await _meta.SaveAsync(tenantId, channelId, request, _tenantContext.Current!.UserId, ct);
        return view is null ? UnprocessableEntity(new { message = error }) : Ok(view);
    }

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }
}
