using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Infrastructure.Channels.Voice;

/// <summary>
/// Phone calls through Twilio. The assistant's reply is spoken in the TwiML that answers Twilio's
/// webhook, so there is nothing to send here; sessions are kept per caller like any other channel.
/// </summary>
public sealed class VoiceChannelHandler : IChannelHandler
{
    private readonly IChannelSessionRepository? _sessionRepo;

    public VoiceChannelHandler(IChannelSessionRepository? sessionRepo = null)
    {
        _sessionRepo = sessionRepo;
    }

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

    public async Task<ChannelSession> GetOrCreateSessionAsync(ChannelContext context, ChannelDefinition definition, CancellationToken ct = default)
    {
        var agentId = AgentFor(definition);
        if (_sessionRepo is null)
        {
            var transient = ChannelSession.Create(definition.TenantId, definition.Id, definition.Type, context.UserIdentifier);
            if (!string.IsNullOrWhiteSpace(agentId)) transient.LinkAgent(agentId);
            return transient;
        }

        var existing = await _sessionRepo.GetByChannelAndIdentifierAsync(context.ChannelId, context.UserIdentifier, definition.TenantId, ct);
        if (existing is not null && !existing.IsExpired())
        {
            if (string.IsNullOrWhiteSpace(existing.AgentId) && !string.IsNullOrWhiteSpace(agentId))
            {
                existing.LinkAgent(agentId);
                await _sessionRepo.UpdateAsync(existing, ct);
            }
            return existing;
        }

        var session = ChannelSession.Create(definition.TenantId, context.ChannelId, ChannelType.Voice, context.UserIdentifier);
        session.SetExpiration(TimeSpan.FromHours(definition.SessionWindowHours));
        session.Metadata.TryAdd("display_name", context.UserDisplayName ?? context.UserIdentifier);
        if (!string.IsNullOrWhiteSpace(agentId)) session.LinkAgent(agentId);
        await _sessionRepo.InsertAsync(session, ct);
        return session;
    }

    /// <summary>The channel's router, its default assistant, or the first of its routing assistants.</summary>
    public static string? AgentFor(ChannelDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.RouterAgentId)) return definition.RouterAgentId;
        var defaultAgent = definition.Config.GetValueOrDefault("DefaultAgentId");
        if (!string.IsNullOrWhiteSpace(defaultAgent)) return defaultAgent;
        return (definition.Config.GetValueOrDefault("IntentAgents") ?? definition.Config.GetValueOrDefault("RoutingAgents"))?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }

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
