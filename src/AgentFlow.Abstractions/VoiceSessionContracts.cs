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
    /// <summary>
    /// The customer's canonical address. The session is the customer's, so it is keyed by them: the
    /// caller on an inbound call, the number called on an outbound one.
    /// </summary>
    public string? CustomerAddress { get; init; }

    /// <summary>The customer's side of the call: <see cref="CustomerAddress"/>, else From (inbound) or To (outbound).</summary>
    public string? CustomerSide()
        => !string.IsNullOrWhiteSpace(CustomerAddress) ? CustomerAddress : CustomerOf(Direction, From, To);

    /// <summary>The customer on a call: who called (inbound) or who was called (outbound, "outbound-api"/"outbound-dial").</summary>
    public static string? CustomerOf(string? direction, string? from, string? to)
    {
        var outbound = direction?.StartsWith("outbound", StringComparison.OrdinalIgnoreCase) == true;
        return outbound ? to : (string.IsNullOrWhiteSpace(from) ? to : from);
    }
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
