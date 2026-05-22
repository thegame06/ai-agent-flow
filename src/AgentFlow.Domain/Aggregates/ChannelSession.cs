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
    public int LockVersion { get; private set; }

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

    public void RecordIncomingMessage(string? preview = null)
    {
        RecordMessage();

        var unread = 0;
        if (Metadata.TryGetValue("unread_count", out var rawUnread))
            int.TryParse(rawUnread, out unread);

        Metadata["unread_count"] = (unread + 1).ToString();
        Metadata["last_incoming_at"] = LastActivityAt.ToString("O");
        Metadata["last_message_direction"] = "incoming";
        Metadata["reply_pending"] = "true";

        if (!string.IsNullOrWhiteSpace(preview))
            Metadata["last_customer_message"] = preview.Length > 240 ? preview[..240] : preview;
    }

    public void RecordOutgoingMessage(string? preview = null)
    {
        RecordMessage();
        Metadata["unread_count"] = "0";
        Metadata["last_outgoing_at"] = LastActivityAt.ToString("O");
        Metadata["last_message_direction"] = "outgoing";
        Metadata["reply_pending"] = "false";
        Metadata.Remove("last_error");
        Metadata.Remove("last_failure_level");
        Metadata.Remove("last_execution_status");

        if (!string.IsNullOrWhiteSpace(preview))
            Metadata["last_agent_message"] = preview.Length > 240 ? preview[..240] : preview;
    }

    public void MarkReplyFailure(string error, string? failureLevel = null, string? executionStatus = null)
    {
        LastActivityAt = DateTimeOffset.UtcNow;
        Metadata["reply_pending"] = "true";
        Metadata["last_error"] = error;
        if (!string.IsNullOrWhiteSpace(failureLevel))
            Metadata["last_failure_level"] = failureLevel;
        if (!string.IsNullOrWhiteSpace(executionStatus))
            Metadata["last_execution_status"] = executionStatus;
    }

    public void LinkThreadIfMissing(string? threadId)
    {
        if (!string.IsNullOrWhiteSpace(threadId) && string.IsNullOrWhiteSpace(ThreadId))
            ThreadId = threadId;
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

    public void IncrementLockVersion()
    {
        LockVersion++;
    }

    public void StartVoiceCall(
        string callId,
        string? phoneNumber,
        string? direction,
        string? providerStatus)
    {
        LastActivityAt = DateTimeOffset.UtcNow;
        Metadata["voice.call_id"] = callId;
        if (!string.IsNullOrWhiteSpace(phoneNumber))
            Metadata["voice.phone_number"] = phoneNumber!;
        if (!string.IsNullOrWhiteSpace(direction))
            Metadata["voice.direction"] = direction!;
        if (!string.IsNullOrWhiteSpace(providerStatus))
            Metadata["voice.provider_status"] = providerStatus!;
        Metadata["voice.session_state"] = "active";
    }

    public void UpdateVoiceCallStatus(string status, string? duration = null)
    {
        LastActivityAt = DateTimeOffset.UtcNow;
        Metadata["voice.provider_status"] = status;
        if (!string.IsNullOrWhiteSpace(duration))
            Metadata["voice.call_duration"] = duration!;

        var normalized = status.Trim().ToLowerInvariant();
        if (normalized is "completed" or "busy" or "failed" or "no-answer" or "canceled")
        {
            Metadata["voice.session_state"] = "ended";
            Close();
            return;
        }

        Metadata["voice.session_state"] = normalized switch
        {
            "ringing" => "ringing",
            "queued" or "initiated" => "queued",
            "in-progress" or "answered" => "in_progress",
            _ => "active"
        };
    }
}

public enum SessionStatus
{
    Active = 0,
    Closed = 1,
    Paused = 2,
    Expired = 3
}
