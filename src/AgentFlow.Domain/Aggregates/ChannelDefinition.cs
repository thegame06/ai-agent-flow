namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// Represents a communication channel configuration (WhatsApp, Web, API, Telegram, etc.)
/// </summary>
public sealed class ChannelDefinition
{
    public string Id { get; private set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public ChannelType Type { get; private set; }
    public ChannelStatus Status { get; private set; } = ChannelStatus.Inactive;
    public Dictionary<string, string> Config { get; private set; } = new();
    public Dictionary<string, string>? Metadata { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastActivityAt { get; private set; }

    public static ChannelDefinition Create(string tenantId, string name, ChannelType type, Dictionary<string, string>? config = null)
    {
        return new ChannelDefinition
        {
            TenantId = tenantId,
            Name = name,
            Type = type,
            Config = config ?? new Dictionary<string, string>(),
            Status = ChannelStatus.Inactive
        };
    }

    public void Activate()
    {
        if (Status == ChannelStatus.Active) return;
        Status = ChannelStatus.Active;
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        Status = ChannelStatus.Inactive;
    }

    public void UpdateConfig(Dictionary<string, string> config)
    {
        Config = config;
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    public void RecordActivity()
    {
        LastActivityAt = DateTimeOffset.UtcNow;
    }
}

public enum ChannelType
{
    WhatsApp = 0,
    WebChat = 1,
    Api = 2,
    Telegram = 3,
    Slack = 4,
    Email = 5,
    Custom = 99
}

public enum ChannelStatus
{
    Inactive = 0,
    Active = 1,
    Error = 2,
    Maintenance = 3
}
