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
        if (channel is not null && channel.TenantId != campaign.TenantId) return null; // never another tenant's line
        if (channel is null && Enum.TryParse<ChannelType>(campaign.Channel, true, out var type))
            channel = ChannelLines.Pick(await channels.GetByTypeAsync(type, campaign.TenantId, ct)); // the default line
        return channel;
    }

    /// <summary>What a campaign is for: collections (and reminders), sales (and reactivation) or general.</summary>
    public static string PurposeOf(CampaignType type) => type switch
    {
        CampaignType.Collections or CampaignType.Reminder => ChannelLines.Purposes.Collections,
        CampaignType.Sales or CampaignType.Reactivation => ChannelLines.Purposes.Sales,
        _ => ChannelLines.Purposes.General
    };

    /// <summary>
    /// Why the campaign must not go out through this line, or null when it may: a collections line
    /// does not sell and a sales or marketing line does not collect (separate numbers keep each one's
    /// reputation, rules and assistant apart). General lines and custom campaigns go anywhere.
    /// </summary>
    public static string? LineMismatch(ChannelDefinition? line, CampaignContract campaign)
    {
        if (line is null) return null;
        var linePurpose = ChannelLines.PurposeOf(line);
        var campaignPurpose = PurposeOf(campaign.CampaignType);
        if (linePurpose == ChannelLines.Purposes.General || campaignPurpose == ChannelLines.Purposes.General) return null;
        var fits = campaignPurpose == ChannelLines.Purposes.Collections
            ? linePurpose == ChannelLines.Purposes.Collections
            : linePurpose is ChannelLines.Purposes.Sales or ChannelLines.Purposes.Marketing;
        return fits
            ? null
            : $"La línea «{line.Name}» es de {PurposeLabel(linePurpose)} y esta campaña es de {PurposeLabel(campaignPurpose)}. Elige una línea de {PurposeLabel(campaignPurpose)} o una general.";
    }

    public static string PurposeLabel(string purpose) => purpose switch
    {
        ChannelLines.Purposes.Collections => "cobranza",
        ChannelLines.Purposes.Sales => "ventas",
        ChannelLines.Purposes.Marketing => "marketing",
        _ => "uso general"
    };

    /// <summary>WhatsApp through Meta's official API (not the QR bridge): free text only inside the window.</summary>
    public static bool IsOfficialWhatsApp(ChannelDefinition? channel)
        => channel is { Type: ChannelType.WhatsApp } &&
           !string.Equals(channel.Config.GetValueOrDefault("AuthMode", "qr"), "qr", StringComparison.OrdinalIgnoreCase);

    public static string? MetaAccountOf(ChannelDefinition? channel)
        => channel?.Config.GetValueOrDefault("BusinessAccountId") is { Length: > 0 } waba ? waba : null;

    /// <summary>
    /// Why a template cannot go out through this channel, or null when it can: a template only exists
    /// at its provider (a Twilio template through a Twilio channel, a Meta template through Meta) and,
    /// within Meta, in its own WhatsApp Business account.
    /// </summary>
    public static string? TemplateProblem(AgentFlow.Api.Messaging.WhatsAppTemplateDocument template, ChannelDefinition? channel)
    {
        if (channel is null) return null;
        var twilioChannel = AgentFlow.Api.Messaging.TwilioWhatsApp.IsTwilio(channel);
        var twilioTemplate = template.Provider == AgentFlow.Api.Messaging.WhatsAppTemplateProviders.Twilio;
        if (twilioTemplate && !twilioChannel)
            return $"La plantilla {template.Name} es de Twilio y el canal {channel.Name} no es de Twilio.";
        if (!twilioTemplate && twilioChannel)
            return $"La plantilla {template.Name} es de Meta: por el canal de Twilio {channel.Name} usa una plantilla de Twilio.";
        if (twilioTemplate && string.IsNullOrWhiteSpace(template.ContentSid))
            return $"La plantilla {template.Name} no tiene su Content SID de Twilio: sincroniza las plantillas.";
        if (!twilioTemplate && MetaAccountOf(channel) is { } waba && template.BusinessAccountId is { } own && waba != own)
            return $"La plantilla {template.Name} es de otra cuenta de Meta que el canal.";
        return null;
    }

    /// <summary>True while the customer wrote in the last 24 hours.</summary>
    public static bool WindowOpen(CampaignContactExecutionContract contact, DateTimeOffset now)
        => (contact.LastReplyAt ?? contact.RepliedAt) is { } last && now - last < ConversationWindow;

    /// <summary>True while the customer's last message on that number's session is under 24 hours old.</summary>
    public static bool WindowOpen(ChannelSession? session, DateTimeOffset now)
        => session?.Metadata.GetValueOrDefault("last_incoming_at") is { Length: > 0 } raw &&
           DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var last) &&
           now - last < ConversationWindow;

    /// <summary>How a channel may have stored the customer's number: "+50581143874" (QR, Twilio) or "50581143874" (Meta).</summary>
    public static IReadOnlyList<string> SessionIdentifiers(string recipient)
    {
        var digits = new string(recipient.Where(char.IsDigit).ToArray());
        return new[] { recipient.Trim(), "+" + digits, digits }
            .Where(x => x.Trim('+').Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
