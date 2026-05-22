using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Communication;

public class VoiceSessionOrchestratorTests
{
    [Fact]
    public async Task HandleStatusCallbackAsync_CreatesSessionWhenMissing()
    {
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        channelRepo.Setup(x => x.GetByTypeAsync(ChannelType.Voice, "tenant-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateActiveVoiceChannel("tenant-a")
            ]);

        var sessionRepo = new Mock<IChannelSessionRepository>();
        sessionRepo.Setup(x => x.GetByChannelAndIdentifierAsync(It.IsAny<string>(), "+15550001111", "tenant-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelSession?)null);
        sessionRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelSession>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        sessionRepo.Setup(x => x.UpdateAsync(It.IsAny<ChannelSession>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var orchestrator = new VoiceSessionOrchestrator(channelRepo.Object, sessionRepo.Object, NullLogger<VoiceSessionOrchestrator>.Instance);

        var session = await orchestrator.HandleStatusCallbackAsync(new VoiceStatusCallbackRequest
        {
            TenantId = "tenant-a",
            ChannelKey = "voice",
            CallSid = "CA123",
            CallStatus = "ringing",
            To = "+15550001111"
        });

        Assert.Equal("CA123", session.CallId);
        Assert.Equal("ringing", session.ProviderStatus);
        Assert.Equal("ringing", session.SessionState);
        Assert.False(session.Closed);
        sessionRepo.Verify(x => x.InsertAsync(It.IsAny<ChannelSession>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleStatusCallbackAsync_ClosesSessionOnCompleted()
    {
        var channel = CreateActiveVoiceChannel("tenant-a");
        var existing = ChannelSession.Create("tenant-a", channel.Id, ChannelType.Voice, "+15550001111");
        existing.StartVoiceCall("CA999", "+15550001111", "outbound-api", "in-progress");

        var channelRepo = new Mock<IChannelDefinitionRepository>();
        channelRepo.Setup(x => x.GetByTypeAsync(ChannelType.Voice, "tenant-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync([channel]);

        var sessionRepo = new Mock<IChannelSessionRepository>();
        sessionRepo.Setup(x => x.GetByChannelAndIdentifierAsync(channel.Id, "+15550001111", "tenant-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        sessionRepo.Setup(x => x.UpdateAsync(existing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var orchestrator = new VoiceSessionOrchestrator(channelRepo.Object, sessionRepo.Object, NullLogger<VoiceSessionOrchestrator>.Instance);

        var session = await orchestrator.HandleStatusCallbackAsync(new VoiceStatusCallbackRequest
        {
            TenantId = "tenant-a",
            ChannelKey = "voice",
            CallSid = "CA999",
            CallStatus = "completed",
            To = "+15550001111",
            CallDuration = "24"
        });

        Assert.True(session.Closed);
        Assert.Equal("ended", session.SessionState);
        Assert.Equal("completed", session.ProviderStatus);
    }

    private static ChannelDefinition CreateActiveVoiceChannel(string tenantId)
    {
        var channel = ChannelDefinition.Create(tenantId, "Voice", ChannelType.Voice);
        channel.Activate();
        return channel;
    }
}
