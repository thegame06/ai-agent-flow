using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;

namespace AgentFlow.Infrastructure.Channels.Voice;

public sealed class VoiceChannelHandler : IChannelHandler
{
    public ChannelType SupportedChannelType => ChannelType.Voice;

    public Task<ChannelStatus> InitializeAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        var hasConnection = !string.IsNullOrWhiteSpace(definition.Config.GetValueOrDefault("ConnectionId")) ||
                            string.Equals(definition.Config.GetValueOrDefault("Provider"), "twilio", StringComparison.OrdinalIgnoreCase);
        if (!hasConnection)
            return Task.FromResult(ChannelStatus.Maintenance);

        definition.Activate();
        return Task.FromResult(ChannelStatus.Active);
    }

    public Task ShutdownAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        definition.Deactivate();
        return Task.CompletedTask;
    }

    public Task<ChannelMessage?> ProcessIncomingMessageAsync(object rawMessage, ChannelDefinition definition, CancellationToken ct = default)
        => Task.FromResult<ChannelMessage?>(null);

    public Task<SendResult> SendReplyAsync(ChannelMessage message, ChannelDefinition definition, CancellationToken ct = default)
        => Task.FromResult(SendResult.Ok($"voice-{Guid.NewGuid():N}"));

    public ChannelContext ExtractContext(object rawMessage, ChannelDefinition definition)
        => ChannelContext.Create(definition.Type, definition.Id, Guid.NewGuid().ToString("N"), "voice-user", "Voice user");

    public Task<ChannelSession> GetOrCreateSessionAsync(ChannelContext context, ChannelDefinition definition, CancellationToken ct = default)
        => Task.FromResult(ChannelSession.Create(definition.TenantId, definition.Id, definition.Type, context.UserIdentifier));

    public Task<HealthStatus> CheckHealthAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        var provider = definition.Config.GetValueOrDefault("Provider") ?? "twilio";
        var hasConnection = !string.IsNullOrWhiteSpace(definition.Config.GetValueOrDefault("ConnectionId")) ||
                            string.Equals(provider, "twilio", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(hasConnection
            ? HealthStatus.Ok($"Voice channel ready using {provider}.")
            : HealthStatus.Unhealthy("Voice channel requires a reusable Twilio connection."));
    }
}
