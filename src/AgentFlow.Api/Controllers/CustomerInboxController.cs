using AgentFlow.Api.Channels;
using AgentFlow.Api.Entitlements;
using AgentFlow.Extensions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

/// <summary>The inbox by customer: one row per person across every channel, and their merged timeline.</summary>
[ApiController]
[RequiresAppModule(AppModules.Conversations)]
[Authorize]
[Route("api/v1/tenants/{tenantId}/customer-inbox")]
public sealed class CustomerInboxController : ControllerBase
{
    private readonly ICustomerInbox _inbox;
    private readonly ITenantContextAccessor _tenantContext;

    public CustomerInboxController(ICustomerInbox inbox, ITenantContextAccessor tenantContext)
    {
        _inbox = inbox;
        _tenantContext = tenantContext;
    }

    /// <param name="provider">qr, meta, twilio, api, webchat…</param>
    /// <param name="attention">Only customers waiting for a person or a reply.</param>
    /// <param name="includeDeleted">Also customers and channels seen only on deleted (test) channels.</param>
    [HttpGet("customers")]
    public async Task<IActionResult> Customers(
        [FromRoute] string tenantId,
        [FromQuery] string? query = null,
        [FromQuery] string? channelId = null,
        [FromQuery] string? provider = null,
        [FromQuery] bool attention = false,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!CanRead(tenantId)) return Forbid();
        var (items, total) = await _inbox.ListAsync(tenantId, new AgentFlow.Api.Channels.InboxFilter(query, channelId, provider, attention, includeDeleted), page, pageSize, ct);
        return Ok(new { items, total, page = Math.Max(0, page), pageSize = Math.Clamp(pageSize, 1, 100) });
    }

    [HttpGet("customers/{customerKey}/timeline")]
    public async Task<IActionResult> Timeline([FromRoute] string tenantId, [FromRoute] string customerKey, [FromQuery] int limit = 200, [FromQuery] bool includeDeleted = false, CancellationToken ct = default)
    {
        if (!CanRead(tenantId)) return Forbid();
        return Ok(await _inbox.TimelineAsync(tenantId, customerKey, limit, includeDeleted, ct));
    }

    private bool CanRead(string tenantId)
    {
        var context = _tenantContext.Current;
        return context is not null && (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(AgentFlowPermissions.ConnectRead) || context.IsPlatformAdmin);
    }
}
