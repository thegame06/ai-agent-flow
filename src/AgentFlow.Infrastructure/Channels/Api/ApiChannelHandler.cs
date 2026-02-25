using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Channels.Api;

/// <summary>
/// REST API channel handler for direct system-to-system integration.
/// </summary>
public sealed class ApiChannelHandler : IChannelHandler
{
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly ILogger<ApiChannelHandler> _logger;

    public ChannelType SupportedChannelType => ChannelType.Api;

    public ApiChannelHandler(
        IChannelSessionRepository sessionRepo,
        ILogger<ApiChannelHandler> logger)
    {
        _sessionRepo = sessionRepo;
        _logger = logger;
    }

    public Task<ChannelStatus> InitializeAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        _logger.LogInformation("API channel {ChannelId} ready", definition.Id);
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
        var apiMessage = rawMessage as ApiIncomingMessage;
        if (apiMessage == null) return Task.FromResult<ChannelMessage?>(null);

        var systemId = apiMessage.SystemId ?? "unknown-system";
        var session = GetOrCreateSessionSync(
            ChannelContext.Create(ChannelType.Api, definition.Id, Guid.NewGuid().ToString("N"), systemId),
            definition
        );

        var message = ChannelMessage.CreateIncoming(
            tenantId: definition.TenantId,
            channelId: definition.Id,
            sessionId: session.Id,
            from: systemId,
            content: apiMessage.Content,
            rawPayload: System.Text.Json.JsonSerializer.Serialize(apiMessage)
        );

        message.Metadata.TryAdd("api_version", apiMessage.ApiVersion ?? "1.0");
        message.Metadata.TryAdd("correlation_id", apiMessage.CorrelationId ?? Guid.NewGuid().ToString("N"));

        session.RecordMessage();
        _ = _sessionRepo.UpdateAsync(session, ct);

        return Task.FromResult<ChannelMessage?>(message);
    }

    public async Task<SendResult> SendReplyAsync(ChannelMessage message, ChannelDefinition definition, CancellationToken ct = default)
    {
        // API replies are returned directly in the HTTP response
        // This method is for callbacks/webhooks if needed
        message.MarkSent();
        await Task.CompletedTask;
        return SendResult.Ok(message.Id);
    }

    public ChannelContext ExtractContext(object rawMessage, ChannelDefinition definition)
    {
        var apiMessage = rawMessage as ApiIncomingMessage;
        if (apiMessage == null)
            throw new ArgumentException("Invalid API message type", nameof(rawMessage));

        var context = ChannelContext.Create(
            ChannelType.Api,
            definition.Id,
            Guid.NewGuid().ToString("N"),
            apiMessage.SystemId ?? "unknown"
        );

        context.AddMetadata("api_version", apiMessage.ApiVersion ?? "1.0");
        context.AddMetadata("correlation_id", apiMessage.CorrelationId ?? Guid.NewGuid().ToString("N"));
        context.AddMetadata("client_ip", apiMessage.ClientIp ?? "unknown");

        return context;
    }

    public Task<ChannelSession> GetOrCreateSessionAsync(ChannelContext context, ChannelDefinition definition, CancellationToken ct = default)
    {
        var session = GetOrCreateSessionSync(context, definition);
        return Task.FromResult(session);
    }

    private ChannelSession GetOrCreateSessionSync(ChannelContext context, ChannelDefinition definition)
    {
        return ChannelSession.Create(
            definition.TenantId,
            context.ChannelId,
            ChannelType.Api,
            context.UserIdentifier
        );
    }

    public Task<HealthStatus> CheckHealthAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        return Task.FromResult(HealthStatus.Ok("API channel operational"));
    }
}

public sealed record ApiIncomingMessage
{
    public string? SystemId { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? ApiVersion { get; init; }
    public string? CorrelationId { get; init; }
    public string? ClientIp { get; init; }
    public Dictionary<string, string>? Context { get; init; }
}
