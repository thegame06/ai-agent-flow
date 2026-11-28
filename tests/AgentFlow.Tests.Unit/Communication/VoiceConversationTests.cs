using AgentFlow.Abstractions;
using AgentFlow.Api.Channels;
using AgentFlow.Api.Voice;
using AgentFlow.Core.Engine;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using AgentFlow.Infrastructure.Channels.Voice;
using AgentFlow.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Communication;

/// <summary>A phone call is one more channel of the customer's conversation with the assistant.</summary>
public class VoiceConversationTests
{
    private const string Tenant = "tenant-a";
    private const string PublicBase = "https://agentflow.example.com";

    [Fact]
    public void TheAssistantsChatText_IsSpokenWithoutMarkdownLinksOrEmojis()
    {
        var spoken = TwilioVoiceTwiml.Speakable("**Hola Carlos** 👋\n- Cuota: C$1,500\n- Paga aquí: https://pay.example.com/x\nCuenta 1002003004");

        Assert.DoesNotContain("*", spoken);
        Assert.DoesNotContain("http", spoken);
        Assert.DoesNotContain("👋", spoken);
        Assert.Contains("Hola Carlos.", spoken);
        Assert.Contains("C$1,500", spoken);
        Assert.Contains("el enlace que te enviamos por mensaje", spoken);
        // Account numbers are read digit by digit; amounts are not.
        Assert.Contains("1 0 0 2 0 0 3 0 0 4", spoken);
    }

    [Fact]
    public void AnOutboundCall_WithAConversationUrl_SpeaksTheScriptAndListens()
    {
        var twiml = TwilioVoiceProviderAdapter.BuildCallTwiml(new ProviderVoiceCallRequest
        {
            PhoneNumber = "+50581143874",
            Script = "Hola, te llamamos de la tienda.",
            ConversationUrl = $"{PublicBase}/api/v1/tenants/{Tenant}/webhooks/twilio/voice/gather?channel=voice&silences=0"
        });

        Assert.StartsWith("<Response><Gather input=\"speech\"", twiml);
        Assert.Contains("action=\"https://agentflow.example.com/api/v1/tenants/tenant-a/webhooks/twilio/voice/gather?channel=voice&amp;silences=0\"", twiml);
        Assert.Contains("Hola, te llamamos de la tienda.</Say></Gather>", twiml);
        Assert.EndsWith("<Hangup/></Response>", twiml);
    }

    [Fact]
    public void AnOutboundCall_WithoutAConversationUrl_OnlySpeaksTheScript()
    {
        var twiml = TwilioVoiceProviderAdapter.BuildCallTwiml(new ProviderVoiceCallRequest { PhoneNumber = "+1", Script = "Aviso." });

        Assert.Equal("<Response><Say language=\"es-MX\" voice=\"Polly.Mia\">Aviso.</Say></Response>", twiml);
    }

    [Theory]
    [InlineData("inbound", "+50581143874", "+15005550006", "+50581143874")]
    [InlineData("outbound-api", "+15005550006", "+50581143874", "+50581143874")]
    [InlineData(null, "+50581143874", "+15005550006", "+50581143874")]
    public void TheCustomer_IsTheCallerInbound_AndTheNumberCalledOutbound(string? direction, string from, string to, string expected)
        => Assert.Equal(expected, VoiceStatusCallbackRequest.CustomerOf(direction, from, to));

    [Fact]
    public async Task WhatTheCustomerSays_GoesThroughTheSharedPipeline_AndTheReplyIsSpoken()
    {
        var channel = VoiceChannel();
        InboundMessage? received = null;
        var ingest = new Mock<IInboundMessageIngest>();
        ingest.Setup(x => x.IngestAsync(It.IsAny<InboundMessage>(), false, It.IsAny<CancellationToken>()))
            .Callback<InboundMessage, bool, CancellationToken>((m, _, _) => received = m)
            .ReturnsAsync(new InboundResult(InboundStatus.Processed,
                Reply: ChannelMessage.CreateOutgoing(Tenant, channel.Id, "s1", "+50581143874", "Tu saldo es **C$500**.")));
        var service = Service(channel, ingest.Object);

        var twiml = await service.AnswerAsync(new VoiceTurn
        {
            TenantId = Tenant,
            CallSid = "CA1",
            From = "+15005550006",
            To = "+50581143874",
            Direction = "outbound-api",
            SpeechResult = "¿Cuánto debo?"
        }, CancellationToken.None);

        Assert.NotNull(received);
        Assert.Equal("+50581143874", received!.From);
        Assert.Equal("twilio", received.Provider);
        Assert.Same(channel, received.Channel);
        Assert.Equal("¿Cuánto debo?", received.Text);
        // Every turn of a call shares the CallSid: it must not be the dedupe key.
        Assert.Null(received.CorrelationId);
        Assert.Null(received.ExternalId);
        Assert.Equal("CA1", received.Metadata!["voice.call_id"]);
        Assert.Contains("Tu saldo es C$500.</Say></Gather>", twiml);
        Assert.Contains($"action=\"{PublicBase}/api/v1/tenants/{Tenant}/webhooks/twilio/voice/gather?channel=voice&amp;silences=0&amp;turn=1\"", twiml);
    }

