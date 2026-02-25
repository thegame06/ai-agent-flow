namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// Normalized message format across all channels.
/// Each channel handler translates from/to its native format.
/// </summary>
public sealed class ChannelMessage
{
    public string Id { get; private set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; private set; } = string.Empty;
    public string ChannelId { get; private set; } = string.Empty;
    public string SessionId { get; private set; } = string.Empty;
    public MessageDirection Direction { get; private set; }
    public MessageType Type { get; private set; } = MessageType.Text;
    
    /// <summary>
    /// Channel-specific sender identifier (phone, userId, etc.)
    /// </summary>
    public string From { get; private set; } = string.Empty;
    
    /// <summary>
    /// Channel-specific recipient identifier
    /// </summary>
    public string? To { get; private set; }
    
    /// <summary>
    /// Message content (text, or serialized JSON for complex types)
    /// </summary>
    public string Content { get; private set; } = string.Empty;
    
    /// <summary>
    /// Raw channel-specific payload for reference/debugging
    /// </summary>
    public string? RawPayload { get; private set; }
    
    public Dictionary<string, string> Metadata { get; private set; } = new();
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public string? AgentExecutionId { get; private set; }
    public MessageStatus Status { get; private set; } = MessageStatus.Pending;
    public string? ErrorMessage { get; private set; }

    public static ChannelMessage CreateIncoming(string tenantId, string channelId, string sessionId, string from, string content, string? rawPayload = null)
    {
        return new ChannelMessage
        {
            TenantId = tenantId,
            ChannelId = channelId,
            SessionId = sessionId,
            Direction = MessageDirection.Incoming,
            From = from,
            Content = content,
            RawPayload = rawPayload,
            Status = MessageStatus.Received
        };
    }

    public static ChannelMessage CreateOutgoing(string tenantId, string channelId, string sessionId, string to, string content)
    {
        return new ChannelMessage
        {
            TenantId = tenantId,
            ChannelId = channelId,
            SessionId = sessionId,
            Direction = MessageDirection.Outgoing,
            To = to,
            From = "agent",
            Content = content,
            Status = MessageStatus.Pending
        };
    }

    public void MarkSent()
    {
        Status = MessageStatus.Sent;
    }

    public void MarkDelivered()
    {
        Status = MessageStatus.Delivered;
    }

    public void MarkFailed(string error)
    {
        Status = MessageStatus.Failed;
        ErrorMessage = error;
    }

    public void LinkExecution(string executionId)
    {
        AgentExecutionId = executionId;
    }
}

public enum MessageDirection
{
    Incoming = 0,
    Outgoing = 1
}

public enum MessageType
{
    Text = 0,
    Image = 1,
    Audio = 2,
    Video = 3,
    Document = 4,
    Location = 5,
    Contact = 6,
    Interactive = 7
}

public enum MessageStatus
{
    Pending = 0,
    Received = 1,
    Processing = 2,
    Sent = 3,
    Delivered = 4,
    Read = 5,
    Failed = 99
}
