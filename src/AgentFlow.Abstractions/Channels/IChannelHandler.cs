using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;

namespace AgentFlow.Abstractions.Channels;

/// <summary>
/// Contract for channel-specific message handlers.
/// Each communication channel (WhatsApp, Web, API, etc.) implements this interface.
/// </summary>
public interface IChannelHandler
{
    /// <summary>
    /// The channel type this handler supports
    /// </summary>
    ChannelType SupportedChannelType { get; }

    /// <summary>
    /// Initialize the channel (connect to WhatsApp, start WebSocket, etc.)
    /// </summary>
    Task<ChannelStatus> InitializeAsync(ChannelDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Shutdown the channel gracefully
    /// </summary>
    Task ShutdownAsync(ChannelDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Process an incoming message from the channel
    /// </summary>
    Task<ChannelMessage?> ProcessIncomingMessageAsync(object rawMessage, ChannelDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Send a reply message through the channel
    /// </summary>
    Task<SendResult> SendReplyAsync(ChannelMessage message, ChannelDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Extract channel context from raw message
    /// </summary>
    ChannelContext ExtractContext(object rawMessage, ChannelDefinition definition);

    /// <summary>
    /// Get or create a session for this user/channel combination
    /// </summary>
    Task<ChannelSession> GetOrCreateSessionAsync(ChannelContext context, ChannelDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Health check for the channel connection
    /// </summary>
    Task<HealthStatus> CheckHealthAsync(ChannelDefinition definition, CancellationToken ct = default);
}

public sealed record SendResult
{
    public bool Success { get; init; }
    public string? MessageId { get; init; }
    public string? Error { get; init; }

    public static SendResult Ok(string messageId) => new() { Success = true, MessageId = messageId };
    public static SendResult Fail(string error) => new() { Success = false, Error = error };
}

public sealed record HealthStatus
{
    public bool Healthy { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;

    public static HealthStatus Ok(string? message = null) => new() { Healthy = true, Message = message };
    public static HealthStatus Unhealthy(string message) => new() { Healthy = false, Message = message };
}
