using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Core.Engine;

public sealed class VoiceSessionOrchestrator : IVoiceSessionOrchestrator
{
    private readonly IChannelDefinitionRepository _channelRepo;
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly ILogger<VoiceSessionOrchestrator> _logger;

    public VoiceSessionOrchestrator(
        IChannelDefinitionRepository channelRepo,
        IChannelSessionRepository sessionRepo,
        ILogger<VoiceSessionOrchestrator> logger)
    {
        _channelRepo = channelRepo;
        _sessionRepo = sessionRepo;
        _logger = logger;
    }

    public async Task<VoiceSessionState> HandleStatusCallbackAsync(VoiceStatusCallbackRequest request, CancellationToken ct = default)
    {
        var channelType = string.Equals(request.ChannelKey, "callcenter", StringComparison.OrdinalIgnoreCase)
            ? ChannelType.CallCenter
            : ChannelType.Voice;
        var channel = await ResolveActiveChannelAsync(request.TenantId, channelType, ct)
            ?? throw new InvalidOperationException($"No active {channelType} channel configured for tenant '{request.TenantId}'.");

        var session = await ResolveSessionAsync(request, channel, ct);
        var normalizedStatus = request.CallStatus.Trim().ToLowerInvariant();
        var phoneNumber = !string.IsNullOrWhiteSpace(request.To) ? request.To : request.From;

        if (string.IsNullOrWhiteSpace(session.Metadata.GetValueOrDefault("voice.call_id")))
            session.StartVoiceCall(request.CallSid, phoneNumber, request.Direction, normalizedStatus);
        else
            session.UpdateVoiceCallStatus(normalizedStatus, request.CallDuration);

        session.Metadata["voice.channel"] = request.ChannelKey;
        session.Metadata["voice.last_callback_at"] = DateTimeOffset.UtcNow.ToString("O");
        session.Metadata["voice.from"] = request.From ?? string.Empty;
        session.Metadata["voice.to"] = request.To ?? string.Empty;

        var update = await _sessionRepo.UpdateAsync(session, ct);
        if (!update.IsSuccess)
        {
            _logger.LogWarning(
                "Voice session update reported failure. Tenant={TenantId} SessionId={SessionId} Error={Error}",
                request.TenantId,
                session.Id,
                update.Error?.Message);
        }

        return new VoiceSessionState
        {
            SessionId = session.Id,
            ChannelId = session.ChannelId,
            ChannelType = session.ChannelType,
            Identifier = session.Identifier,
            CallId = session.Metadata.GetValueOrDefault("voice.call_id") ?? request.CallSid,
            ProviderStatus = session.Metadata.GetValueOrDefault("voice.provider_status") ?? normalizedStatus,
            SessionState = session.Metadata.GetValueOrDefault("voice.session_state") ?? "active",
            Closed = session.Status == SessionStatus.Closed
        };
    }

    private async Task<ChannelDefinition?> ResolveActiveChannelAsync(string tenantId, ChannelType type, CancellationToken ct)
    {
        var channels = await _channelRepo.GetByTypeAsync(type, tenantId, ct);
        return channels.FirstOrDefault(x => x.Status == ChannelStatus.Active);
    }

    private async Task<ChannelSession> ResolveSessionAsync(
        VoiceStatusCallbackRequest request,
        ChannelDefinition channel,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.SessionIdHint))
        {
            var byHint = await _sessionRepo.GetByIdAsync(request.SessionIdHint!, request.TenantId, ct);
            if (byHint is not null)
                return byHint;
        }

        var identifier = !string.IsNullOrWhiteSpace(request.To)
            ? request.To!
            : request.CallSid;

        var existing = await _sessionRepo.GetByChannelAndIdentifierAsync(channel.Id, identifier, request.TenantId, ct);
        if (existing is not null)
            return existing;

        var session = ChannelSession.Create(request.TenantId, channel.Id, channel.Type, identifier);
        session.SetExpiration(TimeSpan.FromHours(channel.SessionPolicy.SessionWindowHours));
        session.StartVoiceCall(request.CallSid, identifier, request.Direction, request.CallStatus);
        session.Metadata["display_name"] = identifier;

        var insert = await _sessionRepo.InsertAsync(session, ct);
        if (!insert.IsSuccess)
            throw new InvalidOperationException(insert.Error?.Message ?? "Failed to create voice session.");

        return session;
    }
}
