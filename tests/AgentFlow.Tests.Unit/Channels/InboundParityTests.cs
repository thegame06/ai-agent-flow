using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Channels;
using AgentFlow.Api.Connect;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Channels;

/// <summary>
/// The same customer, writing the same thing through any channel or provider, must be treated the same:
/// one address, the same registered customer, the same session rules, inbox record, campaign
/// attribution and provenance. Only the provider name and its raw address differ.
/// </summary>
public sealed class InboundParityTests
{
    private const string Tenant = "tenant-1";
    private const string Canonical = "+50581143874";

    public static IEnumerable<object[]> SameCustomerOnEveryChannel()
    {
        yield return new object[] { "qr", ChannelType.WhatsApp, "qr", "50581143874@c.us" };
        yield return new object[] { "meta", ChannelType.WhatsApp, "business", "50581143874" };
        yield return new object[] { "twilio", ChannelType.WhatsApp, "twilio", "whatsapp:+50581143874" };
        yield return new object[] { "api", ChannelType.Api, "", "+50581143874" };
        yield return new object[] { "webchat", ChannelType.WebChat, "", "+505 8114 3874" };
    }

    [Theory]
    [MemberData(nameof(SameCustomerOnEveryChannel))]
    public async Task EveryChannel_TreatsTheCustomerTheSameWay(string provider, ChannelType type, string authMode, string rawFrom)
    {
        var world = new World(type, authMode);

        var result = await world.Ingest.IngestAsync(new InboundMessage
        {
            TenantId = Tenant,
            Channel = world.Channel,
            Provider = provider,
            ExternalId = $"{provider}-msg-1",
            From = rawFrom,
            DisplayName = "Unknown",
            Text = "Ya pague 400"
        });

        Assert.Equal(InboundStatus.Processed, result.Status);
        // One address for the customer, whatever the provider wrote.
        Assert.Equal(Canonical, world.SessionIdentifier);
        Assert.Equal(Canonical, world.Inbox!.Recipient);
        Assert.Equal(Canonical, world.SentToGateway!.From);
        world.Outcomes.Verify(o => o.AttributeReplyAsync(Tenant, Canonical, "Ya pague 400", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>(), true), Times.Once);

        // The registered customer, not the app's "Unknown".
        var session = world.Session!;
        Assert.Equal("party-1", session.Metadata[Provenance.PartyIdKey]);
        Assert.Equal("Cliente Prueba WhatsApp", session.Metadata[Provenance.DisplayNameKey]);
        Assert.Equal("party-1", world.SentToGateway.Metadata[Provenance.PartyIdKey]);

        // Where it came in.
        Assert.Equal(provider, world.SentToGateway.Metadata[Provenance.ProviderKey]);
        Assert.Equal(type.ToString(), world.SentToGateway.Metadata[Provenance.ChannelTypeKey]);
        Assert.Equal(rawFrom, world.SentToGateway.Metadata[Provenance.CustomerRawKey]);
        Assert.Equal("true", session.Metadata["reply_pending"]);
    }

