namespace AgentFlow.Abstractions;

public sealed record VoiceStatusCallbackRequest
{
    public required string TenantId { get; init; }
    public required string ChannelKey { get; init; }
    public required string CallSid { get; init; }
    public required string CallStatus { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Direction { get; init; }
    public string? CallDuration { get; init; }
    public string? SessionIdHint { get; init; }
}

public interface IVoiceSessionOrchestrator
{
    Task<VoiceSessionState> HandleStatusCallbackAsync(VoiceStatusCallbackRequest request, CancellationToken ct = default);
}

public sealed record VoiceSessionState
{
    public required string SessionId { get; init; }
    public required string ChannelId { get; init; }
    public required string ChannelType { get; init; }
    public required string Identifier { get; init; }
    public required string CallId { get; init; }
    public required string ProviderStatus { get; init; }
    public required string SessionState { get; init; }
    public bool Closed { get; init; }
}
