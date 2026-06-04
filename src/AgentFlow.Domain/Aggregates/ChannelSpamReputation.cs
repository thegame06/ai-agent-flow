namespace AgentFlow.Domain.Aggregates;

public sealed class ChannelSpamReputation
{
    public string Id { get; private set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; private set; } = string.Empty;
    public string ChannelId { get; private set; } = string.Empty;
    public string Identifier { get; private set; } = string.Empty;
    public SpamReputationStatus Status { get; private set; } = SpamReputationStatus.None;
    public int SignalCount { get; private set; }
    public string? LastReasonCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public static ChannelSpamReputation Create(string tenantId, string channelId, string identifier)
    {
        return new ChannelSpamReputation
        {
            TenantId = tenantId,
            ChannelId = channelId,
            Identifier = identifier
        };
    }

    public void MarkSuspected(string? reasonCode = null)
    {
        Status = SpamReputationStatus.Suspected;
        SignalCount++;
        LastReasonCode = reasonCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkConfirmed(string? reasonCode = null)
    {
        Status = SpamReputationStatus.ConfirmedSpam;
        SignalCount++;
        LastReasonCode = reasonCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Clear(string? reasonCode = null)
    {
        Status = SpamReputationStatus.Cleared;
        LastReasonCode = reasonCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum SpamReputationStatus
{
    None = 0,
    Suspected = 1,
    ConfirmedSpam = 2,
    Cleared = 3
}
