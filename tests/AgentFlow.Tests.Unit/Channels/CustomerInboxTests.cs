using AgentFlow.Api.Channels;
using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Tests.Unit.Channels;

/// <summary>The inbox shows one row per customer, whatever channels they used, and says where each came in.</summary>
public sealed class CustomerInboxTests
{
    private const string Tenant = "tenant-1";

    [Fact]
    public void TheSameCustomerOnSeveralChannels_IsOneRow_WithEveryChannel()
    {
        var qr = Channel("WhatsApp Cobranza (QR)", "qr");
        var twilio = Channel("WhatsApp por Twilio", "twilio");
        var api = Channel("API", null, ChannelType.Api);
        var onQr = Session(qr, "50581143874@c.us", minutesAgo: 30);                    // before addresses were canonical
        var onTwilio = Session(twilio, "+50581143874", minutesAgo: 5, party: ("party-1", "Cliente Prueba"));
        var onApi = Session(api, "+50581143874", minutesAgo: 60);

        var rows = CustomerInbox.Group([onQr, onTwilio, onApi], Channels(qr, twilio, api),
            new Dictionary<string, ResolvedCustomer?> { ["+50581143874"] = new("party-1", "Cliente Prueba") });

        var row = Assert.Single(rows);
        Assert.Equal("party:party-1", row.Key);
        Assert.Equal("Cliente Prueba", row.Name);
        Assert.True(row.Registered);
        Assert.Equal(new[] { "twilio", "qr", "api" }, row.Channels.Select(c => c.Provider));
        Assert.Equal("WhatsApp por Twilio", row.LastChannel!.ChannelName);
    }

    [Fact]
    public void AnUnregisteredCustomer_IsKeyedByTheirAddress_AndNeverCalledUnknown()
    {
        var qr = Channel("QR", "qr");
        var a = Session(qr, "5215550100002@c.us", minutesAgo: 1);
        a.Metadata[Provenance.DisplayNameKey] = "Unknown";

        var row = Assert.Single(CustomerInbox.Group([a], Channels(qr)));

        Assert.Equal("addr:+5215550100002", row.Key);
        Assert.Equal("+5215550100002", row.Name);
        Assert.False(row.Registered);
    }

    [Fact]
    public void CustomersSeenOnlyOnDeletedChannels_AreHiddenUnlessAskedFor()
    {
        var qr = Channel("QR", "qr");
        var real = Session(qr, "+50581143874", minutesAgo: 1);
        var probe = ChannelSession.Create(Tenant, "deleted-probe-channel", ChannelType.Api, "+5215558041090");

        var rows = CustomerInbox.Group([real, probe], Channels(qr));

        Assert.Single(CustomerInbox.Filter(rows, new InboxFilter()));
        Assert.Equal(2, CustomerInbox.Filter(rows, new InboxFilter(IncludeDeletedChannels: true)).Count());
        Assert.Equal("Canal eliminado", rows.Single(r => r.OnlyDeletedChannels).Channels[0].ChannelName);
    }

    [Fact]
    public void Filters_ByProviderAttentionAndSearch()
    {
        var qr = Channel("QR", "qr");
        var twilio = Channel("Twilio", "twilio");
        var ana = Session(qr, "+50581143874", minutesAgo: 1, party: ("p1", "Ana López"));
        ana.RecordOutgoingMessage("listo"); // answered: nothing waiting
        var carlos = Session(twilio, "+5215550100002", minutesAgo: 2, party: ("p2", "Carlos Méndez"));
        ConversationHandoff.Request(carlos, "no puede pagar", "assistant", DateTimeOffset.UtcNow);
        var rows = CustomerInbox.Group([ana, carlos], Channels(qr, twilio));

        Assert.Equal("Carlos Méndez", Assert.Single(CustomerInbox.Filter(rows, new InboxFilter(Provider: "twilio"))).Name);
        Assert.Equal("Carlos Méndez", Assert.Single(CustomerInbox.Filter(rows, new InboxFilter(NeedsAttention: true))).Name);
        Assert.Equal("Ana López", Assert.Single(CustomerInbox.Filter(rows, new InboxFilter(Query: "8114 3874"))).Name);
        Assert.Equal("Ana López", Assert.Single(CustomerInbox.Filter(rows, new InboxFilter(Query: "ana"))).Name);
    }

    private static ChannelDefinition Channel(string name, string? authMode, ChannelType type = ChannelType.WhatsApp)
    {
        var config = authMode is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["AuthMode"] = authMode };
        var channel = ChannelDefinition.Create(Tenant, name, type, config);
        channel.Activate();
        return channel;
    }

    private static IReadOnlyDictionary<string, ChannelDefinition> Channels(params ChannelDefinition[] channels)
        => channels.ToDictionary(c => c.Id);

    private static ChannelSession Session(ChannelDefinition channel, string identifier, int minutesAgo, (string Id, string Name)? party = null)
    {
        var session = ChannelSession.Create(Tenant, channel.Id, channel.Type, identifier);
        session.RecordIncomingMessage("hola");
        typeof(ChannelSession).GetProperty(nameof(ChannelSession.LastActivityAt))!.SetValue(session, DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));
        if (party is { } p)
        {
            session.Metadata[Provenance.PartyIdKey] = p.Id;
            session.Metadata[Provenance.PartyNameKey] = p.Name;
        }
        return session;
    }
}
