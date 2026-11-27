using AgentFlow.Api.Connect;
using AgentFlow.Api.Entitlements;
using AgentFlow.Extensions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

/// <summary>
/// Hand-off between the assistant and people: the queue of conversations waiting for an advisor,
/// taking a conversation (the assistant goes quiet), answering it and giving it back.
/// </summary>
[ApiController]
[RequiresAppModule(AppModules.Conversations)]
[Authorize]
[Route("api/v1/tenants/{tenantId}/conversations")]
public sealed class ConversationsController : ControllerBase
{
    private readonly IConversationHandoffService _handoff;
    private readonly ITenantContextAccessor _tenantContext;

    public ConversationsController(IConversationHandoffService handoff, ITenantContextAccessor tenantContext)
    {
        _handoff = handoff;
        _tenantContext = tenantContext;
    }

    [HttpGet("attention")]
    public async Task<IActionResult> Queue([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _handoff.QueueAsync(tenantId, ct));
    }

    [HttpGet("{sessionId}")]
    public async Task<IActionResult> Get([FromRoute] string tenantId, [FromRoute] string sessionId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var view = await _handoff.GetAsync(tenantId, sessionId, ct);
        return view is null ? NotFound(new { message = "Conversacion no encontrada." }) : Ok(view);
    }

    [HttpPost("{sessionId}/take")]
    public async Task<IActionResult> Take([FromRoute] string tenantId, [FromRoute] string sessionId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();
        return await _handoff.TakeAsync(tenantId, sessionId, _tenantContext.Current!.UserId, ct) ? Ok(new { sessionId, state = "human" }) : NotFound();
    }

    [HttpPost("{sessionId}/release")]
    public async Task<IActionResult> Release([FromRoute] string tenantId, [FromRoute] string sessionId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();
        return await _handoff.ReleaseAsync(tenantId, sessionId, ct) ? Ok(new { sessionId, state = "assistant" }) : NotFound();
    }

    [HttpPost("{sessionId}/reply")]
    public async Task<IActionResult> Reply([FromRoute] string tenantId, [FromRoute] string sessionId, [FromBody] HandoffReplyRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();
        var result = await _handoff.ReplyAsync(tenantId, sessionId, _tenantContext.Current!.UserId, request.Content ?? string.Empty, ct);
        return result.Sent ? Ok(result) : UnprocessableEntity(result);
    }

    /// <summary>Write to a flagged campaign contact that has no live conversation yet.</summary>
    [HttpPost("contacts/{contactId}/reply")]
    public async Task<IActionResult> ReplyToContact([FromRoute] string tenantId, [FromRoute] string contactId, [FromBody] HandoffReplyRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();
        var result = await _handoff.ReplyToContactAsync(tenantId, contactId, _tenantContext.Current!.UserId, request.Content ?? string.Empty, ct);
        return result.Sent ? Ok(result) : UnprocessableEntity(result);
    }

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }
}

public sealed record HandoffReplyRequest
{
    public string? Content { get; init; }
}
