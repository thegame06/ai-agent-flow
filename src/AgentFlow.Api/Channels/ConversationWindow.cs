using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Connect;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Api.Channels;

/// <summary>
/// WhatsApp's customer-service window, decided in one place for every sender (campaign, follow-up,
/// advisor, workflow): on WhatsApp's official API (Meta or Twilio) a business may send free text only
/// within 24 hours of the customer's last message; outside it, only an approved template. Other
/// channels and the QR bridge have no such limit.
/// </summary>
public interface IConversationWindow
{
    Task<bool> CanSendFreeTextAsync(string tenantId, ChannelDefinition? channel, string recipient, DateTimeOffset now, CancellationToken ct = default);
}

public sealed class ConversationWindow : IConversationWindow
{
    /// <summary>What a sender tells the person when the window is closed.</summary>
    public const string ClosedMessage =
        "Pasaron mas de 24 h desde el ultimo mensaje del cliente: WhatsApp solo permite enviarle una plantilla aprobada. Usa una plantilla o espera a que el cliente escriba.";

    private readonly ICampaignStore? _campaigns;
    private readonly IChannelSessionRepository? _sessions;

    public ConversationWindow(ICampaignStore? campaigns = null, IChannelSessionRepository? sessions = null)
    {
        _campaigns = campaigns;
        _sessions = sessions;
    }

    public async Task<bool> CanSendFreeTextAsync(string tenantId, ChannelDefinition? channel, string recipient, DateTimeOffset now, CancellationToken ct = default)
    {
        if (!CampaignChannels.IsOfficialWhatsApp(channel)) return true;
        if (string.IsNullOrWhiteSpace(recipient)) return false;

        // A reply to any campaign…
        if (_campaigns is not null && RecipientKeys.From(recipient) is { } key)
        {
            var latest = await _campaigns.FindLatestContactByRecipientKeyAsync(tenantId, key, now - TimeSpan.FromDays(30), ct);
            if (latest is not null && CampaignChannels.WindowOpen(latest, now)) return true;
        }

        // …or any message the customer sent to this number (they may write first, unprompted).
        if (_sessions is not null)
        {
            foreach (var identifier in CampaignChannels.SessionIdentifiers(recipient))
            {
                var session = await _sessions.GetByChannelAndIdentifierAsync(channel!.Id, identifier, tenantId, ct);
                if (CampaignChannels.WindowOpen(session, now)) return true;
            }
        }
        return false;
    }
}
