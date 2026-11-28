using AgentFlow.Api.Connect;
using AgentFlow.Api.Messaging;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

/// <summary>
/// WhatsApp message templates of the tenant's Meta accounts and Twilio accounts: list, sync, create,
/// edit (Meta), delete, map placeholders to campaign fields, preview and version history.
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
    private readonly ITwilioTemplateService _twilio;
    private readonly IChannelDefinitionRepository _channelDefinitions;

    public WhatsAppTemplatesController(
        IWhatsAppTemplateStore store,
        IWhatsAppTemplateService templates,
        IWhatsAppChannelMetaService channels,
        ITenantContextAccessor tenantContext,
        ITwilioTemplateService twilio,
        IChannelDefinitionRepository channelDefinitions)
    {
        _store = store;
        _templates = templates;
        _channels = channels;
        _tenantContext = tenantContext;
        _twilio = twilio;
        _channelDefinitions = channelDefinitions;
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
        var channels = (await _channels.ListAsync(tenantId, ct)).Concat(await TwilioChannelsAsync(tenantId, ct)).ToList();
        var items = await _store.ListAsync(tenantId, ct);
        if (!string.IsNullOrWhiteSpace(channelId))
        {
            var channel = channels.FirstOrDefault(c => c.ChannelId == channelId);
            items = channel?.Provider == WhatsAppTemplateProviders.Twilio
                // Twilio templates belong to the channel's Twilio connection.
                ? items.Where(t => t.Provider == WhatsAppTemplateProviders.Twilio && (t.ConnectionId ?? string.Empty) == (channel.ConnectionId ?? string.Empty)).ToList()
                : items.Where(t => t.Provider != WhatsAppTemplateProviders.Twilio && channel?.BusinessAccountId is { } waba && t.BusinessAccountId == waba).ToList();
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
        var user = _tenantContext.Current!.UserId;
        if (!string.IsNullOrWhiteSpace(channelId))
            return Ok(await TwilioChannelAsync(tenantId, channelId, ct) is not null
                ? await _twilio.SyncAsync(tenantId, channelId, user, ct)
                : await _templates.SyncAsync(tenantId, channelId, user, ct));

        // Every account: Meta's and Twilio's. Not having one of the two is not an error.
        var twilio = await _twilio.SyncAsync(tenantId, null, user, ct);
        var meta = await _templates.SyncAsync(tenantId, null, user, ct);
        var metaErrors = twilio.Accounts > 0 || twilio.Errors.Count > 0
            ? meta.Errors.Where(e => e != WhatsAppTemplateService.NoMetaAccount).ToList()
            : meta.Errors.ToList();
        return Ok(new TemplateSyncResult
        {
            Accounts = meta.Accounts + twilio.Accounts,
            Created = meta.Created + twilio.Created,
            Updated = meta.Updated + twilio.Updated,
            StatusChanged = meta.StatusChanged + twilio.StatusChanged,
            Unchanged = meta.Unchanged + twilio.Unchanged,
            Removed = meta.Removed + twilio.Removed,
            Errors = metaErrors.Concat(twilio.Errors).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromRoute] string tenantId, [FromBody] TemplateDraft draft, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var user = _tenantContext.Current!.UserId;
        if (!string.IsNullOrWhiteSpace(draft.ChannelId) && await TwilioChannelAsync(tenantId, draft.ChannelId!, ct) is { } twilioChannel)
            return Result(await _twilio.CreateAsync(tenantId, twilioChannel, draft, user, ct));
        return Result(await _templates.CreateAsync(tenantId, draft, user, ct));
    }

    [HttpPut("{templateId}")]
    public async Task<IActionResult> Edit([FromRoute] string tenantId, [FromRoute] string templateId, [FromBody] TemplateDraft draft, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        if ((await _store.GetAsync(tenantId, templateId, ct))?.Provider == WhatsAppTemplateProviders.Twilio)
            return UnprocessableEntity(new { message = "Twilio no permite editar una plantilla ya enviada a revision de WhatsApp: crea una nueva con el texto corregido." });
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
        if (await _store.GetAsync(tenantId, templateId, ct) is { Provider: WhatsAppTemplateProviders.Twilio } twilioTemplate)
            return Result(await _twilio.DeleteAsync(tenantId, twilioTemplate, _tenantContext.Current!.UserId, ct));
        return Result(await _templates.DeleteAsync(tenantId, templateId, _tenantContext.Current!.UserId, ct));
    }

    private async Task<ChannelDefinition?> TwilioChannelAsync(string tenantId, string channelId, CancellationToken ct)
        => await _channelDefinitions.GetByIdAsync(channelId, tenantId, ct) is { } channel && TwilioWhatsApp.IsTwilio(channel) ? channel : null;

    /// <summary>The tenant's Twilio WhatsApp channels, shown next to the Meta ones (no secrets).</summary>
    private async Task<IEnumerable<WhatsAppChannelMetaView>> TwilioChannelsAsync(string tenantId, CancellationToken ct)
        => (await _channelDefinitions.GetByTypeAsync(ChannelType.WhatsApp, tenantId, ct))
            .Where(TwilioWhatsApp.IsTwilio)
            .Select(c => new WhatsAppChannelMetaView
            {
                ChannelId = c.Id,
                ChannelName = c.Name,
                Status = c.Status.ToString(),
                Provider = WhatsAppTemplateProviders.Twilio,
                ConnectionId = TwilioTemplateService.ConnectionOf(c)
            });

    private IActionResult Result(TemplateOperationResult result)
        => result.Ok ? Ok(result.Template) : UnprocessableEntity(new { message = result.Error });

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }
}
