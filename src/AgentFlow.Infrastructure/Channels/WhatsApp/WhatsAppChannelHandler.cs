using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentFlow.Infrastructure.Channels.WhatsApp;

/// <summary>
/// WhatsApp channel handler supporting QR authentication (initial) and Business API.
/// </summary>
public sealed class WhatsAppChannelHandler : IChannelHandler, IChannelQrProvider
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

        message.Metadata.TryAdd("wa_message_id", waMessage.Id);
        message.Metadata.TryAdd("wa_timestamp", waMessage.Timestamp.ToString());

        session.RecordIncomingMessage(content);
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

            // Determine if the session window is still open.
            // If the session is expired (window closed) we must use a template message
            // to re-open the 24-hour conversation window per WhatsApp Business policy.
            var sessionId = message.Metadata.GetValueOrDefault("session_id");
            var sessionExpired = await IsSessionExpiredAsync(sessionId, definition, ct);

            string waMessageId;
            if (sessionExpired)
            {
                var templateName = definition.ReopenTemplateName;
                if (string.IsNullOrWhiteSpace(templateName))
                {
                    _logger.LogWarning(
                        "Session window closed for {To} but no ReopenTemplateName configured on channel {ChannelId}. " +
                        "Message will NOT be delivered. Configure a WhatsApp-approved template.",
                        to, definition.Id);
                    return SendResult.Fail("Session window closed and no reopen template configured.");
                }

                _logger.LogInformation(
                    "Session window closed for {To}. Sending template '{Template}' to re-open window.",
                    to, templateName);

                waMessageId = await _whatsappClient.SendTemplateMessageAsync(to, templateName, ct);
            }
            else
            {
                waMessageId = await _whatsappClient.SendTextMessageAsync(to, message.Content, ct);
            }

            message.Metadata["wa_message_id_out"] = waMessageId;
            message.Metadata["wa_window_open"] = (!sessionExpired).ToString().ToLower();
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
            if (string.IsNullOrWhiteSpace(existing.AgentId))
            {
                var selectedAgent = await SelectAgentForSessionAsync(definition, ct);
                if (!string.IsNullOrWhiteSpace(selectedAgent))
                {
                    existing.LinkAgent(selectedAgent);
                    await _sessionRepo.UpdateAsync(existing, ct);
                }
            }
            return existing;
        }

        var session = ChannelSession.Create(
            definition.TenantId,
            context.ChannelId,
            ChannelType.WhatsApp,
            context.UserIdentifier
        );

        // Use channel-configured window; falls back to 24h if not set
        session.SetExpiration(TimeSpan.FromHours(definition.SessionWindowHours));
        session.Metadata.TryAdd("display_name", context.UserDisplayName ?? "Unknown");

        // Router agent takes priority over the round-robin selection
        var routerAgentId = definition.RouterAgentId;
        var assignedAgent = !string.IsNullOrWhiteSpace(routerAgentId)
            ? routerAgentId
            : await SelectAgentForSessionAsync(definition, ct);
        if (!string.IsNullOrWhiteSpace(assignedAgent))
            session.LinkAgent(assignedAgent);

        await _sessionRepo.InsertAsync(session, ct);
        return session;
    }

    /// <summary>
    /// Returns true when the session window is closed and a template message is required.
    /// </summary>
    private async Task<bool> IsSessionExpiredAsync(
        string? sessionId, ChannelDefinition definition, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false; // No session = first message, window is open
        var session = await _sessionRepo.GetByIdAsync(sessionId, definition.TenantId, ct);
        return session?.IsExpired() ?? false;
    }

    private async Task<string?> SelectAgentForSessionAsync(ChannelDefinition definition, CancellationToken ct)
    {
        var routingAgentsRaw = definition.Config.GetValueOrDefault("RoutingAgents");
        if (!string.IsNullOrWhiteSpace(definition.RouterAgentId))
            return definition.RouterAgentId;

        if (string.IsNullOrWhiteSpace(routingAgentsRaw))
        {
            return definition.Config.GetValueOrDefault("DefaultAgentId");
        }

        var candidates = routingAgentsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
            return definition.Config.GetValueOrDefault("DefaultAgentId");

        var active = await _sessionRepo.GetActiveByChannelAsync(definition.Id, definition.TenantId, ct);
        var loadByAgent = active
            .Where(s => !string.IsNullOrWhiteSpace(s.AgentId))
            .GroupBy(s => s.AgentId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var capacities = ParseRoutingCapacities(definition.Config.GetValueOrDefault("RoutingCapacities"));
        var withinCapacity = candidates
            .Where(agentId => !capacities.TryGetValue(agentId, out var max) || (loadByAgent.TryGetValue(agentId, out var current) ? current : 0) < max)
            .ToList();
        var pool = withinCapacity.Count > 0 ? withinCapacity : candidates;

        return pool
            .OrderBy(a => loadByAgent.TryGetValue(a, out var count) ? count : 0)
            .ThenBy(a => a, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static Dictionary<string, int> ParseRoutingCapacities(string? raw)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return result;
        var entries = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[1], out var cap) && cap > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                result[parts[0]] = cap;
        }
        return result;
    }

    public async Task<string?> GetQrCodeAsync(CancellationToken ct = default)
        => await _whatsappClient.GetQrCodeAsync(ct);

    public async Task<HealthStatus> CheckHealthAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        var isHealthy = await _whatsappClient.IsConnectedAsync(ct);
        return isHealthy
            ? HealthStatus.Ok("WhatsApp connection active")
            : HealthStatus.Unhealthy("WhatsApp disconnected");
    }
}
