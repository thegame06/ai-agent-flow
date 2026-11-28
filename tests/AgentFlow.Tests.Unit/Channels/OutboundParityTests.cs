using AgentFlow.Abstractions;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Channels;
using AgentFlow.Api.Connect;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Infrastructure.Channels.WhatsApp;
using AgentFlow.Infrastructure.Providers;
using Moq;

namespace AgentFlow.Tests.Unit.Channels;

/// <summary>
/// Sending follows one set of rules whatever the channel: the same customer address, the same
/// 24-hour WhatsApp window for every sender, and each provider's own address and template format.
/// </summary>
public sealed class OutboundParityTests
{
    private const string Tenant = "tenant-1";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Theory]
    [InlineData("qr", true)]
    [InlineData("business", false)]
    [InlineData("twilio", false)]
    public async Task TheWindow_OnlyLimitsWhatsAppsOfficialApi(string authMode, bool openWithoutAnyMessage)
    {
        var window = new ConversationWindow(sessions: Sessions(null).Object);
        var channel = Channel(ChannelType.WhatsApp, authMode);

        Assert.Equal(openWithoutAnyMessage, await window.CanSendFreeTextAsync(Tenant, channel, "+50581143874", Now));
    }

    [Theory]
    [InlineData(ChannelType.Api)]
    [InlineData(ChannelType.WebChat)]
    public async Task TheWindow_NeverLimitsOtherChannels(ChannelType type)
    {
        var window = new ConversationWindow(sessions: Sessions(null).Object);
        Assert.True(await window.CanSendFreeTextAsync(Tenant, Channel(type, null), "visitor-1", Now));
    }

    [Theory]
    [InlineData("business")]
    [InlineData("twilio")]
    public async Task TheWindow_OpensWhenTheCustomerWroteToTheNumberInTheLast24h(string authMode)
    {
        var channel = Channel(ChannelType.WhatsApp, authMode);
        var session = ChannelSession.Create(Tenant, channel.Id, ChannelType.WhatsApp, "+50581143874");
        session.Metadata["last_incoming_at"] = Now.AddHours(-3).ToString("O");
        var window = new ConversationWindow(sessions: Sessions(session).Object);

        Assert.True(await window.CanSendFreeTextAsync(Tenant, channel, "50581143874", Now));
    }

    [Fact]
    public async Task AnySender_IsRefusedFreeTextOutsideTheWindow_WithTheReason()
    {
        var channel = Channel(ChannelType.WhatsApp, "twilio");
        var (sender, gateway) = Sender(channel, session: null);

        var result = await sender.SendAsync(Tenant, channel.Id, "+50581143874", "Hola", "advisor-1");

        Assert.False(result.Sent);
        Assert.Equal(ConversationWindow.ClosedMessage, result.Error);
        gateway.Verify(g => g.SendMessageAsync(It.IsAny<string>(), It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ATemplate_GoesOutEvenWithTheWindowClosed()
    {
        var channel = Channel(ChannelType.WhatsApp, "business");
        var (sender, gateway) = Sender(channel, session: null);

        var result = await sender.SendAsync(Tenant, channel.Id, "+50581143874", "Hola", "campaign", "camp-1",
            metadata: new Dictionary<string, string> { [WhatsAppTemplatePayload.MetadataKey] = "{}" });

        Assert.True(result.Sent);
        gateway.Verify(g => g.SendMessageAsync(channel.Id, It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnOutboundMessage_LandsInTheCustomersConversation_WithItsProvenance()
    {
        var channel = Channel(ChannelType.WhatsApp, "qr");
        var session = ChannelSession.Create(Tenant, channel.Id, ChannelType.WhatsApp, "+50581143874");
        var (sender, gateway) = Sender(channel, session);
        ChannelMessage? sent = null;
        gateway.Setup(g => g.SendMessageAsync(channel.Id, It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .Callback((string _, ChannelMessage m, CancellationToken _) => sent = m)
            .ReturnsAsync(SendResult.Ok("x"));

        await sender.SendAsync(Tenant, channel.Id, "50581143874@c.us", "Hola", "campaign", "camp-1");

        Assert.Equal(session.Id, sent!.SessionId);
        Assert.Equal("+50581143874", sent.To);
        Assert.Equal("qr", sent.Metadata[Provenance.ProviderKey]);
    }

    [Theory]
    [InlineData("50581143874", "whatsapp:+50581143874")]
    [InlineData("+505 8114-3874", "whatsapp:+50581143874")]
    [InlineData("whatsapp:+50581143874", "whatsapp:+50581143874")]
    public void Twilio_GetsE164Addresses(string recipient, string expected)
    {
        var form = TwilioWhatsAppProviderAdapter.BuildForm(new ProviderMessageSendRequest { Recipient = recipient, Content = "Hola" }, "14155238886");
        Assert.Equal(expected, form["To"]);
        Assert.Equal("whatsapp:+14155238886", form["From"]);
        Assert.Equal("Hola", form["Body"]);
    }

    [Fact]
    public void Twilio_SendsATemplateAsItsContentSid_WithNumberedVariables()
    {
        var form = TwilioWhatsAppProviderAdapter.BuildForm(new ProviderMessageSendRequest
        {
            Recipient = "+50581143874",
            Content = "Hola Ana, debes 850",
            TemplateName = "HX1234567890abcdef",
            TemplateComponentsJson = """[{"type":"body","parameters":[{"type":"text","text":"Ana"},{"type":"text","text":"850"}]}]"""
        }, "+14155238886");

        Assert.Equal("HX1234567890abcdef", form["ContentSid"]);
        Assert.Equal("""{"1":"Ana","2":"850"}""", form["ContentVariables"]);
        Assert.False(form.ContainsKey("Body"));
    }

    [Fact]
    public void Twilio_RefusesAMetaTemplate_InsteadOfSendingFreeText()
    {
        var error = Assert.Throws<InvalidOperationException>(() => TwilioWhatsAppProviderAdapter.BuildForm(
            new ProviderMessageSendRequest { Recipient = "+50581143874", Content = "x", TemplateName = "recordatorio_pago" }, "+14155238886"));
        Assert.Contains("Content SID", error.Message);
    }

    [Theory]
    [InlineData("+505 8114-3874", "50581143874")]
    [InlineData("50581143874", "50581143874")]
    public void Meta_GetsDigitsOnly(string recipient, string expected)
        => Assert.Equal(expected, MetaWhatsAppProviderAdapter.MetaRecipient(recipient));

    [Fact]
    public void AnAdvisorsHandoff_FollowsTheCustomerIntoANewSession()
    {
        var old = ChannelSession.Create(Tenant, "c", ChannelType.WhatsApp, "+50581143874");
        ConversationHandoff.Request(old, "no puede pagar", "collections-assistant", Now);
        var fresh = ChannelSession.Create(Tenant, "c", ChannelType.WhatsApp, "+50581143874");

        InboundMessageIngest.CarryOverHandoff(old, fresh);

        Assert.True(ConversationHandoff.AssistantPaused(fresh));
        Assert.Equal("no puede pagar", fresh.Metadata[ConversationHandoff.ReasonKey]);
    }

    private static ChannelDefinition Channel(ChannelType type, string? authMode)
    {
        var config = authMode is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["AuthMode"] = authMode };
        var channel = ChannelDefinition.Create(Tenant, "Canal", type, config);
        channel.Activate();
        return channel;
    }

    private static Mock<IChannelSessionRepository> Sessions(ChannelSession? session)
    {
        var sessions = new Mock<IChannelSessionRepository>();
        sessions.Setup(s => s.GetByChannelAndIdentifierAsync(It.IsAny<string>(), It.IsAny<string>(), Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string id, string _, CancellationToken _) => session is not null && session.Identifier == id ? session : null);
        return sessions;
    }

    private static (OutboundMessageSender, Mock<IChannelGateway>) Sender(ChannelDefinition channel, ChannelSession? session)
    {
        var channels = new Mock<IChannelDefinitionRepository>();
        channels.Setup(c => c.GetByIdAsync(channel.Id, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(channel);
        var gateway = new Mock<IChannelGateway>();
        gateway.Setup(g => g.SendMessageAsync(channel.Id, It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(SendResult.Ok("x"));
        var store = new Mock<IConnectStore>();
        store.Setup(s => s.CreateInboxMessageAsync(It.IsAny<ConnectInboxMessageContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectInboxMessageContract m, CancellationToken _) => m);
        var sessions = Sessions(session);
        var window = new ConversationWindow(sessions: sessions.Object);
        return (new OutboundMessageSender(channels.Object, gateway.Object, store.Object, sessions.Object, window), gateway);
    }
}
