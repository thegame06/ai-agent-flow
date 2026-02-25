namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// Represents an active conversation session within a channel.
/// Similar to Thread but channel-specific with channel metadata.
/// </summary>
public sealed class ChannelSession
{
    public string Id { get; private set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; private set; } = string.Empty;
    public string ChannelId { get; private set; } = string.Empty;
    public string ChannelType { get; private set; } = string.Empty;
    
    /// <summary>
    /// Channel-specific identifier (phone number for WhatsApp, userId for Web, apiKey for API, etc.)
    /// </summary>
    public string Identifier { get; private set; } = string.Empty;
    
    public string? AgentId { get; private set; }
    public string? ThreadId { get; private set; }
    public SessionStatus Status { get; private set; } = SessionStatus.Active;
    
    public Dictionary<string, string> Metadata { get; private set; } = new();
    public int MessageCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; private set; }

    public static ChannelSession Create(string tenantId, string channelId, ChannelType channelType, string identifier)
    {
        return new ChannelSession
        {
            TenantId = tenantId,
            ChannelId = channelId,
            ChannelType = channelType.ToString(),
            Identifier = identifier,
            Status = SessionStatus.Active
        };
    }

    public void LinkAgent(string agentId)
    {
        AgentId = agentId;
    }

    public void LinkThread(string threadId)
    {
        ThreadId = threadId;
    }

    public void RecordMessage()
    {
        MessageCount++;
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    public void Close()
    {
        Status = SessionStatus.Closed;
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    public void SetExpiration(TimeSpan expiresIn)
    {
        ExpiresAt = DateTimeOffset.UtcNow + expiresIn;
    }

    public bool IsExpired()
    {
        return ExpiresAt.HasValue && ExpiresAt <= DateTimeOffset.UtcNow;
    }
}

public enum SessionStatus
{
    Active = 0,
    Closed = 1,
    Paused = 2,
    Expired = 3
}
