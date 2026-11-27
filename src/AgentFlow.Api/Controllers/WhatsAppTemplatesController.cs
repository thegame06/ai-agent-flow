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
    private readonly IMetaWhatsAppAccounts _accounts;
    private readonly ITenantContextAccessor _tenantContext;

    public WhatsAppTemplatesController(
        IWhatsAppTemplateStore store,
        IWhatsAppTemplateService templates,
        IMetaWhatsAppAccounts accounts,
        ITenantContextAccessor tenantContext)
    {
        _store = store;
        _templates = templates;
        _accounts = accounts;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var accounts = await _accounts.GetAllAsync(tenantId, ct);
        return Ok(new
        {
            items = await _store.ListAsync(tenantId, ct),
            // Ids only: tokens and secrets never leave the server.
            accounts = accounts.Select(a => new
            {
                a.ConnectionId,
                a.Name,
                a.BusinessAccountId,
                a.PhoneNumberId,
                hasToken = !string.IsNullOrWhiteSpace(a.AccessToken),
                hasAppSecret = !string.IsNullOrWhiteSpace(a.AppSecret)
            }),
            fields = WhatsAppTemplateText.Fields
        });
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
    public async Task<IActionResult> Sync([FromRoute] string tenantId, [FromQuery] string? connectionId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        return Ok(await _templates.SyncAsync(tenantId, connectionId, _tenantContext.Current!.UserId, ct));
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
