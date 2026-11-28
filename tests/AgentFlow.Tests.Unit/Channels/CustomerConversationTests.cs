using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Channels;
using AgentFlow.Api.Connect;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Channels;

/// <summary>
/// The same customer gets the same assistant whatever channel they use (one conversation, not one per
/// channel), and messages sent seconds apart get one answer.
/// </summary>
public sealed class CustomerConversationTests
{
    private const string Tenant = "tenant-1";
    private static readonly TimeSpan Window = TimeSpan.FromHours(12);

    [Fact]
    public void WritingOnAnotherChannel_ContinuesTheConversationTheyJustHad()
    {
        var now = DateTimeOffset.UtcNow;
        var onTwilio = Session("twilio-channel", minutesAgo: 10, thread: "thread-twilio");
        var onQr = Session("qr-channel", minutesAgo: 90, thread: "thread-qr");

        Assert.Same(onTwilio, InboundMessageIngest.ConversationToFollow(onQr, [onTwilio, onQr], Window, now));
    }

    [Fact]
    public void TheMostRecentChannel_KeepsItsOwnConversation()
    {
        var now = DateTimeOffset.UtcNow;
        var onTwilio = Session("twilio-channel", minutesAgo: 90, thread: "thread-twilio");
        var onQr = Session("qr-channel", minutesAgo: 10, thread: "thread-qr");

        Assert.Null(InboundMessageIngest.ConversationToFollow(onQr, [onTwilio], Window, now));
    }

    [Fact]
    public void AnOldConversationElsewhere_IsNotResumed()
    {
        var now = DateTimeOffset.UtcNow;
        var old = Session("twilio-channel", minutesAgo: 60 * 20, thread: "thread-twilio");
        var fresh = ChannelSession.Create(Tenant, "qr-channel", ChannelType.WhatsApp, "+50581143874");

        Assert.Null(InboundMessageIngest.ConversationToFollow(fresh, [old], Window, now));
    }

    [Fact]
    public void ABatch_IsAnsweredByItsLastMessage_WithEveryText()
    {
        var batcher = new InboundBatcher();
        var photo = Incoming("[El cliente envió una imagen]", """[{"Kind":"image","MediaId":"m1"}]""");
        var caption = Incoming("Aquí está mi comprobante");

        var v1 = batcher.Add("s1", photo);
        var v2 = batcher.Add("s1", caption);

        Assert.Null(batcher.TakeIfLatest("s1", v1));
        var batch = batcher.TakeIfLatest("s1", v2)!;
        var combined = InboundBatcher.Combine(batch);
        Assert.Equal("[El cliente envió una imagen]\nAquí está mi comprobante", combined.Content);
        Assert.Equal("Aquí está mi comprobante", combined.Metadata["agentflow.own_content"]);
        Assert.Contains("m1", combined.Metadata[Provenance.AttachmentsKey]);
    }

