using AgentFlow.Api.Settings;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/settings/agent-contexts")]
[Authorize]
public sealed class AgentContextSettingsController : ControllerBase
{
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ITenantAgentContextService _service;

    public AgentContextSettingsController(ITenantContextAccessor tenantContext, ITenantAgentContextService service)
    {
        _tenantContext = tenantContext;
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();
        return Ok(await _service.GetAsync(tenantId, context.UserId, ct));
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromRoute] string tenantId, [FromBody] TenantAgentContextSettingsDto request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();
        return Ok(await _service.SaveAsync(tenantId, request, context.UserId, ct));
    }
}