    [Fact]
    public async Task Silence_IsAskedAgainOnce_ThenTheCallEnds()
    {
        var ingest = new Mock<IInboundMessageIngest>(MockBehavior.Strict);
        var service = Service(VoiceChannel(), ingest.Object);

        var first = await service.AnswerAsync(new VoiceTurn { TenantId = Tenant, From = "+50581143874", SpeechResult = "" }, CancellationToken.None);
        var second = await service.AnswerAsync(new VoiceTurn { TenantId = Tenant, From = "+50581143874", Silences = 1 }, CancellationToken.None);

        Assert.Contains("<Gather", first);
        Assert.Contains("silences=1", first);
        Assert.DoesNotContain("<Gather", second);
        Assert.Contains("<Hangup/>", second);
    }

    [Fact]
    public async Task WhenTheAssistantFails_TheCallerHearsAnApology_AndTheCallContinues()
    {
        var ingest = new Mock<IInboundMessageIngest>();
        ingest.Setup(x => x.IngestAsync(It.IsAny<InboundMessage>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var service = Service(VoiceChannel(), ingest.Object);

        var twiml = await service.AnswerAsync(new VoiceTurn { TenantId = Tenant, From = "+50581143874", SpeechResult = "Hola" }, CancellationToken.None);

        Assert.Contains("Disculpa, tuve un problema", twiml);
        Assert.Contains("<Gather", twiml);
    }

    [Fact]
    public async Task WithoutAnActiveVoiceChannel_TheCallIsToldTheLineIsUnavailable()
    {
        var channels = new Mock<IChannelDefinitionRepository>();
        channels.Setup(x => x.GetByTypeAsync(ChannelType.Voice, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new VoiceConversationService(channels.Object, Mock.Of<IInboundMessageIngest>(), Mock.Of<IProviderResolver>(),
            NullLogger<VoiceConversationService>.Instance, Config());

        var twiml = await service.GreetAsync(Tenant, "voice", CancellationToken.None);

        Assert.Contains("no está disponible", twiml);
        Assert.Contains("<Hangup/>", twiml);
    }

    [Fact]
    public async Task TheGreeting_IsTheChannelsOwn()
    {
        var channel = VoiceChannel();
        channel.Config[VoiceConversationService.GreetingKey] = "Hola, soy Ana de la tienda.";
        var service = Service(channel, Mock.Of<IInboundMessageIngest>());

        var twiml = await service.GreetAsync(Tenant, "voice", CancellationToken.None);

        Assert.Contains("Hola, soy Ana de la tienda.</Say></Gather>", twiml);
    }

    [Fact]
    public async Task CallingACustomer_UsesTheirCanonicalNumber_AndPointsTwilioBackToTheConversation()
    {
        var channel = VoiceChannel();
        var adapter = new Mock<IVoiceCallProviderAdapter>();
        ProviderVoiceCallRequest? sent = null;
        adapter.SetupGet(x => x.ProviderId).Returns("twilio");
        adapter.Setup(x => x.PlaceCallAsync(It.IsAny<ProviderConnectionProfile>(), It.IsAny<ProviderVoiceCallRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderConnectionProfile, ProviderVoiceCallRequest, CancellationToken>((_, r, _) => sent = r)
            .ReturnsAsync(new ProviderVoiceCallResult { ProviderCallId = "CA9", ProviderStatus = "queued" });
        var providers = new Mock<IProviderResolver>();
        providers.Setup(x => x.ResolveRequiredAsync<IVoiceCallProviderAdapter>(
                It.Is<ProviderResolutionContext>(c => c.Capability == CommunicationCapabilities.CallOutbound && c.ConnectionId == "twilio-main"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedProviderAdapter<IVoiceCallProviderAdapter>(adapter.Object, new ProviderConnectionProfile
            {
                ConnectionId = "twilio-main", TenantId = Tenant, ProviderId = "twilio", ConnectorId = "twilio"
            }));
        var service = Service(channel, Mock.Of<IInboundMessageIngest>(), providers.Object);

        var result = await service.PlaceCallAsync(new VoiceCallRequest { TenantId = Tenant, PhoneNumber = "8114 3874" }, CancellationToken.None);

        Assert.Equal("CA9", result.ProviderCallId);
        Assert.Equal("+50581143874", sent!.PhoneNumber);
        Assert.Equal(TwilioVoiceTwiml.DefaultGreeting, sent.Script);
        Assert.Equal($"{PublicBase}/api/v1/tenants/{Tenant}/webhooks/twilio/voice/gather?channel=voice&silences=0&turn=0", sent.ConversationUrl);
        Assert.StartsWith($"{PublicBase}/api/v1/tenants/{Tenant}/webhooks/twilio/voice/answer?channel=voice&script=", sent.AnswerUrl);
        Assert.Equal(VoiceConversationService.MaxCallSeconds, sent.TimeLimitSeconds);
        Assert.Equal($"{PublicBase}/api/v1/tenants/{Tenant}/webhooks/twilio/voice/status?channel=voice", sent.StatusCallbackUrl);
    }

    [Fact]
    public async Task CallingAnInvalidNumber_IsRejectedBeforeTwilio()
    {
        var providers = new Mock<IProviderResolver>(MockBehavior.Strict);
        var service = Service(VoiceChannel(), Mock.Of<IInboundMessageIngest>(), providers.Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.PlaceCallAsync(new VoiceCallRequest { TenantId = Tenant, PhoneNumber = "hola" }, CancellationToken.None));
    }

    [Fact]
    public async Task AnInboundCall_IsKeptOnTheCallersSession_NotOnTheBusinessNumber()
    {
        var channel = VoiceChannel();
        string? keyedBy = null;
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        channelRepo.Setup(x => x.GetByTypeAsync(ChannelType.Voice, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync([channel]);
        var sessions = new Mock<IChannelSessionRepository>();
        sessions.Setup(x => x.GetByChannelAndIdentifierAsync(channel.Id, It.IsAny<string>(), Tenant, It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, id, _, _) => keyedBy = id)
            .ReturnsAsync((ChannelSession?)null);
        sessions.Setup(x => x.InsertAsync(It.IsAny<ChannelSession>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        sessions.Setup(x => x.UpdateAsync(It.IsAny<ChannelSession>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var orchestrator = new VoiceSessionOrchestrator(channelRepo.Object, sessions.Object, NullLogger<VoiceSessionOrchestrator>.Instance);

        var state = await orchestrator.HandleStatusCallbackAsync(new VoiceStatusCallbackRequest
        {
            TenantId = Tenant, ChannelKey = "voice", CallSid = "CA1", CallStatus = "ringing",
            From = "+50581143874", To = "+15005550006", Direction = "inbound"
        });

        Assert.Equal("+50581143874", keyedBy);
        Assert.Equal("+50581143874", state.Identifier);
    }

    [Fact]
    public async Task ANewCallOnTheCustomersSession_StartsANewCall()
    {
        var channel = VoiceChannel();
        var existing = ChannelSession.Create(Tenant, channel.Id, ChannelType.Voice, "+50581143874");
        existing.StartVoiceCall("CA-old", "+50581143874", "inbound", "in-progress");
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        channelRepo.Setup(x => x.GetByTypeAsync(ChannelType.Voice, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync([channel]);
        var sessions = new Mock<IChannelSessionRepository>();
        sessions.Setup(x => x.GetByChannelAndIdentifierAsync(channel.Id, "+50581143874", Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        sessions.Setup(x => x.UpdateAsync(existing, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var orchestrator = new VoiceSessionOrchestrator(channelRepo.Object, sessions.Object, NullLogger<VoiceSessionOrchestrator>.Instance);

        var state = await orchestrator.HandleStatusCallbackAsync(new VoiceStatusCallbackRequest
        {
            TenantId = Tenant, ChannelKey = "voice", CallSid = "CA-new", CallStatus = "ringing",
            From = "+15005550006", To = "+50581143874", Direction = "outbound-api", CustomerAddress = "+50581143874"
        });

        Assert.Equal("CA-new", state.CallId);
        Assert.Equal("ringing", state.SessionState);
    }

    [Fact]
    public async Task VoiceSessions_ArePersistedPerCaller_WithTheChannelsAssistant()
    {
        var channel = VoiceChannel();
        channel.Config["DefaultAgentId"] = "agent-sales";
        var sessions = new Mock<IChannelSessionRepository>();
        sessions.Setup(x => x.GetByChannelAndIdentifierAsync(channel.Id, "+50581143874", Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelSession?)null);
        sessions.Setup(x => x.InsertAsync(It.IsAny<ChannelSession>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var handler = new VoiceChannelHandler(sessions.Object);

        var session = await handler.GetOrCreateSessionAsync(
            ChannelContext.Create(ChannelType.Voice, channel.Id, "c1", "+50581143874", "Carlos"), channel);

        Assert.Equal("agent-sales", session.AgentId);
        Assert.Equal("+50581143874", session.Identifier);
        sessions.Verify(x => x.InsertAsync(session, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("machine_start", true)]
    [InlineData("machine_end_beep", true)]
    [InlineData("fax", true)]
    [InlineData("human", false)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public async Task AVoicemail_IsHungUpOn_AndAPersonHearsTheOpening(string? answeredBy, bool hangsUp)
    {
        var service = Service(VoiceChannel(), Mock.Of<IInboundMessageIngest>());

        var twiml = await service.AnsweredAsync(Tenant, "voice", "Hola, te llamamos de la tienda.", answeredBy, CancellationToken.None);

        if (hangsUp)
            Assert.Equal("<Response><Hangup/></Response>", twiml);
        else
            Assert.Contains("Hola, te llamamos de la tienda.</Say></Gather>", twiml);
    }

    [Fact]
    public async Task ACallThatGoesOnForever_IsClosed()
    {
        var ingest = new Mock<IInboundMessageIngest>(MockBehavior.Strict);
        var service = Service(VoiceChannel(), ingest.Object);

        var twiml = await service.AnswerAsync(new VoiceTurn
        {
            TenantId = Tenant, From = "+50581143874", SpeechResult = "deja un mensaje después de la señal", Turn = VoiceConversationService.MaxTurns
        }, CancellationToken.None);

        Assert.DoesNotContain("<Gather", twiml);
        Assert.Contains("<Hangup/>", twiml);
    }

    [Fact]
    public void TheCallForm_AsksTwilioToDetectVoicemail_AndReportsEveryStatus()
    {
        var form = TwilioVoiceProviderAdapter.BuildCallForm(new ProviderVoiceCallRequest
        {
            PhoneNumber = "+50581143874",
            Script = "Hola",
            AnswerUrl = $"{PublicBase}/answer",
            TimeLimitSeconds = 900
        }, "+19016608125", $"{PublicBase}/status");

        Assert.Contains(form, kv => kv is { Key: "Url", Value: PublicBase + "/answer" });
        Assert.Contains(form, kv => kv is { Key: "MachineDetection", Value: "Enable" });
        Assert.DoesNotContain(form, kv => kv.Key == "Twiml");
        Assert.Contains(form, kv => kv is { Key: "TimeLimit", Value: "900" });
        Assert.Equal(["initiated", "ringing", "answered", "completed"],
            form.Where(kv => kv.Key == "StatusCallbackEvent").Select(kv => kv.Value).ToArray());
    }

    private static ChannelDefinition VoiceChannel()
    {
        var channel = ChannelDefinition.Create(Tenant, "Voz", ChannelType.Voice);
        channel.Config["ConnectionId"] = "twilio-main";
        channel.Activate();
        return channel;
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Webhooks:PublicBaseUrl"] = PublicBase + "/",
            ["Messaging:DefaultCountryCode"] = "505"
        })
        .Build();

    private static VoiceConversationService Service(ChannelDefinition channel, IInboundMessageIngest ingest, IProviderResolver? providers = null)
    {
        var channels = new Mock<IChannelDefinitionRepository>();
        channels.Setup(x => x.GetByTypeAsync(ChannelType.Voice, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync([channel]);
        return new VoiceConversationService(channels.Object, ingest, providers ?? Mock.Of<IProviderResolver>(),
            NullLogger<VoiceConversationService>.Instance, Config());
    }
}
