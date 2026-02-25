using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Domain.Common;

/// <summary>
/// Encapsulates channel-specific context for agent execution.
/// Provides unified interface regardless of channel type.
/// </summary>
public sealed class ChannelContext
{
    public string ChannelId { get; private set; } = string.Empty;
    public ChannelType ChannelType { get; private set; }
    public string SessionId { get; private set; } = string.Empty;
    
    /// <summary>
    /// Unified identifier for the user across channels.
    /// WhatsApp: phone number, Web: userId, API: apiKey/systemId
    /// </summary>
    public string UserIdentifier { get; private set; } = string.Empty;
    
    /// <summary>
    /// Display name if available (WhatsApp push name, Web user name, etc.)
    /// </summary>
    public string? UserDisplayName { get; private set; }
    
    /// <summary>
    /// Channel-specific metadata (phone country code, browser info, API version, etc.)
    /// </summary>
    public Dictionary<string, string> Metadata { get; private set; } = new();

    public static ChannelContext Create(ChannelType channelType, string channelId, string sessionId, string userIdentifier, string? userDisplayName = null)
    {
        return new ChannelContext
        {
            ChannelType = channelType,
            ChannelId = channelId,
            SessionId = sessionId,
            UserIdentifier = userIdentifier,
            UserDisplayName = userDisplayName
        };
    }

    public void AddMetadata(string key, string value)
    {
        Metadata[key] = value;
    }

    /// <summary>
    /// Get phone number if WhatsApp channel, null otherwise
    /// </summary>
    public string? GetPhoneNumber()
    {
        if (ChannelType != ChannelType.WhatsApp) return null;
        return UserIdentifier;
    }

    /// <summary>
    /// Get user ID if Web/API channel, null otherwise
    /// </summary>
    public string? GetUserId()
    {
        if (ChannelType is ChannelType.WebChat or ChannelType.Api) return UserIdentifier;
        return null;
    }
}