    [Fact]
    public async Task APhotoAndItsCaptionSecondsApart_GetOneAnswer()
    {
        var channel = ChannelDefinition.Create(Tenant, "Twilio", ChannelType.WhatsApp, new Dictionary<string, string> { ["AuthMode"] = "twilio" });
        var session = ChannelSession.Create(Tenant, channel.Id, ChannelType.WhatsApp, "+50581143874");
        var handler = new Mock<IChannelHandler>();
        handler.Setup(h => h.GetOrCreateSessionAsync(It.IsAny<ChannelContext>(), It.IsAny<ChannelDefinition>(), It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var answered = new List<ChannelMessage>();
        var gateway = new Mock<IChannelGateway>();
        gateway.Setup(g => g.GetHandler(ChannelType.WhatsApp)).Returns(handler.Object);
        gateway.Setup(g => g.ProcessMessageAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelMessage m, CancellationToken _) => { lock (answered) answered.Add(m); return ChannelMessage.CreateOutgoing(Tenant, m.ChannelId, m.SessionId, m.From, "ok"); });
        var connect = new Mock<IConnectStore>();
        connect.Setup(c => c.CreateInboxMessageAsync(It.IsAny<ConnectInboxMessageContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectInboxMessageContract m, CancellationToken _) => m);
        var stored = new Mock<IChannelMessageRepository>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Messaging:BatchWindowMs"] = "300" }).Build();
        var ingest = new InboundMessageIngest(gateway.Object, new Mock<IChannelSessionRepository>().Object, connect.Object,
            new Mock<ICustomerResolver>().Object, NullLogger<InboundMessageIngest>.Instance, configuration: config,
            batcher: new InboundBatcher(), messages: stored.Object);

        var photo = ingest.IngestAsync(new InboundMessage { TenantId = Tenant, Channel = channel, Provider = "twilio", ExternalId = "SM1", From = "whatsapp:+50581143874", Attachments = [new InboundAttachment("image")] });
        await Task.Delay(50);
        var caption = ingest.IngestAsync(new InboundMessage { TenantId = Tenant, Channel = channel, Provider = "twilio", ExternalId = "SM2", From = "whatsapp:+50581143874", Text = "Aquí está mi comprobante" });
        var results = await Task.WhenAll(photo, caption);

        var answer = Assert.Single(answered);
        Assert.Equal("[El cliente envió una imagen]\nAquí está mi comprobante", answer.Content);
        Assert.Equal(InboundStatus.Accepted, results[0].Status);
        Assert.Equal(InboundStatus.Processed, results[1].Status);
        stored.Verify(s => s.InsertAsync(It.Is<ChannelMessage>(m => m.Metadata["agentflow.batched"] == "true"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MessagesOnTwoChannelsAtOnce_AreAnsweredOneAfterTheOther()
    {
        var twilio = ChannelDefinition.Create(Tenant, "Twilio", ChannelType.WhatsApp, new Dictionary<string, string> { ["AuthMode"] = "twilio" });
        var qr = ChannelDefinition.Create(Tenant, "QR", ChannelType.WhatsApp, new Dictionary<string, string> { ["AuthMode"] = "qr" });
        var handler = new Mock<IChannelHandler>();
        handler.Setup(h => h.GetOrCreateSessionAsync(It.IsAny<ChannelContext>(), It.IsAny<ChannelDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelContext _, ChannelDefinition c, CancellationToken _) => ChannelSession.Create(Tenant, c.Id, ChannelType.WhatsApp, "+50581143874"));
        int running = 0, maxRunning = 0;
        var gateway = new Mock<IChannelGateway>();
        gateway.Setup(g => g.GetHandler(ChannelType.WhatsApp)).Returns(handler.Object);
        gateway.Setup(g => g.ProcessMessageAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .Returns(async (ChannelMessage m, CancellationToken _) =>
            {
                var now = Interlocked.Increment(ref running);
                lock (gateway) maxRunning = Math.Max(maxRunning, now);
                await Task.Delay(150);
                Interlocked.Decrement(ref running);
                return ChannelMessage.CreateOutgoing(Tenant, m.ChannelId, m.SessionId, m.From, "ok");
            });
        var connect = new Mock<IConnectStore>();
        connect.Setup(c => c.CreateInboxMessageAsync(It.IsAny<ConnectInboxMessageContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectInboxMessageContract m, CancellationToken _) => m);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Messaging:BatchWindowMs"] = "0" }).Build();
        var ingest = new InboundMessageIngest(gateway.Object, new Mock<IChannelSessionRepository>().Object, connect.Object,
            new Mock<ICustomerResolver>().Object, NullLogger<InboundMessageIngest>.Instance, configuration: config,
            batcher: new InboundBatcher(), messages: new Mock<IChannelMessageRepository>().Object);

        await Task.WhenAll(
            ingest.IngestAsync(new InboundMessage { TenantId = Tenant, Channel = twilio, Provider = "twilio", ExternalId = "SM1", From = "whatsapp:+50581143874", Text = "Ya pagué" }),
            ingest.IngestAsync(new InboundMessage { TenantId = Tenant, Channel = qr, Provider = "qr", ExternalId = "Q1", From = "50581143874@c.us", Text = "¿Lo recibieron?" }));

        Assert.Equal(1, maxRunning);
        gateway.Verify(g => g.ProcessMessageAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static ChannelSession Session(string channelId, int minutesAgo, string thread)
    {
        var session = ChannelSession.Create(Tenant, channelId, ChannelType.WhatsApp, "+50581143874");
        session.LinkThread(thread);
        session.RecordIncomingMessage("hola");
        typeof(ChannelSession).GetProperty(nameof(ChannelSession.LastActivityAt))!.SetValue(session, DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));
        return session;
    }

    private static ChannelMessage Incoming(string text, string? attachments = null)
    {
        var m = ChannelMessage.CreateIncoming(Tenant, "c1", "s1", "+50581143874", text);
        if (attachments is not null) m.Metadata[Provenance.AttachmentsKey] = attachments;
        return m;
    }
}
