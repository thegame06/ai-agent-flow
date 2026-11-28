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

    /// <summary>
    /// The active Business API channel of the number Meta delivered to. A number bound to no channel finds
    /// only a channel with no number set (single-number setups); never another number's channel, and never a
    /// QR channel, which does not receive Meta's webhooks.
    /// </summary>
    public static ChannelDefinition? ChannelForNumber(IReadOnlyList<ChannelDefinition> channels, string? phoneNumberId)
    {
        var business = channels
            .Where(c => c.Status == ChannelStatus.Active)
            .Where(c => !string.Equals(c.Config.GetValueOrDefault("AuthMode"), "qr", StringComparison.OrdinalIgnoreCase))
            .Where(c => !AgentFlow.Api.Messaging.TwilioWhatsApp.IsTwilio(c))
            .ToList();
        if (!string.IsNullOrWhiteSpace(phoneNumberId))
        {
            var match = business.FirstOrDefault(c => c.Config.GetValueOrDefault("PhoneNumberId") == phoneNumberId);
            if (match is not null) return match;
        }
        return business.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.Config.GetValueOrDefault("PhoneNumberId")))
               ?? (string.IsNullOrWhiteSpace(phoneNumberId) ? business.FirstOrDefault() : null);
    }

    [HttpPost]
    [RequireMetaSignature]
    public async Task<IActionResult> ReceiveWebhook(
        string tenantId,
        [FromBody] WhatsAppWebhookPayload payload,
        [FromServices] AgentFlow.Api.Messaging.IWhatsAppTemplateService templates,
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

            // Extract messages from payload
            var messages = payload.Entry?
                .SelectMany(e => e.Changes ?? new List<WhatsAppWebhookChange>())
                .SelectMany(c => c.Value?.Messages ?? new List<WhatsAppIncomingMessage>())
                .ToList();

            if (messages == null || messages.Count == 0)
            {
                _logger.LogDebug("No messages in webhook payload");
                return Ok(new { status = "no_messages" });
            }

            // Each message goes to the channel of the number it was sent to: a company can have several
            // numbers (collections, sales, marketing), each with its own assistant.
            var channels = await _channelRepo.GetByTypeAsync(ChannelType.WhatsApp, tenantId, ct);
            var byMessageId = payload.Entry!
                .SelectMany(e => e.Changes ?? new List<WhatsAppWebhookChange>())
                .SelectMany(c => (c.Value?.Messages ?? new List<WhatsAppIncomingMessage>()).Select(m => (m.Id, Phone: PhoneNumberIdOf(c.Value!))))
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First().Phone);

            // Process each message
            var results = new List<object>();
            foreach (var waMessage in messages)
            {
                var activeChannel = ChannelForNumber(channels, byMessageId.GetValueOrDefault(waMessage.Id));
                if (activeChannel == null)
                {
                    _logger.LogWarning("No active WhatsApp channel for tenant {TenantId} and number {PhoneNumberId}", tenantId, byMessageId.GetValueOrDefault(waMessage.Id));
                    results.Add(new { from = waMessage.From, status = "no_channel" });
                    continue;
                }
                try
                {
                    var externalEventKey = BuildExternalEventKey("wa-meta", waMessage);
                    if (!string.IsNullOrWhiteSpace(externalEventKey))
                    {
                        var existing = await _connectStore.GetInboxMessageByExternalKeyAsync(tenantId, externalEventKey, ct);
                        if (existing is not null)
                        {
                            results.Add(new
                            {
                                from = waMessage.From,
                                status = "duplicate_accepted",
                                inboxMessageId = existing.Id
                            });
                            continue;
                        }
                    }

                    var channelMessage = await ProcessWhatsAppMessage(waMessage, activeChannel, ct);
                    if (channelMessage != null)
                    {
                        var inboxMessage = await _connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            TenantId = tenantId,
                            Channel = "whatsapp",
                            Recipient = waMessage.From,
                            Content = waMessage.Text?.Body ?? waMessage.Caption ?? string.Empty,
                            ExternalEventKey = externalEventKey,
                            Status = ConnectOperationalStatus.Queued,
                            CreatedAt = DateTimeOffset.UtcNow,
                            UpdatedAt = DateTimeOffset.UtcNow,
                            UpdatedBy = "meta-webhook"
                        }, ct);

                        await AttributeCampaignReplyAsync(tenantId, waMessage.From, waMessage.Text?.Body ?? waMessage.Caption, HasAssistant(activeChannel), ct);

                        var response = await _gateway.ProcessMessageAsync(channelMessage, ct);
                        results.Add(new
                        {
                            from = waMessage.From,
                            status = "processed",
                            response_id = response.Id,
                            inboxMessageId = inboxMessage.Id
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process WhatsApp message from {From}", waMessage.From);
                    results.Add(new
                    {
                        from = waMessage.From,
                        status = "error",
                        error = ex.Message
                    });
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
    /// Receive message from QR-based WhatsApp gateway (OpenClaw-style).
    /// Dispatches to the agent via ChannelGateway AND triggers any published
    /// workflow listening on the "connect.message.received" event.
    /// </summary>
    [HttpPost("qr")]
    [RequireQrBridgeKey]
    public async Task<IActionResult> ReceiveQrMessage(string tenantId, [FromBody] QrWhatsAppMessage message, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Received QR WhatsApp message from {From}", message.From);
            var externalEventKey = BuildExternalEventKey("wa-qr", message.Id, message.From, message.Timestamp, message.Content);
            if (!string.IsNullOrWhiteSpace(externalEventKey))
            {
                var existing = await _connectStore.GetInboxMessageByExternalKeyAsync(tenantId, externalEventKey, ct);
                if (existing is not null)
                {
                    return Ok(new
                    {
                        status = "duplicate_accepted",
                        inboxMessageId = existing.Id
                    });
                }
            }

            var channels = await _channelRepo.GetByTypeAsync(ChannelType.WhatsApp, tenantId, ct);
            var activeChannel = channels.FirstOrDefault(c => c.Status == ChannelStatus.Active && c.Config.GetValueOrDefault("AuthMode") == "qr");

            if (activeChannel == null)
                return BadRequest(new { error = "No active QR WhatsApp channel" });

            var waMessage = new WhatsAppIncomingMessage
            {
                Id = message.Id,
                From = message.From,
                Timestamp = message.Timestamp,
                Text = new WhatsAppTextMessage { Body = message.Content },
                Profile = new WhatsAppProfile { Name = message.PushName ?? "Unknown" }
            };
            var channelMessage = await ProcessWhatsAppMessage(waMessage, activeChannel, ct);
            if (channelMessage is null)
                return BadRequest(new { error = "Could not normalize incoming WhatsApp message." });

            var session = await _sessionRepo.GetByIdAsync(channelMessage.SessionId, tenantId, ct);
            if (session is null)
                return BadRequest(new { error = "Could not resolve WhatsApp session." });

            // --- Store inbox message for traceability ---
            var inboxMessage = await _connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                Channel = "whatsapp",
                Recipient = message.From,
                Content = message.Content,
                ExternalEventKey = externalEventKey,
                Status = ConnectOperationalStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = "qr-webhook"
            }, ct);

            await AttributeCampaignReplyAsync(tenantId, message.From, message.Content, HasAssistant(activeChannel), ct);

            // --- Route through channel gateway as primary path ---
            // Router + intent rules decide whether to trigger a workflow.
            var response = await _gateway.ProcessMessageAsync(channelMessage, ct);
            var agentResponse = response.Content;
            var agentExecutionId = response.AgentExecutionId;

            return Ok(new
            {
                status = "success",
                inboxMessageId = inboxMessage.Id,
                workflowExecutionId = (string?)null,
                agentResponse,
                agentExecutionId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing QR WhatsApp message");
            return StatusCode(500, new { error = ex.Message });
        }
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
        CancellationToken requestAborted)
    {
        if (!await signatures.IsValidAsync(tenantId, Request, requestAborted))
            return StatusCode(StatusCodes.Status403Forbidden);

        var from = AgentFlow.Api.Messaging.TwilioWhatsApp.Phone(form.From);
        var body = form.Body?.Trim();
        if (string.IsNullOrEmpty(body) && int.TryParse(form.NumMedia, out var media) && media > 0)
            body = "[archivo adjunto]";
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(form.MessageSid) || string.IsNullOrEmpty(body))
            return EmptyTwiml();

        // Twilio stops waiting after 15 s, but the reply goes out through its API: a slow assistant
        // must not be cancelled together with the request.
        var ct = CancellationToken.None;
        try
        {
            var channels = await _channelRepo.GetByTypeAsync(ChannelType.WhatsApp, tenantId, ct);
            var channel = AgentFlow.Api.Messaging.TwilioWhatsApp.ChannelFor(channels, form.To);
            if (channel is null)
            {
                _logger.LogWarning("No active Twilio WhatsApp channel for tenant {TenantId} and number {To}", tenantId, form.To);
                return EmptyTwiml();
            }

            var externalEventKey = $"wa-twilio:{form.MessageSid}";
            if (await _connectStore.GetInboxMessageByExternalKeyAsync(tenantId, externalEventKey, ct) is not null)
                return EmptyTwiml();

            var waMessage = new WhatsAppIncomingMessage
            {
                Id = form.MessageSid!,
                From = from!,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Text = new WhatsAppTextMessage { Body = body! },
                Profile = new WhatsAppProfile { Name = string.IsNullOrWhiteSpace(form.ProfileName) ? "Unknown" : form.ProfileName }
            };
            var channelMessage = await ProcessWhatsAppMessage(waMessage, channel, ct);
            if (channelMessage is null)
                return EmptyTwiml();

            await _connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                Channel = "whatsapp",
                Recipient = from!,
                Content = body!,
                ExternalEventKey = externalEventKey,
                Status = ConnectOperationalStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = "twilio-webhook"
            }, ct);

            await AttributeCampaignReplyAsync(tenantId, from, body, HasAssistant(channel), ct);
            await _gateway.ProcessMessageAsync(channelMessage, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Twilio WhatsApp message {MessageSid}", form.MessageSid);
        }
        return EmptyTwiml();
    }

    private ContentResult EmptyTwiml()
        => Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response></Response>", "text/xml");

    /// <summary>
    /// Links a reply to the campaign contact that messaged this number and classifies it
    /// (promesa de pago, ya pague, no me interesa...). Never blocks the conversation flow.
    /// </summary>
    private static bool HasAssistant(ChannelDefinition channel) =>
        !string.IsNullOrWhiteSpace(channel.RouterAgentId) || !string.IsNullOrWhiteSpace(channel.Config.GetValueOrDefault("DefaultAgentId"));

    private async Task AttributeCampaignReplyAsync(string tenantId, string? from, string? text, bool answeredByAssistant, CancellationToken ct)
    {
        if (_campaignOutcomes is null || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var contact = await _campaignOutcomes.AttributeReplyAsync(tenantId, from, text, DateTimeOffset.UtcNow, ct, answeredByAssistant);
            if (contact is not null)
                _logger.LogInformation("Reply attributed to campaign {CampaignId} contact {ContactId}: {Outcome}", contact.CampaignId, contact.Id, contact.Outcome);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not attribute WhatsApp reply to a campaign contact");
        }
    }

    private async Task<ChannelMessage?> ProcessWhatsAppMessage(WhatsAppIncomingMessage waMessage, ChannelDefinition channel, CancellationToken ct)
    {
        var handler = _gateway.GetHandler(ChannelType.WhatsApp) as WhatsAppChannelHandler;
        if (handler == null) return null;

        return await handler.ProcessIncomingMessageAsync(waMessage, channel, ct);
    }

    private static string? BuildExternalEventKey(string prefix, WhatsAppIncomingMessage message)
    {
        var content = message.Text?.Body ?? message.Caption ?? string.Empty;
        return BuildExternalEventKey(prefix, message.Id, message.From, message.Timestamp, content);
    }

    private static string? BuildExternalEventKey(string prefix, string? nativeId, string? from, long? timestamp, string? content)
    {
        if (!string.IsNullOrWhiteSpace(nativeId))
            return $"{prefix}:{nativeId}";

        if (string.IsNullOrWhiteSpace(from) && timestamp is null && string.IsNullOrWhiteSpace(content))
            return null;

        return $"{prefix}:{from}:{timestamp}:{content}".Trim();
    }
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
}

