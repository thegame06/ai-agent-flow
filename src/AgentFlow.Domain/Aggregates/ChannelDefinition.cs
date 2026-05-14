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

    // ── Typed config helpers ────────────────────────────────────────────────
    // Stored in Config dictionary for backward compatibility with existing records.

    /// <summary>
    /// Duration of an open conversation window in hours.
    /// Within this window the channel sends free-text replies.
    /// After expiry the channel must use a template message to re-open the window.
    /// Defaults to 24 hours (WhatsApp Business policy).
    /// </summary>
    public int SessionWindowHours =>
        Config.TryGetValue("SessionWindowHours", out var v) && int.TryParse(v, out var h) && h > 0
            ? h
            : 24;

    /// <summary>
    /// ID of the Router agent assigned to this channel.
    /// The Router receives all incoming messages and decides which workflow to trigger.
    /// </summary>
    public string? RouterAgentId =>
        Config.TryGetValue("RouterAgentId", out var id) && !string.IsNullOrWhiteSpace(id)
            ? id
            : null;

    /// <summary>
    /// WhatsApp template name to use when the session window is closed.
    /// Must be a pre-approved template in the WhatsApp Business account.
    /// </summary>
    public string? ReopenTemplateName =>
        Config.TryGetValue("ReopenTemplateName", out var t) && !string.IsNullOrWhiteSpace(t)
            ? t
            : null;

    public void SetSessionWindowHours(int hours)
    {
        if (hours < 1) throw new ArgumentOutOfRangeException(nameof(hours), "SessionWindowHours must be at least 1.");
        Config["SessionWindowHours"] = hours.ToString();
    }

    public void SetRouterAgentId(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId)) throw new ArgumentException("RouterAgentId cannot be empty.");
        Config["RouterAgentId"] = agentId;
    }

    public void SetReopenTemplateName(string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName)) throw new ArgumentException("ReopenTemplateName cannot be empty.");
        Config["ReopenTemplateName"] = templateName;
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
    Voice = 6,
    CallCenter = 7,
    Custom = 99
}

public enum ChannelStatus
{
    Inactive = 0,
    Active = 1,
    Error = 2,
    Maintenance = 3
}
