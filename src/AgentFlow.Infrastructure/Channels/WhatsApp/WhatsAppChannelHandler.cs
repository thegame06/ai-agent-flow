using AgentFlow.Abstractions.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentFlow.Infrastructure.Channels.WhatsApp;

/// <summary>
/// WhatsApp channel handler supporting QR authentication (initial) and Business API.
/// </summary>
public sealed class WhatsAppChannelHandler : IChannelHandler
{
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly ILogger<WhatsAppChannelHandler> _logger;
    private readonly WhatsAppClient _whatsappClient;

    public ChannelType SupportedChannelType => ChannelType.WhatsApp;

    public WhatsAppChannelHandler(
        IChannelSessionRepository sessionRepo,
        IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppChannelHandler> logger)
    {
        _sessionRepo = sessionRepo;
        _logger = logger;
        _whatsappClient = new WhatsAppClient(options.Value, logger);
    }

    public async Task<ChannelStatus> InitializeAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Initializing WhatsApp channel {ChannelId} ({Name})", definition.Id, definition.Name);

            var authMode = definition.Config.GetValueOrDefault("AuthMode", "qr");
            
            if (authMode == "qr")
            {
                // QR-based authentication (like OpenClaw)
                var qrResult = await _whatsappClient.ConnectWithQrAsync(definition.Id, ct);
                if (!qrResult.Success)
                {
                    _logger.LogError("WhatsApp QR auth failed: {Error}", qrResult.Error);
                    return ChannelStatus.Error;
                }

                _logger.LogInformation("WhatsApp QR ready. Scan with WhatsApp mobile app.");
            }
            else if (authMode == "business")
            {
                // WhatsApp Business Cloud API
                var apiToken = definition.Config.GetValueOrDefault("ApiToken");
                var phoneNumberId = definition.Config.GetValueOrDefault("PhoneNumberId");
                
                if (string.IsNullOrEmpty(apiToken) || string.IsNullOrEmpty(phoneNumberId))
                {
                    _logger.LogError("WhatsApp Business API requires ApiToken and PhoneNumberId");
                    return ChannelStatus.Error;
                }

                await _whatsappClient.ConnectWithBusinessApiAsync(apiToken, phoneNumberId, ct);
            }

            definition.Activate();
            return ChannelStatus.Active;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize WhatsApp channel {ChannelId}", definition.Id);
            return ChannelStatus.Error;
        }
    }

    public async Task ShutdownAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        _logger.LogInformation("Shutting down WhatsApp channel {ChannelId}", definition.Id);
        await _whatsappClient.DisconnectAsync(ct);
        definition.Deactivate();
    }

    public async Task<ChannelMessage?> ProcessIncomingMessageAsync(object rawMessage, ChannelDefinition definition, CancellationToken ct = default)
    {
        var waMessage = rawMessage as WhatsAppIncomingMessage;
        if (waMessage == null) return null;

        var phoneNumber = waMessage.From; // +50581143874
        var content = waMessage.Text?.Body ?? waMessage.Caption ?? string.Empty;

        if (string.IsNullOrEmpty(content)) return null;

        var session = await GetOrCreateSessionAsync(
            ChannelContext.Create(ChannelType.WhatsApp, definition.Id, Guid.NewGuid().ToString("N"), phoneNumber),
            definition,
            ct
        );

        var message = ChannelMessage.CreateIncoming(
            tenantId: definition.TenantId,
            channelId: definition.Id,
            sessionId: session.Id,
            from: phoneNumber,
            content: content,
            rawPayload: System.Text.Json.JsonSerializer.Serialize(waMessage)
        );

        message.AddMetadata("wa_message_id", waMessage.Id);
        message.AddMetadata("wa_timestamp", waMessage.Timestamp.ToString());

        session.RecordMessage();
        await _sessionRepo.UpdateAsync(session, ct);

        return message;
    }

    public async Task<SendResult> SendReplyAsync(ChannelMessage message, ChannelDefinition definition, CancellationToken ct = default)
    {
        try
        {
            var to = message.To ?? message.Metadata.GetValueOrDefault("phone");
            if (string.IsNullOrEmpty(to))
                return SendResult.Fail("Missing recipient phone number");

            var waMessageId = await _whatsappClient.SendTextMessageAsync(to, message.Content, ct);
            
            message.MarkSent();
            return SendResult.Ok(waMessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp reply to {To}", message.To);
            message.MarkFailed(ex.Message);
            return SendResult.Fail(ex.Message);
        }
    }

    public ChannelContext ExtractContext(object rawMessage, ChannelDefinition definition)
    {
        var waMessage = rawMessage as WhatsAppIncomingMessage;
        if (waMessage == null)
            throw new ArgumentException("Invalid WhatsApp message type", nameof(rawMessage));

        var context = ChannelContext.Create(
            ChannelType.WhatsApp,
            definition.Id,
            Guid.NewGuid().ToString("N"),
            waMessage.From,
            waMessage.Profile?.Name
        );

        context.AddMetadata("wa_id", waMessage.Id);
        context.AddMetadata("phone_country_code", waMessage.From.StartsWith("+") ? "international" : "local");

        return context;
    }

    public async Task<ChannelSession> GetOrCreateSessionAsync(ChannelContext context, ChannelDefinition definition, CancellationToken ct = default)
    {
        var existing = await _sessionRepo.GetByChannelAndIdentifierAsync(
            context.ChannelId,
            context.UserIdentifier,
            definition.TenantId,
            ct
        );

        if (existing != null && !existing.IsExpired())
        {
            existing.RecordMessage();
            await _sessionRepo.UpdateAsync(existing, ct);
            return existing;
        }

        var session = ChannelSession.Create(
            definition.TenantId,
            context.ChannelId,
            ChannelType.WhatsApp,
            context.UserIdentifier
        );

        session.SetExpiration(TimeSpan.FromHours(24));
        session.AddMetadata("display_name", context.UserDisplayName ?? "Unknown");

        await _sessionRepo.InsertAsync(session, ct);
        return session;
    }

    public async Task<HealthStatus> CheckHealthAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        var isHealthy = await _whatsappClient.IsConnectedAsync(ct);
        return isHealthy
            ? HealthStatus.Ok("WhatsApp connection active")
            : HealthStatus.Unhealthy("WhatsApp disconnected");
    }
}
