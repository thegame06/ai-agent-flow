using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Channels;
using AgentFlow.Api.Collections;
using AgentFlow.Api.Connect;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Channels;

/// <summary>A receipt photo the customer sends is kept, shown in the conversation and attached to their payment report.</summary>
public sealed class ReceiptTests
{
    private const string Tenant = "tenant-1";

    [Theory]
    [InlineData("qr")]
    [InlineData("twilio")]
    [InlineData("meta")]
    public async Task AFileTheCustomerSends_IsKeptOnEveryProvider(string provider)
    {
        var channel = ChannelDefinition.Create(Tenant, "Canal", ChannelType.WhatsApp,
            new Dictionary<string, string> { ["AuthMode"] = provider == "meta" ? "business" : provider });
        var media = new Mock<IMediaStore>();
        media.Setup(m => m.SaveAsync(Tenant, It.IsAny<byte[]>(), "image/jpeg", It.IsAny<string?>(), provider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredMedia("media-1", "image/jpeg", "recibo.jpg", 3));
        var fetcher = new Mock<IInboundMediaFetcher>();
        fetcher.Setup(f => f.Handles(provider)).Returns(true);
        fetcher.Setup(f => f.FetchAsync(Tenant, channel, It.IsAny<InboundAttachment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new byte[] { 1, 2, 3 }, "image/jpeg"));
        var (ingest, session, sent) = Ingest(channel, media.Object, fetcher.Object);

        var attachment = provider == "qr"
            ? new InboundAttachment("image", MimeType: "image/jpeg", Data: [1, 2, 3])
            : new InboundAttachment("image", Url: "https://api.twilio.com/media/1", MimeType: "image/jpeg", ProviderMediaId: "meta-media-1");
        await ingest.IngestAsync(new InboundMessage
        {
            TenantId = Tenant, Channel = channel, Provider = provider, ExternalId = "m-1", From = "+50581143874", Attachments = [attachment]
        });

        Assert.Contains("media-1", sent()!.Metadata[Provenance.AttachmentsKey]);
        Assert.DoesNotContain("Data", sent()!.Metadata[Provenance.AttachmentsKey]);
        Assert.Equal("media-1", session().Metadata[Provenance.LastMediaIdsKey]);
    }

    [Fact]
    public async Task AFileThatCannotBeFetched_IsStillRecordedAsSent()
    {
        var channel = ChannelDefinition.Create(Tenant, "Canal", ChannelType.WhatsApp, new Dictionary<string, string> { ["AuthMode"] = "twilio" });
        var fetcher = new Mock<IInboundMediaFetcher>();
        fetcher.Setup(f => f.Handles("twilio")).Returns(true);
        fetcher.Setup(f => f.FetchAsync(Tenant, channel, It.IsAny<InboundAttachment>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("401"));
        var (ingest, _, sent) = Ingest(channel, new Mock<IMediaStore>().Object, fetcher.Object);

        await ingest.IngestAsync(new InboundMessage
        {
            TenantId = Tenant, Channel = channel, Provider = "twilio", ExternalId = "m-2", From = "+50581143874",
            Attachments = [new InboundAttachment("image", Url: "https://api.twilio.com/media/2")]
        });

        Assert.Equal("[El cliente envió una imagen]", sent()!.Content);
    }

    [Fact]
    public void ALaterReceipt_IsAddedToThePendingReport()
    {
        var report = new CollectionPaymentReportDocument { TenantId = Tenant, PartyId = "p1", Amount = 400m };
        CollectionPaymentDesk.Complete(report, new CollectionPaymentReportDocument { TenantId = Tenant, PartyId = "p1", ReceiptMediaIds = ["media-1"] });
        CollectionPaymentDesk.Complete(report, new CollectionPaymentReportDocument { TenantId = Tenant, PartyId = "p1", ReceiptMediaIds = ["media-1", "media-2"] });

        Assert.Equal(new[] { "media-1", "media-2" }, report.ReceiptMediaIds);
    }

    private static (IInboundMessageIngest, Func<ChannelSession>, Func<ChannelMessage?>) Ingest(ChannelDefinition channel, IMediaStore media, IInboundMediaFetcher fetcher)
    {
        ChannelSession? session = null;
        ChannelMessage? sent = null;
        var handler = new Mock<IChannelHandler>();
        handler.Setup(h => h.GetOrCreateSessionAsync(It.IsAny<ChannelContext>(), It.IsAny<ChannelDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelContext ctx, ChannelDefinition def, CancellationToken _) => session = ChannelSession.Create(Tenant, def.Id, def.Type, ctx.UserIdentifier));
        var gateway = new Mock<IChannelGateway>();
        gateway.Setup(g => g.GetHandler(It.IsAny<ChannelType>())).Returns(handler.Object);
        gateway.Setup(g => g.ProcessMessageAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelMessage m, CancellationToken _) => { sent = m; return ChannelMessage.CreateOutgoing(Tenant, m.ChannelId, m.SessionId, m.From, "ok"); });
        var connect = new Mock<IConnectStore>();
        connect.Setup(c => c.CreateInboxMessageAsync(It.IsAny<ConnectInboxMessageContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectInboxMessageContract m, CancellationToken _) => m);
        var ingest = new InboundMessageIngest(gateway.Object, new Mock<IChannelSessionRepository>().Object, connect.Object,
            new Mock<ICustomerResolver>().Object, NullLogger<InboundMessageIngest>.Instance, media: media, fetchers: [fetcher]);
        return (ingest, () => session!, () => sent);
    }
}
