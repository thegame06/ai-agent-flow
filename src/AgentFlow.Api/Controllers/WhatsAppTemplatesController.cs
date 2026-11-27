using AgentFlow.Api.Connect;
using AgentFlow.Api.Messaging;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

/// <summary>
/// WhatsApp message templates of the tenant's Meta accounts: list, sync from Meta, create, edit,
/// delete, map placeholders to campaign fields, preview and version history.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/tenants/{tenantId}/whatsapp/templates")]
public sealed class WhatsAppTemplatesController : ControllerBase
{
    private readonly IWhatsAppTemplateStore _store;
    private readonly IWhatsAppTemplateService _templates;
    private readonly IWhatsAppChannelMetaService _channels;
    private readonly ITenantContextAccessor _tenantContext;

    public WhatsAppTemplatesController(
        IWhatsAppTemplateStore store,
        IWhatsAppTemplateService templates,
        IWhatsAppChannelMetaService channels,
        ITenantContextAccessor tenantContext)
    {
        _store = store;
        _templates = templates;
        _channels = channels;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Templates, with the WhatsApp channels that can send them. With <paramref name="channelId"/>
    /// only the templates of that channel's Meta account (a company may have one account per area).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromRoute] string tenantId, [FromQuery] string? channelId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        // Channel views carry ids and flags only: tokens and secrets never leave the server.
        var channels = await _channels.ListAsync(tenantId, ct);
        var items = await _store.ListAsync(tenantId, ct);
        if (!string.IsNullOrWhiteSpace(channelId))
        {
            var waba = channels.FirstOrDefault(c => c.ChannelId == channelId)?.BusinessAccountId;
            items = items.Where(t => waba is not null && t.BusinessAccountId == waba).ToList();
        }
        return Ok(new { items, channels, fields = WhatsAppTemplateText.Fields });
    }

    [HttpGet("{templateId}")]
    public async Task<IActionResult> Get([FromRoute] string tenantId, [FromRoute] string templateId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var template = await _store.GetAsync(tenantId, templateId, ct);
        if (template is null) return NotFound();

        // Preview with the sample values, as the customer would read it.
        var samples = template.Parameters
            .Where(p => p.Source == "field" && p.Field is not null)
            .GroupBy(p => p.Field!)
            .ToDictionary(g => g.Key, g => g.First().Example ?? WhatsAppTemplateText.Fields.FirstOrDefault(f => f.Key == g.Key)?.Example ?? g.Key);
        var preview = WhatsAppTemplateText.Build(template, samples);
        return Ok(new
        {
            template,
            versions = await _store.GetVersionsAsync(tenantId, templateId, ct),
            preview = preview.Text,
            previewMissing = preview.Missing
        });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromRoute] string tenantId, [FromQuery] string? channelId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        return Ok(await _templates.SyncAsync(tenantId, channelId, _tenantContext.Current!.UserId, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromRoute] string tenantId, [FromBody] TemplateDraft draft, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        return Result(await _templates.CreateAsync(tenantId, draft, _tenantContext.Current!.UserId, ct));
    }

    [HttpPut("{templateId}")]
    public async Task<IActionResult> Edit([FromRoute] string tenantId, [FromRoute] string templateId, [FromBody] TemplateDraft draft, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        return Result(await _templates.EditAsync(tenantId, templateId, draft, _tenantContext.Current!.UserId, ct));
    }

    [HttpPut("{templateId}/parameters")]
    public async Task<IActionResult> UpdateParameters([FromRoute] string tenantId, [FromRoute] string templateId, [FromBody] List<WhatsAppTemplateParameter> parameters, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        return Result(await _templates.UpdateParametersAsync(tenantId, templateId, parameters ?? [], _tenantContext.Current!.UserId, ct));
    }

    [HttpDelete("{templateId}")]
    public async Task<IActionResult> Delete([FromRoute] string tenantId, [FromRoute] string templateId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        return Result(await _templates.DeleteAsync(tenantId, templateId, _tenantContext.Current!.UserId, ct));
    }

    private IActionResult Result(TemplateOperationResult result)
        => result.Ok ? Ok(result.Template) : UnprocessableEntity(new { message = result.Error });

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }
}
