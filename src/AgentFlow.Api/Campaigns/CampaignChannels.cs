using AgentFlow.Abstractions.Connect;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Api.Campaigns;

/// <summary>The channel a campaign sends through, and what WhatsApp lets it send there.</summary>
public static class CampaignChannels
{
    /// <summary>
    /// WhatsApp customer-service window: after the customer's last message a business may answer
    /// with free text for 24 hours. Outside it, the official API only accepts approved templates.
    /// </summary>
    public static readonly TimeSpan ConversationWindow = TimeSpan.FromHours(24);

    /// <summary>The campaign's channel: its id, or a type ("whatsapp") meaning the tenant's first active one.</summary>
    public static async Task<ChannelDefinition?> ResolveAsync(IChannelDefinitionRepository channels, CampaignContract campaign, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(campaign.Channel)) return null;
        var channel = await channels.GetByIdAsync(campaign.Channel, campaign.TenantId, ct);
        if (channel is null && Enum.TryParse<ChannelType>(campaign.Channel, true, out var type))
            channel = (await channels.GetByTypeAsync(type, campaign.TenantId, ct)).FirstOrDefault(c => c.Status == ChannelStatus.Active);
        return channel;
    }

    /// <summary>WhatsApp through Meta's official API (not the QR bridge): free text only inside the window.</summary>
    public static bool IsOfficialWhatsApp(ChannelDefinition? channel)
        => channel is { Type: ChannelType.WhatsApp } &&
           !string.Equals(channel.Config.GetValueOrDefault("AuthMode", "qr"), "qr", StringComparison.OrdinalIgnoreCase);

    public static string? MetaAccountOf(ChannelDefinition? channel)
        => channel?.Config.GetValueOrDefault("BusinessAccountId") is { Length: > 0 } waba ? waba : null;

    /// <summary>True while the customer wrote in the last 24 hours.</summary>
    public static bool WindowOpen(CampaignContactExecutionContract contact, DateTimeOffset now)
        => (contact.LastReplyAt ?? contact.RepliedAt) is { } last && now - last < ConversationWindow;
}