    [Fact]
    public async Task AProviderRetry_IsNotAnsweredTwice()
    {
        var world = new World(ChannelType.WhatsApp, "qr");
        var message = new InboundMessage { TenantId = Tenant, Channel = world.Channel, Provider = "qr", ExternalId = "m-1", From = "50581143874@c.us", Text = "hola" };

        await world.Ingest.IngestAsync(message);
        world.ExistingInboxKey = world.Inbox!.ExternalEventKey;
        var retry = await world.Ingest.IngestAsync(message);

        Assert.Equal(InboundStatus.Duplicate, retry.Status);
        world.Gateway.Verify(g => g.ProcessMessageAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TheSameTextTwice_WithoutAProviderId_IsTwoMessages()
    {
        var channel = ChannelDefinition.Create(Tenant, "api", ChannelType.Api);
        var address = CustomerAddress.Normalize("+5215550100002", ChannelType.Api);
        var hola = new InboundMessage { TenantId = Tenant, Channel = channel, Provider = "api", From = "+5215550100002", Text = "Hola" };

        Assert.NotEqual(InboundMessageIngest.ExternalKey(hola, address, "Hola"), InboundMessageIngest.ExternalKey(hola, address, "Hola"));
        // A caller retrying with its own correlation id is still one message.
        var retried = hola with { CorrelationId = "corr-1" };
        Assert.Equal(InboundMessageIngest.ExternalKey(retried, address, "Hola"), InboundMessageIngest.ExternalKey(retried, address, "Hola"));
    }

    [Fact]
    public async Task AFileWithoutText_ReachesTheAssistantAsALine_AndKeepsTheAttachment()
    {
        var world = new World(ChannelType.WhatsApp, "twilio");

        await world.Ingest.IngestAsync(new InboundMessage
        {
            TenantId = Tenant, Channel = world.Channel, Provider = "twilio", ExternalId = "SM1", From = "whatsapp:+50581143874",
            Attachments = [new InboundAttachment("image", "https://api.twilio.com/media/1", "image/jpeg")]
        });

        Assert.Equal("[El cliente envió una imagen]", world.SentToGateway!.Content);
        Assert.Contains("https://api.twilio.com/media/1", world.SentToGateway.Metadata[Provenance.AttachmentsKey]);
    }

    [Fact]
    public async Task TheSessionWindow_CountsFromTheCustomersLatestMessage()
    {
        var world = new World(ChannelType.WhatsApp, "qr");
        world.ExistingSession = ChannelSession.Create(Tenant, world.Channel.Id, ChannelType.WhatsApp, Canonical);
        world.ExistingSession.SetExpiration(TimeSpan.FromMinutes(5));

        await world.Ingest.IngestAsync(new InboundMessage { TenantId = Tenant, Channel = world.Channel, Provider = "qr", ExternalId = "m-2", From = "50581143874@c.us", Text = "sigo aqui" });

        Assert.True(world.ExistingSession.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));
    }

    [Fact]
    public async Task AConversationOpenedUnderAnOldAddress_ContinuesUnderTheCanonicalOne()
    {
        var world = new World(ChannelType.WhatsApp, "qr");
        var old = ChannelSession.Create(Tenant, world.Channel.Id, ChannelType.WhatsApp, "50581143874@c.us");
        world.LegacySessions["50581143874@c.us"] = old;

        await world.Ingest.IngestAsync(new InboundMessage { TenantId = Tenant, Channel = world.Channel, Provider = "qr", ExternalId = "m-3", From = "50581143874@c.us", Text = "hola" });

        Assert.Equal(Canonical, old.Identifier);
    }

    [Theory]
    [InlineData("50581143874@c.us", ChannelType.WhatsApp, null, "+50581143874", true)]
    [InlineData("whatsapp:+50581143874", ChannelType.WhatsApp, null, "+50581143874", true)]
    [InlineData("8114 3874", ChannelType.WhatsApp, "505", "+50581143874", true)]
    [InlineData("005215550100002", ChannelType.WhatsApp, "505", "+5215550100002", true)]
    [InlineData("visitor-42", ChannelType.WebChat, "505", "visitor-42", false)]
    [InlineData("admin-user-1", ChannelType.Api, "505", "admin-user-1", false)]
    public void CustomerAddress_IsCanonical(string raw, ChannelType type, string? country, string expected, bool isPhone)
    {
        var address = CustomerAddress.Normalize(raw, type, country);
        Assert.Equal(expected, address.Value);
        Assert.Equal(isPhone, address.IsPhone);
    }

    [Fact]
    public void ALinkedWhatsAppId_IsFlaggedAsUnresolved()
    {
        var address = CustomerAddress.Normalize("123456789012345@lid", ChannelType.WhatsApp);
        Assert.True(address.Unresolved);
        Assert.False(address.IsPhone);
    }

    [Theory]
    [InlineData("qr", null, "qr")]
    [InlineData("business", null, "meta")]
    [InlineData("twilio", null, "twilio")]
    [InlineData(null, ChannelType.Api, "api")]
    [InlineData(null, ChannelType.WebChat, "webchat")]
    public void Provenance_NamesTheProviderOfEveryChannel(string? authMode, ChannelType? type, string expected)
    {
        var config = authMode is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["AuthMode"] = authMode };
        var channel = ChannelDefinition.Create(Tenant, "c", type ?? ChannelType.WhatsApp, config);
        Assert.Equal(expected, Provenance.ProviderOf(channel));
    }

