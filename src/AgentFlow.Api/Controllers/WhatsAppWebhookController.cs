using AgentFlow.Abstractions.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Infrastructure.Channels.WhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<WhatsAppWebhookController> _logger;
    private readonly WhatsAppOptions _waOptions;

    public WhatsAppWebhookController(
        IChannelGateway gateway,
        IChannelDefinitionRepository channelRepo,
        IChannelMessageRepository messageRepo,
        IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppWebhookController> logger)
    {
        _gateway = gateway;
        _channelRepo = channelRepo;
        _messageRepo = messageRepo;
        _waOptions = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Meta Business API webhook verification (GET)
    /// </summary>
    [HttpGet]
    public IActionResult VerifyWebhook([FromQuery] string? mode, [FromQuery] string? verify_token, [FromQuery] string? challenge)
    {
        if (mode == "subscribe" && verify_token == _waOptions.WebhookVerifyToken)
        {
            _logger.LogInformation("WhatsApp webhook verified successfully");
            return Challenge(string.IsNullOrEmpty(challenge) ? "verified" : challenge);
        }

        _logger.LogWarning("WhatsApp webhook verification failed: mode={Mode}, token_match={TokenMatch}",
            mode, verify_token == _waOptions.WebhookVerifyToken);
        return Forbid();
    }

    /// <summary>
    /// Receive incoming WhatsApp messages from Meta Business API
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ReceiveWebhook(string tenantId, [FromBody] WhatsAppWebhookPayload payload, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Received WhatsApp webhook: {Object}", System.Text.Json.JsonSerializer.Serialize(payload));

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

            // Find WhatsApp channel for this tenant
            var channels = await _channelRepo.GetByTypeAsync(ChannelType.WhatsApp, tenantId, ct);
            var activeChannel = channels.FirstOrDefault(c => c.Status == ChannelStatus.Active);

            if (activeChannel == null)
            {
                _logger.LogWarning("No active WhatsApp channel for tenant {TenantId}", tenantId);
                return BadRequest(new { error = "No active WhatsApp channel configured" });
            }

            // Process each message
            var results = new List<object>();
            foreach (var waMessage in messages)
            {
                try
                {
                    var channelMessage = await ProcessWhatsAppMessage(waMessage, activeChannel, ct);
                    if (channelMessage != null)
                    {
                        var response = await _gateway.ProcessMessageAsync(channelMessage, ct);
                        results.Add(new
                        {
                            from = waMessage.From,
                            status = "processed",
                            response_id = response.Id
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
    /// Receive message from QR-based WhatsApp gateway (OpenClaw-style)
    /// </summary>
    [HttpPost("qr")]
    public async Task<IActionResult> ReceiveQrMessage(string tenantId, [FromBody] QrWhatsAppMessage message, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Received QR WhatsApp message from {From}", message.From);

            var channels = await _channelRepo.GetByTypeAsync(ChannelType.WhatsApp, tenantId, ct);
            var activeChannel = channels.FirstOrDefault(c => c.Status == ChannelStatus.Active && c.Config.GetValueOrDefault("AuthMode") == "qr");

            if (activeChannel == null)
            {
                return BadRequest(new { error = "No active QR WhatsApp channel" });
            }

            var waMessage = new WhatsAppIncomingMessage
            {
                Id = message.Id,
                From = message.From,
                Timestamp = message.Timestamp,
                Text = new WhatsAppTextMessage { Body = message.Content },
                Profile = new WhatsAppProfile { Name = message.PushName ?? "Unknown" }
            };

            var channelMessage = await ProcessWhatsAppMessage(waMessage, activeChannel, ct);
            if (channelMessage == null)
                return BadRequest(new { error = "Invalid message" });

            var response = await _gateway.ProcessMessageAsync(channelMessage, ct);

            return Ok(new
            {
                status = "success",
                response = response.Content,
                execution_id = response.AgentExecutionId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing QR WhatsApp message");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private async Task<ChannelMessage?> ProcessWhatsAppMessage(WhatsAppIncomingMessage waMessage, ChannelDefinition channel, CancellationToken ct)
    {
        var handler = _gateway.GetHandler(ChannelType.WhatsApp) as WhatsAppChannelHandler;
        if (handler == null) return null;

        return await handler.ProcessIncomingMessageAsync(waMessage, channel, ct);
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
    public string? MessagingProduct { get; init; }
    public string? Metadata { get; init; }
    public List<WhatsAppIncomingMessage>? Messages { get; init; }
    public List<WhatsAppWebhookStatus>? Statuses { get; init; }
}

public sealed record WhatsAppWebhookStatus
{
    public string? Id { get; init; }
    public string? Status { get; init; }
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
