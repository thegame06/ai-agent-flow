using AgentFlow.Abstractions;
using AgentFlow.Api.Controllers;
using AgentFlow.Api.Voice;
using AgentFlow.Api.Workflow;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Communication;

public class TwilioVoiceWebhookControllerTests
{
    [Fact]
    public async Task ReceiveStatus_PublishesVoiceRuntimeEvent()
    {
        var eventTransport = new Mock<IAgentEventTransport>();
        eventTransport.Setup(x => x.PublishAsync(It.IsAny<AgentEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var audit = new Mock<IWorkflowAuditService>();
        audit.Setup(x => x.RecordStudioActionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var orchestrator = new Mock<IVoiceSessionOrchestrator>();
        orchestrator.Setup(x => x.HandleStatusCallbackAsync(It.IsAny<VoiceStatusCallbackRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceSessionState
            {
                SessionId = "sess-1",
                ChannelId = "voice-1",
                ChannelType = "Voice",
                Identifier = "+15550001111",
                CallId = "CA111",
                ProviderStatus = "ringing",
                SessionState = "active",
                Closed = false
            });
        var runtimeRegistry = new Mock<IAgentRuntimeRegistry>();
        var signatureValidator = new Mock<ITwilioWebhookSignatureValidator>();
        signatureValidator.Setup(x => x.IsValidAsync("tenant-a", It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = new TwilioVoiceWebhookController(
            eventTransport.Object,
            audit.Object,
            orchestrator.Object,
            runtimeRegistry.Object,
            signatureValidator.Object,
            NullLogger<TwilioVoiceWebhookController>.Instance);

        var result = await controller.ReceiveStatus(
            "tenant-a",
            new TwilioVoiceStatusForm
            {
                CallSid = "CA111",
                CallStatus = "ringing",
                To = "+15550001111"
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        eventTransport.Verify(x => x.PublishAsync(
            It.Is<AgentEvent>(e => e.AgentKey == "voice-runtime" && e.EventType == "connect.call.status.ringing"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceiveIncoming_ReturnsTwimlAndPublishesCallReceived()
    {
        var eventTransport = new Mock<IAgentEventTransport>();
        eventTransport.Setup(x => x.PublishAsync(It.IsAny<AgentEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var audit = new Mock<IWorkflowAuditService>();
        audit.Setup(x => x.RecordStudioActionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var orchestrator = new Mock<IVoiceSessionOrchestrator>();
        orchestrator.Setup(x => x.HandleStatusCallbackAsync(It.IsAny<VoiceStatusCallbackRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceSessionState
            {
                SessionId = "sess-2",
                ChannelId = "voice-1",
                ChannelType = "Voice",
                Identifier = "+15550002222",
                CallId = "CA222",
                ProviderStatus = "initiated",
                SessionState = "active",
                Closed = false
            });
        var runtime = new Mock<IAgentRuntime>();
        runtime.SetupGet(x => x.Kind).Returns(AgentRuntimeKind.Voice);
        runtime.Setup(x => x.ExecuteAsync(It.IsAny<AgentRuntimeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRuntimeResult
            {
                RuntimeKind = AgentRuntimeKind.Voice,
                Status = ExecutionStatus.Completed,
                SessionId = "sess-2",
                Response = "Hola desde runtime de voz."
            });
        var runtimeRegistry = new Mock<IAgentRuntimeRegistry>();
        runtimeRegistry.Setup(x => x.GetRequired(AgentRuntimeKind.Voice)).Returns(runtime.Object);
        var signatureValidator = new Mock<ITwilioWebhookSignatureValidator>();
        signatureValidator.Setup(x => x.IsValidAsync("tenant-a", It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = new TwilioVoiceWebhookController(
            eventTransport.Object,
            audit.Object,
            orchestrator.Object,
            runtimeRegistry.Object,
            signatureValidator.Object,
            NullLogger<TwilioVoiceWebhookController>.Instance);

        var result = await controller.ReceiveIncoming(
            "tenant-a",
            new TwilioVoiceStatusForm
            {
                CallSid = "CA222",
                From = "+15550002222",
                Direction = "inbound"
            },
            CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("text/xml", content.ContentType);
        Assert.Contains("<Say", content.Content);

        eventTransport.Verify(x => x.PublishAsync(
            It.Is<AgentEvent>(e => e.AgentKey == "voice-runtime" && e.EventType == "connect.call.received"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceiveAudioChunk_PublishesChunkEvent()
    {
        var eventTransport = new Mock<IAgentEventTransport>();
        eventTransport.Setup(x => x.PublishAsync(It.IsAny<AgentEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var audit = new Mock<IWorkflowAuditService>();
        var orchestrator = new Mock<IVoiceSessionOrchestrator>();
        var runtimeRegistry = new Mock<IAgentRuntimeRegistry>();
        var signatureValidator = new Mock<ITwilioWebhookSignatureValidator>();
        signatureValidator.Setup(x => x.IsValidAsync("tenant-a", It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = new TwilioVoiceWebhookController(
            eventTransport.Object,
            audit.Object,
            orchestrator.Object,
            runtimeRegistry.Object,
            signatureValidator.Object,
            NullLogger<TwilioVoiceWebhookController>.Instance);

        var payload = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        var result = await controller.ReceiveAudioChunk(
            "tenant-a",
            new TwilioVoiceMediaForm
            {
                CallSid = "CA333",
                StreamSid = "MZ333",
                PayloadBase64 = payload,
                Track = "inbound"
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        eventTransport.Verify(x => x.PublishAsync(
            It.Is<AgentEvent>(e => e.EventType == "connect.call.audio.chunk" && e.AgentKey == "voice-runtime"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceiveStatus_InvalidSignature_ReturnsUnauthorized()
    {
        var eventTransport = new Mock<IAgentEventTransport>();
        var audit = new Mock<IWorkflowAuditService>();
        var orchestrator = new Mock<IVoiceSessionOrchestrator>();
        var runtimeRegistry = new Mock<IAgentRuntimeRegistry>();
        var signatureValidator = new Mock<ITwilioWebhookSignatureValidator>();
        signatureValidator.Setup(x => x.IsValidAsync("tenant-a", It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = new TwilioVoiceWebhookController(
            eventTransport.Object,
            audit.Object,
            orchestrator.Object,
            runtimeRegistry.Object,
            signatureValidator.Object,
            NullLogger<TwilioVoiceWebhookController>.Instance);

        var result = await controller.ReceiveStatus(
            "tenant-a",
            new TwilioVoiceStatusForm
            {
                CallSid = "CA401",
                CallStatus = "ringing"
            },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        eventTransport.Verify(x => x.PublishAsync(It.IsAny<AgentEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
