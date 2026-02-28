using AgentFlow.Abstractions;
using AgentFlow.Application.Channels;
using AgentFlow.Core.Engine;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Engine;

public sealed class ChannelGatewayTests
{
    [Fact]
    public async Task ProcessMessageAsync_UsesSessionOwnerAgent_ForStickyRouting()
    {
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        var sessionRepo = new Mock<IChannelSessionRepository>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        var executor = new Mock<IAgentExecutor>();
        var handoffExecutor = new Mock<IAgentHandoffExecutor>();
        var handoffPolicy = new Mock<IManagerHandoffPolicy>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");
        session.LinkAgent("manager-agent");

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync(session.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        sessionRepo.Setup(x => x.UpdateAsync(session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        executor.Setup(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult
            {
                ExecutionId = "exec-1",
                AgentKey = "manager-agent",
                AgentVersion = "v1",
                Status = ExecutionStatus.Completed,
                FinalResponse = "ok"
            });

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            new[] { new TestChannelHandler(ChannelType.Api) },
            NullLogger<ChannelGateway>.Instance);

        var incoming = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "hello");

        await gateway.ProcessMessageAsync(incoming, CancellationToken.None);

        executor.Verify(x => x.ExecuteAsync(
            It.Is<AgentExecutionRequest>(r => r.AgentKey == "manager-agent"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMessageAsync_UsesDefaultAgent_WhenSessionOwnerMissing()
    {
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        var sessionRepo = new Mock<IChannelSessionRepository>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        var executor = new Mock<IAgentExecutor>();
        var handoffExecutor = new Mock<IAgentHandoffExecutor>();
        var handoffPolicy = new Mock<IManagerHandoffPolicy>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync("missing-session", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelSession?)null);

        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        executor.Setup(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult
            {
                ExecutionId = "exec-2",
                AgentKey = "default-agent",
                AgentVersion = "v1",
                Status = ExecutionStatus.Completed,
                FinalResponse = "ok"
            });

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            new[] { new TestChannelHandler(ChannelType.Api) },
            NullLogger<ChannelGateway>.Instance);

        var incoming = ChannelMessage.CreateIncoming("tenant-1", channel.Id, "missing-session", "user-1", "hello");

        await gateway.ProcessMessageAsync(incoming, CancellationToken.None);

        executor.Verify(x => x.ExecuteAsync(
            It.Is<AgentExecutionRequest>(r => r.AgentKey == "default-agent"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMessageAsync_DelegatesViaHandoff_WhenManagerReturnsHandoffDirective()
    {
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        var sessionRepo = new Mock<IChannelSessionRepository>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        var executor = new Mock<IAgentExecutor>();
        var handoffExecutor = new Mock<IAgentHandoffExecutor>();
        var handoffPolicy = new Mock<IManagerHandoffPolicy>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");
        session.LinkAgent("manager-agent");

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync(session.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        sessionRepo.Setup(x => x.UpdateAsync(session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        executor.Setup(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult
            {
                ExecutionId = "exec-manager",
                AgentKey = "manager-agent",
                AgentVersion = "v1",
                Status = ExecutionStatus.Completed,
                FinalResponse = "{\"type\":\"handoff\",\"targetAgentId\":\"collections-bot\",\"intent\":\"collections_reminder\",\"payload\":{\"customerId\":\"C1\"}}"
            });

        handoffPolicy.Setup(x => x.IsAllowed("tenant-1", "manager-agent", "collections-bot")).Returns(true);
        handoffExecutor.Setup(x => x.ExecuteAsync(It.IsAny<AgentHandoffRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentHandoffResponse
            {
                Ok = true,
                ResultJson = "{\"message\":\"Delegated reply\"}",
                StatePatch = new Dictionary<string, string> { ["lastExecutionId"] = "exec-sub" }
            });

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            new[] { new TestChannelHandler(ChannelType.Api) },
            NullLogger<ChannelGateway>.Instance);

        var incoming = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "hello");

        var outgoing = await gateway.ProcessMessageAsync(incoming, CancellationToken.None);

        Assert.Equal("Delegated reply", outgoing.Content);
        handoffExecutor.Verify(x => x.ExecuteAsync(
            It.Is<AgentHandoffRequest>(h => h.TargetAgentKey == "collections-bot" && h.SourceAgentKey == "manager-agent"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class TestChannelHandler(ChannelType type) : IChannelHandler
    {
        public ChannelType SupportedChannelType => type;

        public Task<ChannelStatus> InitializeAsync(ChannelDefinition definition, CancellationToken ct = default)
            => Task.FromResult(ChannelStatus.Active);

        public Task ShutdownAsync(ChannelDefinition definition, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ChannelMessage?> ProcessIncomingMessageAsync(object rawMessage, ChannelDefinition definition, CancellationToken ct = default)
            => Task.FromResult<ChannelMessage?>(null);

        public Task<SendResult> SendReplyAsync(ChannelMessage message, ChannelDefinition definition, CancellationToken ct = default)
        {
            message.MarkSent();
            return Task.FromResult(SendResult.Ok(message.Id));
        }

        public ChannelContext ExtractContext(object rawMessage, ChannelDefinition definition)
            => ChannelContext.Create(type, definition.Id, "req", "user");

        public Task<ChannelSession> GetOrCreateSessionAsync(ChannelContext context, ChannelDefinition definition, CancellationToken ct = default)
            => Task.FromResult(ChannelSession.Create(definition.TenantId, definition.Id, type, context.UserIdentifier));

        public Task<HealthStatus> CheckHealthAsync(ChannelDefinition definition, CancellationToken ct = default)
            => Task.FromResult(HealthStatus.Ok());
    }
}
