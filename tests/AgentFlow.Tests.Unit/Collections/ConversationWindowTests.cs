using AgentFlow.Api.Campaigns;
using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Tests.Unit.Collections;

/// <summary>
/// WhatsApp's 24-hour window also opens when the customer writes to the number unprompted, not only
/// when they answer a campaign.
/// </summary>
public sealed class ConversationWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Session_WithACustomerMessageUnder24hOld_KeepsTheWindowOpen()
    {
        var session = Session(Now.AddHours(-2));
        Assert.True(CampaignChannels.WindowOpen(session, Now));
    }

    [Fact]
    public void Session_WithAnOldOrMissingCustomerMessage_LeavesTheWindowClosed()
    {
        Assert.False(CampaignChannels.WindowOpen(Session(Now.AddHours(-25)), Now));
        Assert.False(CampaignChannels.WindowOpen(ChannelSession.Create("t", "c", ChannelType.WhatsApp, "+505"), Now));
        Assert.False(CampaignChannels.WindowOpen((ChannelSession?)null, Now));
    }

    [Fact]
    public void SessionIdentifiers_CoverEveryWayAChannelStoresTheNumber()
    {
        var ids = CampaignChannels.SessionIdentifiers("+50581143874");
        Assert.Equal(new[] { "+50581143874", "50581143874" }, ids);
        Assert.Contains("+50581143874", CampaignChannels.SessionIdentifiers("50581143874"));
    }

    private static ChannelSession Session(DateTimeOffset lastIncoming)
    {
        var session = ChannelSession.Create("tenant-1", "channel-1", ChannelType.WhatsApp, "+50581143874");
        session.Metadata["last_incoming_at"] = lastIncoming.ToString("O");
        return session;
    }
}
