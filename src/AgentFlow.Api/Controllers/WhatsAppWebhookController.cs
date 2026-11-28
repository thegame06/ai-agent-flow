using AgentFlow.Abstractions.Connect;
using AgentFlow.Abstractions.Workflow;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Security;
using AgentFlow.Api.Workflow;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Infrastructure.Channels.WhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AgentFlow.Api.Controllers;

/// <summary>
/// WhatsApp webhook endpoint for receiving incoming messages.
/// Supports both Meta Business API webhooks and custom QR-based gateway.
/// </summary>
[ApiController]
[Route("api/v1/tenants/{tenantId}/webhooks/whatsapp")]
public sealed class WhatsAppWebhookController : ControllerBase
{
    private readonly IChannelGateway _gateway;
    private readonly IChannelDefinitionRepository _channelRepo;
    private readonly IChannelMessageRepository _messageRepo;
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly IConnectStore _connectStore;
    private readonly IWorkflowTriggerService _triggerService;
    private readonly IWorkflowAuditService _audit;
    private readonly ILogger<WhatsAppWebhookController> _logger;
    private readonly WhatsAppOptions _waOptions;
    private readonly ICampaignOutcomeService? _campaignOutcomes;

    public WhatsAppWebhookController(
        IChannelGateway gateway,
        IChannelDefinitionRepository channelRepo,
        IChannelMessageRepository messageRepo,
        IChannelSessionRepository sessionRepo,
        IConnectStore connectStore,
        IWorkflowTriggerService triggerService,
        IWorkflowAuditService audit,
        IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppWebhookController> logger,
        ICampaignOutcomeService? campaignOutcomes = null)
    {
        _campaignOutcomes = campaignOutcomes;
        _gateway = gateway;
        _channelRepo = channelRepo;
        _messageRepo = messageRepo;
        _sessionRepo = sessionRepo;
        _connectStore = connectStore;
        _triggerService = triggerService;
        _audit = audit;
        _waOptions = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Meta Business API webhook verification (GET). Meta sends hub.mode, hub.verify_token and
    /// hub.challenge, and expects the challenge echoed back as plain text.
    /// </summary>
    [HttpGet]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var result = MetaWebhookVerification.Check(mode, verifyToken, challenge, _waOptions.WebhookVerifyToken);
        if (result is not null)
        {
            _logger.LogInformation("WhatsApp webhook verified successfully");
            return Content(result, "text/plain");
        }

        _logger.LogWarning("WhatsApp webhook verification failed: mode={Mode}", mode);
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Receive incoming WhatsApp messages from Meta Business API
    /// </summary>
    /// <summary>value.metadata.phone_number_id: the business number the customer wrote to.</summary>
    private static string? PhoneNumberIdOf(WhatsAppWebhookValue value)
        => value.Metadata is { ValueKind: System.Text.Json.JsonValueKind.Object } metadata &&
           metadata.TryGetProperty("phone_number_id", out var id)
            ? id.ValueKind == System.Text.Json.JsonValueKind.String ? id.GetString() : id.GetRawText()
            : null;

    private static string? DisplayPhoneOf(WhatsAppWebhookValue value)
        => value.Metadata is { ValueKind: System.Text.Json.JsonValueKind.Object } metadata &&
           metadata.TryGetProperty("display_phone_number", out var number) && number.ValueKind == System.Text.Json.JsonValueKind.String
            ? number.GetString()
            : null;

    /// <summary>What the customer wrote: the text, or the caption of the file they sent.</summary>
    private static string MetaText(WhatsAppIncomingMessage m)
        => m.Text?.Body
           ?? m.Image?.Caption ?? m.Document?.Caption ?? m.Video?.Caption
           ?? m.Caption
           ?? string.Empty;

    private static IReadOnlyList<AgentFlow.Api.Channels.InboundAttachment> MetaAttachments(WhatsAppIncomingMessage m)
    {
        var list = new List<AgentFlow.Api.Channels.InboundAttachment>();
        void Add(string kind, WhatsAppMedia? media)
        {
            if (media is null) return;
            list.Add(new(kind, null, media.MimeType, media.Id, media.Filename));
        }
        Add("image", m.Image);
        Add("document", m.Document);
        Add("audio", m.Audio);
        Add("video", m.Video);
        Add("sticker", m.Sticker);
        return list;
    }

    /// <summary>
    /// The active Business API channel of the number Meta delivered to. A number bound to no channel finds
    /// only a channel with no number set (single-number setups); never another number's channel, and never a
    /// QR channel, which does not receive Meta's webhooks.
    /// </summary>
    public static ChannelDefinition? ChannelForNumber(IReadOnlyList<ChannelDefinition> channels, string? phoneNumberId)
    {
        // One reading of a channel's mode everywhere (a channel without AuthMode is QR, as for sending).
        var business = channels
            .Where(c => c.Status == ChannelStatus.Active)
            .Where(c => AgentFlow.Api.Channels.Provenance.ProviderOf(c) == "meta")
            .ToList();
        if (!string.IsNullOrWhiteSpace(phoneNumberId))
        {
            var match = business.FirstOrDefault(c => c.Config.GetValueOrDefault("PhoneNumberId") == phoneNumberId);
            if (match is not null) return match;
        }
        return business.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.Config.GetValueOrDefault("PhoneNumberId")))
               ?? (string.IsNullOrWhiteSpace(phoneNumberId) ? business.FirstOrDefault() : null);
    }

    /// <summary>Verification of the webhook shared by every tenant (one Meta app for the platform).</summary>
    [HttpGet("/api/v1/webhooks/whatsapp")]
    public IActionResult VerifyPlatformWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
        => VerifyWebhook(mode, verifyToken, challenge);

    /// <summary>
    /// Webhook shared by every tenant (the platform's Meta app, signed with its app secret): each
    /// change goes to the tenant whose line has its number id (template events: whose WhatsApp
    /// Business account it is). A number no tenant, or more than one, claims is dropped, never guessed.
    /// </summary>
    [HttpPost("/api/v1/webhooks/whatsapp")]
    [RequireMetaSignature]
    public async Task<IActionResult> ReceivePlatformWebhook(
        [FromBody] WhatsAppWebhookPayload payload,
        [FromServices] AgentFlow.Api.Messaging.IWhatsAppTemplateService templates,
        [FromServices] AgentFlow.Api.Channels.IInboundMessageIngest ingest,
        [FromServices] AgentFlow.Api.Channels.ChannelLineService lines,
        CancellationToken ct)
    {
        var byTenant = new Dictionary<string, List<WhatsAppWebhookEntry>>(StringComparer.Ordinal);
        var dropped = 0;
        foreach (var entry in payload.Entry ?? [])
        {
            foreach (var change in entry.Changes ?? [])
            {
                var phoneNumberId = change.Value is null ? null : PhoneNumberIdOf(change.Value);
                var owner = phoneNumberId is not null
                    ? await lines.FindOwnerAsync(null, phoneNumberId, type => type == ChannelType.WhatsApp, ct)
                    : await lines.FindOwnerOfMetaAccountAsync(entry.Id, ct);
                if (owner is null)
                {
                    dropped++;
                    _logger.LogWarning("Shared WhatsApp webhook: no tenant owns number {PhoneNumberId} / account {AccountId}.", phoneNumberId, entry.Id);
                    continue;
                }
                if (!byTenant.TryGetValue(owner.TenantId, out var entries)) byTenant[owner.TenantId] = entries = [];
                entries.Add(new WhatsAppWebhookEntry { Id = entry.Id, Changes = [change] });
            }
        }

        foreach (var (tenantId, entries) in byTenant)
            await ReceiveWebhook(tenantId, new WhatsAppWebhookPayload { Object = payload.Object, Entry = entries }, templates, ingest, ct);
        return Ok(new { status = "routed", tenants = byTenant.Count, dropped });
    }

    [HttpPost]
    [RequireMetaSignature]
    public async Task<IActionResult> ReceiveWebhook(
        string tenantId,
        [FromBody] WhatsAppWebhookPayload payload,
        [FromServices] AgentFlow.Api.Messaging.IWhatsAppTemplateService templates,
        [FromServices] AgentFlow.Api.Channels.IInboundMessageIngest ingest,
        CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Received WhatsApp webhook: {Object}", System.Text.Json.JsonSerializer.Serialize(payload));

            // Template review results and quality/category changes come through the same webhook.
            var templateChanges = payload.Entry?
                .SelectMany(e => e.Changes ?? new List<WhatsAppWebhookChange>())
                .Where(c => c.Field is "message_template_status_update" or "message_template_quality_update" or "template_category_update" && c.Value is not null)
                .ToList() ?? [];
            foreach (var change in templateChanges)
            {
                var id = change.Value!.MessageTemplateId is { } raw
                    ? raw.ValueKind == System.Text.Json.JsonValueKind.Number ? raw.GetRawText() : raw.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(id)) continue;
                var applied = await templates.ApplyWebhookAsync(
                    tenantId, id!,
                    change.Field == "message_template_status_update" ? change.Value.Event : null,
                    change.Value.Reason,
                    change.Value.NewQualityScore,
                    change.Value.NewCategory,
                    ct);
                _logger.LogInformation("Template webhook {Field} for {TemplateId} ({Name}): {Event} applied={Applied}",
                    change.Field, id, change.Value.MessageTemplateName, change.Value.Event, applied);
            }
            if (templateChanges.Count > 0 && templateChanges.Count == payload.Entry!.Sum(e => e.Changes?.Count ?? 0))
                return Ok(new { status = "template_updates", count = templateChanges.Count });

            var changes = payload.Entry?
                .SelectMany(e => e.Changes ?? new List<WhatsAppWebhookChange>())
                .Where(c => c.Value?.Messages is { Count: > 0 })
                .ToList() ?? [];
            if (changes.Count == 0)
            {
                _logger.LogDebug("No messages in webhook payload");
                return Ok(new { status = "no_messages" });
            }

            // Each message goes to the channel of the number it was sent to: a company can have several
            // numbers (collections, sales, marketing), each with its own assistant.
            var channels = await _channelRepo.GetByTypeAsync(ChannelType.WhatsApp, tenantId, ct);
            var results = new List<object>();
            foreach (var change in changes)
            {
                var value = change.Value!;
                var phoneNumberId = PhoneNumberIdOf(value);
                var names = (value.Contacts ?? [])
                    .Where(c => !string.IsNullOrWhiteSpace(c.WaId))
                    .GroupBy(c => c.WaId!)
                    .ToDictionary(g => g.Key, g => g.First().Profile?.Name);
                foreach (var waMessage in value.Messages!)
                {
                    var activeChannel = ChannelForNumber(channels, phoneNumberId);
                    if (activeChannel == null)
                    {
                        _logger.LogWarning("No active WhatsApp channel for tenant {TenantId} and number {PhoneNumberId}", tenantId, phoneNumberId);
                        results.Add(new { from = waMessage.From, status = "no_channel" });
                        continue;
                    }
                    try
                    {
                        var result = await ingest.IngestAsync(new AgentFlow.Api.Channels.InboundMessage
                        {
                            TenantId = tenantId,
                            Channel = activeChannel,
                            Provider = "meta",
                            ExternalId = waMessage.Id,
                            From = waMessage.From,
                            DisplayName = names.GetValueOrDefault(waMessage.From) ?? waMessage.Profile?.Name,
                            Text = MetaText(waMessage),
                            Attachments = MetaAttachments(waMessage),
                            BusinessAddress = DisplayPhoneOf(value) ?? phoneNumberId,
                            SentAt = waMessage.Timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(waMessage.Timestamp) : null,
                            RawPayload = System.Text.Json.JsonSerializer.Serialize(waMessage)
                        }, ct: ct);
                        results.Add(new { from = waMessage.From, status = result.Status.ToString().ToLowerInvariant(), inboxMessageId = result.InboxMessageId });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process WhatsApp message from {From}", waMessage.From);
                        results.Add(new { from = waMessage.From, status = "error", error = ex.Message });
                    }
                }
            }

            return Ok(new { status = "success", processed = results.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp webhook");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Message from the QR bridge (whatsapp-web.js). Answers once the message is recorded and the
    /// assistant has replied through the bridge; a bridge retry after a timeout is a duplicate.
    /// </summary>
    [HttpPost("qr")]
    [RequireQrBridgeKey]
    public async Task<IActionResult> ReceiveQrMessage(
        string tenantId,
        [FromBody] QrWhatsAppMessage message,
        [FromServices] AgentFlow.Api.Channels.IInboundMessageIngest ingest,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Received QR WhatsApp message from {From}", message.From);
            var channels = await _channelRepo.GetByTypeAsync(ChannelType.WhatsApp, tenantId, ct);
            var activeChannel = QrChannelFor(channels, message.ChannelId);
            if (activeChannel == null)
                return BadRequest(new { error = "No active QR WhatsApp channel" });

            var attachments = message.Media is null
                ? Array.Empty<AgentFlow.Api.Channels.InboundAttachment>()
                : new[] { new AgentFlow.Api.Channels.InboundAttachment(message.Media.Type ?? "file", null, message.Media.MimeType, null, message.Media.FileName,
                    Data: QrMediaBytes(message.Media.Data)) };
            var result = await ingest.IngestAsync(new AgentFlow.Api.Channels.InboundMessage
            {
                TenantId = tenantId,
                Channel = activeChannel,
                Provider = "qr",
                ExternalId = string.IsNullOrWhiteSpace(message.Id) ? null : message.Id,
                From = message.From,
                DisplayName = message.PushName,
                Text = message.Content ?? string.Empty,
                Attachments = attachments,
                BusinessAddress = message.To,
                SentAt = message.Timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp) : null
            }, ct: ct);

            return result.Status switch
            {
                AgentFlow.Api.Channels.InboundStatus.Duplicate => Ok(new { status = "duplicate_accepted", inboxMessageId = result.InboxMessageId }),
                AgentFlow.Api.Channels.InboundStatus.Ignored => Ok(new { status = "ignored", reason = result.Reason }),
                _ => Ok(new
                {
                    status = result.Status == AgentFlow.Api.Channels.InboundStatus.Failed ? "failed" : "success",
                    inboxMessageId = result.InboxMessageId,
                    workflowExecutionId = (string?)null,
                    agentResponse = result.Reply?.Content,
                    agentExecutionId = result.Reply?.AgentExecutionId
                })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing QR WhatsApp message");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static byte[]? QrMediaBytes(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try { return Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }
    }

    /// <summary>The QR channel the bridge session belongs to; the only active one when the bridge does not say.</summary>
    public static ChannelDefinition? QrChannelFor(IReadOnlyList<ChannelDefinition> channels, string? channelId)
    {
        var qr = channels
            .Where(c => c.Status == ChannelStatus.Active)
            .Where(c => string.Equals(c.Config.GetValueOrDefault("AuthMode", "qr"), "qr", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return (string.IsNullOrWhiteSpace(channelId) ? null : qr.FirstOrDefault(c => c.Id == channelId)) ?? qr.FirstOrDefault();
    }

    /// <summary>
    /// Incoming WhatsApp message forwarded by Twilio (sandbox or a Twilio sender), signed with the
    /// tenant's Twilio auth token. Answers with empty TwiML: the reply goes out through Twilio's API.
    /// </summary>
    [HttpPost("twilio")]
    public async Task<IActionResult> ReceiveTwilioMessage(
        string tenantId,
        [FromForm] AgentFlow.Api.Messaging.TwilioWhatsAppForm form,
        [FromServices] AgentFlow.Api.Voice.ITwilioWebhookSignatureValidator signatures,
        [FromServices] AgentFlow.Api.Channels.IInboundMessageIngest ingest,
        CancellationToken requestAborted)
    {
        if (!await signatures.IsValidAsync(tenantId, Request, requestAborted))
            return StatusCode(StatusCodes.Status403Forbidden);
        if (string.IsNullOrWhiteSpace(form.From) || string.IsNullOrWhiteSpace(form.MessageSid))
            return EmptyTwiml();

        try
        {
            var channels = await _channelRepo.GetByTypeAsync(ChannelType.WhatsApp, tenantId, requestAborted);
            var channel = AgentFlow.Api.Messaging.TwilioWhatsApp.ChannelFor(channels, form.To);
            if (channel is null)
            {
                _logger.LogWarning("No active Twilio WhatsApp channel for tenant {TenantId} and number {To}", tenantId, form.To);
                return EmptyTwiml();
            }

            await ingest.IngestAsync(new AgentFlow.Api.Channels.InboundMessage
            {
                TenantId = tenantId,
                Channel = channel,
                Provider = "twilio",
                ExternalId = form.MessageSid,
                From = form.From,
                DisplayName = form.ProfileName,
                Text = form.Body ?? string.Empty,
                Attachments = TwilioAttachments(Request.HasFormContentType ? Request.Form : null),
                BusinessAddress = AgentFlow.Api.Messaging.TwilioWhatsApp.Phone(form.To)
            }, ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Twilio WhatsApp message {MessageSid}", form.MessageSid);
        }
        return EmptyTwiml();
    }

    /// <summary>MediaUrl0..N / MediaContentType0..N of a Twilio message.</summary>
    public static IReadOnlyList<AgentFlow.Api.Channels.InboundAttachment> TwilioAttachments(IFormCollection? form)
    {
        if (form is null || !int.TryParse(form["NumMedia"], out var count) || count <= 0) return [];
        var list = new List<AgentFlow.Api.Channels.InboundAttachment>();
        for (var i = 0; i < count && i < 10; i++)
        {
            var mime = form[$"MediaContentType{i}"].ToString();
            var kind = mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "image"
                : mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ? "audio"
                : mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? "video"
                : "document";
            list.Add(new(kind, form[$"MediaUrl{i}"].ToString(), string.IsNullOrWhiteSpace(mime) ? null : mime));
        }
        return list;
    }

    private ContentResult EmptyTwiml()
        => Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response></Response>", "text/xml");

}

// Webhook payload models
public sealed record WhatsAppWebhookPayload
{
    public string? Object { get; init; }
    public List<WhatsAppWebhookEntry>? Entry { get; init; }
}

public sealed record WhatsAppWebhookEntry
{
    public string? Id { get; init; }
    public List<WhatsAppWebhookChange>? Changes { get; init; }
}

public sealed record WhatsAppWebhookChange
{
    public string? Field { get; init; }
    public WhatsAppWebhookValue? Value { get; init; }
}

public sealed record WhatsAppWebhookValue
{
    [System.Text.Json.Serialization.JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; init; }
    /// <summary>{display_phone_number, phone_number_id}: an object, not a string.</summary>
    public System.Text.Json.JsonElement? Metadata { get; init; }
    /// <summary>The senders' WhatsApp profiles (name), keyed by wa_id.</summary>
    public List<WhatsAppWebhookContact>? Contacts { get; init; }
    public List<WhatsAppIncomingMessage>? Messages { get; init; }
    public List<WhatsAppWebhookStatus>? Statuses { get; init; }

    // message_template_status_update / message_template_quality_update / template_category_update
    public string? Event { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("message_template_id")]
    public System.Text.Json.JsonElement? MessageTemplateId { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("message_template_name")]
    public string? MessageTemplateName { get; init; }
    public string? Reason { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("new_quality_score")]
    public string? NewQualityScore { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("new_category")]
    public string? NewCategory { get; init; }
}

public sealed record WhatsAppWebhookContact
{
    [System.Text.Json.Serialization.JsonPropertyName("wa_id")]
    public string? WaId { get; init; }
    public WhatsAppProfile? Profile { get; init; }
}

public sealed record WhatsAppWebhookStatus
{
    public string? Id { get; init; }
    public string? Status { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("recipient_id")]
    public string? RecipientId { get; init; }
}

public sealed record QrWhatsAppMessage
{
    public string Id { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string? PushName { get; init; }
    public long Timestamp { get; init; }
    /// <summary>The AgentFlow channel of the bridge session that received it.</summary>
    public string? ChannelId { get; init; }
    /// <summary>The linked WhatsApp number that received it, when the bridge knows it.</summary>
    public string? To { get; init; }
    /// <summary>The file the customer sent, if any (the bridge does not upload it).</summary>
    public QrWhatsAppMedia? Media { get; init; }
}

public sealed record QrWhatsAppMedia
{
    public string? Type { get; init; }
    public string? MimeType { get; init; }
    public string? FileName { get; init; }
    /// <summary>The file itself, base64 (the bridge downloads it from WhatsApp).</summary>
    public string? Data { get; init; }
}

