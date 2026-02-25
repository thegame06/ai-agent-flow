using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;

namespace AgentFlow.Abstractions.Channels;

/// <summary>
/// Main gateway interface for multi-channel message orchestration.
/// Routes messages through appropriate channel handlers and agent execution.
/// </summary>
public interface IChannelGateway
{
    /// <summary>
    /// Register a channel handler at runtime
    /// </summary>
    void RegisterHandler(IChannelHandler handler);

    /// <summary>
    /// Get handler for a specific channel type
    /// </summary>
    IChannelHandler? GetHandler(ChannelType channelType);

    /// <summary>
    /// Process an incoming message from any channel
    /// Returns the agent response message
    /// </summary>
    Task<ChannelMessage> ProcessMessageAsync(ChannelMessage incomingMessage, CancellationToken ct = default);

    /// <summary>
    /// Send a message through a specific channel
    /// </summary>
    Task<SendResult> SendMessageAsync(string channelId, ChannelMessage message, CancellationToken ct = default);

    /// <summary>
    /// Get active sessions for a channel
    /// </summary>
    Task<IReadOnlyList<ChannelSession>> GetActiveSessionsAsync(string channelId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Close a session gracefully
    /// </summary>
    Task CloseSessionAsync(string sessionId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Broadcast a message to all active sessions in a channel
    /// </summary>
    Task<BroadcastResult> BroadcastAsync(string channelId, string tenantId, string content, CancellationToken ct = default);
}

public sealed record BroadcastResult
{
    public int TotalSent { get; init; }
    public int Failed { get; init; }
    public IReadOnlyList<string> FailedSessionIds { get; init; } = new List<string>();

    public static BroadcastResult Ok(int totalSent) => new() { TotalSent = totalSent };
    public static BroadcastResult Partial(int totalSent, int failed, IReadOnlyList<string> failedIds) =>
        new() { TotalSent = totalSent, Failed = failed, FailedSessionIds = failedIds };
}
