using AgentFlow.Abstractions.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Channels.WebChat;

/// <summary>
/// Web chat widget channel handler (embedded widget on websites).
/// </summary>
public sealed class WebChatChannelHandler : IChannelHandler
{
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly ILogger<WebChatChannelHandler> _logger;

    public ChannelType SupportedChannelType => ChannelType.WebChat;

    public WebChatChannelHandler(
        IChannelSessionRepository sessionRepo,
        ILogger<WebChatChannelHandler> logger)
    {
        _sessionRepo = sessionRepo;
        _logger = logger;
    }

    public Task<ChannelStatus> InitializeAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        _logger.LogInformation("WebChat channel {ChannelId} ready", definition.Id);
        definition.Activate();
        return Task.FromResult(ChannelStatus.Active);
    }

    public Task ShutdownAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        definition.Deactivate();
        return Task.CompletedTask;
    }

    public Task<ChannelMessage?> ProcessIncomingMessageAsync(object rawMessage, ChannelDefinition definition, CancellationToken ct = default)
    {
        var webMessage = rawMessage as WebChatIncomingMessage;
        if (webMessage == null) return Task.FromResult<ChannelMessage?>(null);

        var userId = webMessage.UserId;
        var session = GetOrCreateSessionSync(
            ChannelContext.Create(ChannelType.WebChat, definition.Id, Guid.NewGuid().ToString("N"), userId, webMessage.UserName),
            definition
        );

        var message = ChannelMessage.CreateIncoming(
            tenantId: definition.TenantId,
            channelId: definition.Id,
            sessionId: session.Id,
            from: userId,
            content: webMessage.Content,
            rawPayload: System.Text.Json.JsonSerializer.Serialize(webMessage)
        );

        message.AddMetadata("browser", webMessage.Browser ?? "unknown");
        message.AddMetadata("page_url", webMessage.PageUrl ?? "unknown");

        session.RecordMessage();
        _ = _sessionRepo.UpdateAsync(session, ct);

        return Task.FromResult<ChannelMessage?>(message);
    }

    public async Task<SendResult> SendReplyAsync(ChannelMessage message, ChannelDefinition definition, CancellationToken ct = default)
    {
        // Web chat replies are sent via WebSocket/SignalR to connected clients
        // For now, mark as sent (actual delivery handled by SignalR hub)
        message.MarkSent();
        await Task.CompletedTask;
        return SendResult.Ok(message.Id);
    }

    public ChannelContext ExtractContext(object rawMessage, ChannelDefinition definition)
    {
        var webMessage = rawMessage as WebChatIncomingMessage;
        if (webMessage == null)
            throw new ArgumentException("Invalid WebChat message type", nameof(rawMessage));

        var context = ChannelContext.Create(
            ChannelType.WebChat,
            definition.Id,
            Guid.NewGuid().ToString("N"),
            webMessage.UserId,
            webMessage.UserName
        );

        context.AddMetadata("browser", webMessage.Browser ?? "unknown");
        context.AddMetadata("page_url", webMessage.PageUrl ?? "unknown");
        context.AddMetadata("ip_address", webMessage.IpAddress ?? "unknown");

        return context;
    }

    public Task<ChannelSession> GetOrCreateSessionAsync(ChannelContext context, ChannelDefinition definition, CancellationToken ct = default)
    {
        var session = GetOrCreateSessionSync(context, definition);
        return Task.FromResult(session);
    }

    private ChannelSession GetOrCreateSessionSync(ChannelContext context, ChannelDefinition definition)
    {
        // Synchronous helper for session creation
        return ChannelSession.Create(
            definition.TenantId,
            context.ChannelId,
            ChannelType.WebChat,
            context.UserIdentifier
        );
    }

    public Task<HealthStatus> CheckHealthAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        // Web chat is always healthy if the server is running
        return Task.FromResult(HealthStatus.Ok("WebChat server running"));
    }
}

public sealed record WebChatIncomingMessage
{
    public string UserId { get; init; } = string.Empty;
    public string? UserName { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? Browser { get; init; }
    public string? PageUrl { get; init; }
    public string? IpAddress { get; init; }
}
