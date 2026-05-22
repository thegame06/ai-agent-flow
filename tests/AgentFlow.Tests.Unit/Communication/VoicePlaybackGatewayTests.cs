using AgentFlow.Abstractions;
using AgentFlow.Api.Voice;
using AgentFlow.Api.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Communication;

public class VoicePlaybackGatewayTests
{
    [Fact]
    public async Task HandleSynthesizedEventAsync_ResolvesProviderAndUpdatesCall()
    {
        var adapter = new Mock<IVoiceCallControlProviderAdapter>();
        adapter.SetupGet(x => x.ProviderId).Returns("twilio");
        adapter.SetupGet(x => x.Capabilities).Returns(
        [
            new ProviderCapabilityDescriptor
            {
                Name = CommunicationCapabilities.CallControl,
                Channel = "voice",
                Description = "test"
            }
        ]);
        adapter.Setup(x => x.UpdateCallAsync(
                It.IsAny<ProviderConnectionProfile>(),
                It.IsAny<ProviderVoiceCallControlRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderVoiceCallControlResult
            {
                ProviderCallId = "CA-1",
                ProviderStatus = "in-progress"
            });

        var resolver = new Mock<IProviderResolver>();
        resolver.Setup(x => x.ResolveRequiredAsync<IVoiceCallControlProviderAdapter>(
                It.IsAny<ProviderResolutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedProviderAdapter<IVoiceCallControlProviderAdapter>(
                adapter.Object,
                new ProviderConnectionProfile
                {
                    ConnectionId = "conn-1",
                    TenantId = "tenant-a",
                    ProviderId = "twilio",
                    ConnectorId = "twilio-voice"
                }));

        var services = new ServiceCollection();
        services.AddSingleton(resolver.Object);
        using var root = services.BuildServiceProvider();

        var gateway = new VoicePlaybackGateway(
            Mock.Of<IAgentEventTransport>(),
            root.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VoicePlaybackGateway>.Instance,
            Mock.Of<IWorkflowAuditService>());

        var evt = new AgentEvent
        {
            EventType = "connect.call.audio.synthesized",
            TenantId = "tenant-a",
            AgentKey = "voice-runtime",
            SessionId = "session-1",
            CorrelationId = "CA-1",
            Headers = new Dictionary<string, string>
            {
                ["provider"] = "twilio",
                ["channel"] = "voice"
            },
            Payload = "{}"
        };
        var synthesized = new AudioSynthesizedEvent
        {
            TenantId = "tenant-a",
            SessionId = "session-1",
            StreamId = "stream-1",
            ContentType = "text/plain",
            Payload = System.Text.Encoding.UTF8.GetBytes("hola mundo"),
            Text = "hola mundo",
            ProviderId = "openai"
        };

        await gateway.HandleSynthesizedEventAsync(evt, synthesized, CancellationToken.None);

        adapter.Verify(x => x.UpdateCallAsync(
                It.IsAny<ProviderConnectionProfile>(),
                It.Is<ProviderVoiceCallControlRequest>(r =>
                    r.CallId == "CA-1" &&
                    r.Twiml.Contains("<Say", StringComparison.OrdinalIgnoreCase) &&
                    r.Twiml.Contains("hola mundo", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleSynthesizedEventAsync_DuplicateEvent_IsIgnored()
    {
        var adapter = new Mock<IVoiceCallControlProviderAdapter>();
        adapter.SetupGet(x => x.ProviderId).Returns("twilio");
        adapter.SetupGet(x => x.Capabilities).Returns(
        [
            new ProviderCapabilityDescriptor
            {
                Name = CommunicationCapabilities.CallControl,
                Channel = "voice",
                Description = "test"
            }
        ]);
        adapter.Setup(x => x.UpdateCallAsync(
                It.IsAny<ProviderConnectionProfile>(),
                It.IsAny<ProviderVoiceCallControlRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderVoiceCallControlResult
            {
                ProviderCallId = "CA-1",
                ProviderStatus = "in-progress"
            });

        var resolver = new Mock<IProviderResolver>();
        resolver.Setup(x => x.ResolveRequiredAsync<IVoiceCallControlProviderAdapter>(
                It.IsAny<ProviderResolutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedProviderAdapter<IVoiceCallControlProviderAdapter>(
                adapter.Object,
                new ProviderConnectionProfile
                {
                    ConnectionId = "conn-1",
                    TenantId = "tenant-a",
                    ProviderId = "twilio",
                    ConnectorId = "twilio-voice"
                }));

        var services = new ServiceCollection();
        services.AddSingleton(resolver.Object);
        using var root = services.BuildServiceProvider();

        var gateway = new VoicePlaybackGateway(
            Mock.Of<IAgentEventTransport>(),
            root.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VoicePlaybackGateway>.Instance,
            Mock.Of<IWorkflowAuditService>());

        var evt = new AgentEvent
        {
            EventType = "connect.call.audio.synthesized",
            TenantId = "tenant-a",
            AgentKey = "voice-runtime",
            SessionId = "session-1",
            CorrelationId = "CA-1",
            Headers = new Dictionary<string, string> { ["provider"] = "twilio", ["channel"] = "voice" },
            Payload = "{}"
        };
        var synthesized = new AudioSynthesizedEvent
        {
            TenantId = "tenant-a",
            SessionId = "session-1",
            StreamId = "stream-1",
            ContentType = "text/plain",
            Payload = System.Text.Encoding.UTF8.GetBytes("hola"),
            Text = "hola",
            ProviderId = "openai"
        };

        await gateway.HandleSynthesizedEventAsync(evt, synthesized, CancellationToken.None);
        await gateway.HandleSynthesizedEventAsync(evt, synthesized, CancellationToken.None);

        adapter.Verify(x => x.UpdateCallAsync(
                It.IsAny<ProviderConnectionProfile>(),
                It.IsAny<ProviderVoiceCallControlRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleSynthesizedEventAsync_WithAudioPlaybackUrl_UsesPlayTwiml()
    {
        var adapter = new Mock<IVoiceCallControlProviderAdapter>();
        adapter.SetupGet(x => x.ProviderId).Returns("twilio");
        adapter.SetupGet(x => x.Capabilities).Returns(
        [
            new ProviderCapabilityDescriptor
            {
                Name = CommunicationCapabilities.CallControl,
                Channel = "voice",
                Description = "test"
            }
        ]);
        adapter.Setup(x => x.UpdateCallAsync(
                It.IsAny<ProviderConnectionProfile>(),
                It.IsAny<ProviderVoiceCallControlRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderVoiceCallControlResult
            {
                ProviderCallId = "CA-1",
                ProviderStatus = "in-progress"
            });

        var resolver = new Mock<IProviderResolver>();
        resolver.Setup(x => x.ResolveRequiredAsync<IVoiceCallControlProviderAdapter>(
                It.IsAny<ProviderResolutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedProviderAdapter<IVoiceCallControlProviderAdapter>(
                adapter.Object,
                new ProviderConnectionProfile
                {
                    ConnectionId = "conn-1",
                    TenantId = "tenant-a",
                    ProviderId = "twilio",
                    ConnectorId = "twilio-voice"
                }));

        var services = new ServiceCollection();
        services.AddSingleton(resolver.Object);
        using var root = services.BuildServiceProvider();

        var gateway = new VoicePlaybackGateway(
            Mock.Of<IAgentEventTransport>(),
            root.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VoicePlaybackGateway>.Instance,
            Mock.Of<IWorkflowAuditService>());

        var evt = new AgentEvent
        {
            EventType = "connect.call.audio.synthesized",
            TenantId = "tenant-a",
            AgentKey = "voice-runtime",
            SessionId = "session-1",
            CorrelationId = "CA-1",
            Headers = new Dictionary<string, string>
            {
                ["provider"] = "twilio",
                ["channel"] = "voice",
                ["audioPlaybackUrl"] = "https://cdn.example.com/audio/abc.mp3"
            },
            Payload = "{}"
        };
        var synthesized = new AudioSynthesizedEvent
        {
            TenantId = "tenant-a",
            SessionId = "session-1",
            StreamId = "stream-1",
            ContentType = "audio/mpeg",
            Payload = [1, 2, 3],
            Text = "hola mundo",
            ProviderId = "openai"
        };

        await gateway.HandleSynthesizedEventAsync(evt, synthesized, CancellationToken.None);

        adapter.Verify(x => x.UpdateCallAsync(
                It.IsAny<ProviderConnectionProfile>(),
                It.Is<ProviderVoiceCallControlRequest>(r =>
                    r.CallId == "CA-1" &&
                    r.Twiml.Contains("<Play>", StringComparison.OrdinalIgnoreCase) &&
                    r.Twiml.Contains("https://cdn.example.com/audio/abc.mp3", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