    /// <summary>A tenant with one channel, one registered customer and in-memory stores.</summary>
    private sealed class World
    {
        public ChannelDefinition Channel { get; }
        public Mock<IChannelGateway> Gateway { get; } = new();
        public Mock<ICampaignOutcomeService> Outcomes { get; } = new();
        public IInboundMessageIngest Ingest { get; }
        public ChannelSession? Session { get; private set; }
        public ChannelSession? ExistingSession { get; set; }
        public Dictionary<string, ChannelSession> LegacySessions { get; } = new();
        public string? SessionIdentifier { get; private set; }
        public ConnectInboxMessageContract? Inbox { get; private set; }
        public string? ExistingInboxKey { get; set; }
        public ChannelMessage? SentToGateway { get; private set; }

        public World(ChannelType type, string authMode)
        {
            var config = new Dictionary<string, string> { ["DefaultAgentId"] = "collections-assistant", ["SessionWindowHours"] = "24" };
            if (!string.IsNullOrEmpty(authMode)) config["AuthMode"] = authMode;
            Channel = ChannelDefinition.Create(Tenant, "Canal", type, config);
            Channel.Activate();

            var handler = new Mock<IChannelHandler>();
            handler.Setup(h => h.GetOrCreateSessionAsync(It.IsAny<ChannelContext>(), It.IsAny<ChannelDefinition>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ChannelContext ctx, ChannelDefinition def, CancellationToken _) =>
                {
                    SessionIdentifier = ctx.UserIdentifier;
                    Session = ExistingSession ?? ChannelSession.Create(Tenant, def.Id, def.Type, ctx.UserIdentifier);
                    return Session;
                });
            Gateway.Setup(g => g.GetHandler(type)).Returns(handler.Object);
            Gateway.Setup(g => g.ProcessMessageAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ChannelMessage m, CancellationToken _) =>
                {
                    SentToGateway = m;
                    return ChannelMessage.CreateOutgoing(Tenant, m.ChannelId, m.SessionId, m.From, "ok");
                });

            var sessions = new Mock<IChannelSessionRepository>();
            sessions.Setup(s => s.GetByChannelAndIdentifierAsync(It.IsAny<string>(), It.IsAny<string>(), Tenant, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string id, string _, CancellationToken _) =>
                    ExistingSession is not null && ExistingSession.Identifier == id ? ExistingSession : LegacySessions.GetValueOrDefault(id));

            var connect = new Mock<IConnectStore>();
            connect.Setup(c => c.GetInboxMessageByExternalKeyAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string key, CancellationToken _) => key == ExistingInboxKey ? Inbox : null);
            connect.Setup(c => c.CreateInboxMessageAsync(It.IsAny<ConnectInboxMessageContract>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConnectInboxMessageContract m, CancellationToken _) => Inbox = m);

            var customers = new Mock<ICustomerResolver>();
            customers.Setup(c => c.ResolveAsync(Tenant, It.Is<CustomerAddress>(a => a.Value == Canonical), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedCustomer("party-1", "Cliente Prueba WhatsApp"));

            Ingest = new InboundMessageIngest(Gateway.Object, sessions.Object, connect.Object, customers.Object,
                NullLogger<InboundMessageIngest>.Instance, configuration: null, campaignOutcomes: Outcomes.Object);
        }
    }
}
