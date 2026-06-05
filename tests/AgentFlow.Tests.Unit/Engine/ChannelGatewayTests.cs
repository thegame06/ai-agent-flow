using AgentFlow.Abstractions;
using AgentFlow.Application.Channels;
using AgentFlow.Application.Memory;
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
        var requestFactory = new Mock<IChannelExecutionRequestFactory>();
        var spamReputationRepo = new Mock<IChannelSpamReputationRepository>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");
        session.LinkAgent("manager-agent");
        session.Metadata["routing.guard.stage"] = "classified";

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync(session.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        sessionRepo.Setup(x => x.UpdateAsync(session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.GetBySessionAsync(session.Id, "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChannelMessage>());

        executor.Setup(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult
            {
                ExecutionId = "exec-1",
                AgentKey = "manager-agent",
                AgentVersion = "v1",
                Status = ExecutionStatus.Completed,
                FinalResponse = "ok"
            });
        requestFactory.Setup(x => x.CreateAsync(It.IsAny<ChannelMessage>(), It.IsAny<ChannelDefinition>(), It.IsAny<ChannelSession?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelMessage incoming, ChannelDefinition _, ChannelSession? s, string a, CancellationToken _) =>
                BuildExecutionRequest(incoming, s, a));

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            requestFactory.Object,
            Mock.Of<IAgentDefinitionRepository>(),
            Mock.Of<IAuditMemory>(),
            Mock.Of<IChannelCapabilityPolicy>(),
            spamReputationRepo.Object,
            null,
            new[] { new TestChannelHandler(ChannelType.Api) },
            null,
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
        var requestFactory = new Mock<IChannelExecutionRequestFactory>();
        var spamReputationRepo = new Mock<IChannelSpamReputationRepository>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync("missing-session", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelSession?)null);

        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.GetBySessionAsync("missing-session", "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChannelMessage>());

        executor.Setup(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult
            {
                ExecutionId = "exec-2",
                AgentKey = "default-agent",
                AgentVersion = "v1",
                Status = ExecutionStatus.Completed,
                FinalResponse = "ok"
            });
        requestFactory.Setup(x => x.CreateAsync(It.IsAny<ChannelMessage>(), It.IsAny<ChannelDefinition>(), It.IsAny<ChannelSession?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelMessage incoming, ChannelDefinition _, ChannelSession? s, string a, CancellationToken _) =>
                BuildExecutionRequest(incoming, s, a));

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            requestFactory.Object,
            Mock.Of<IAgentDefinitionRepository>(),
            Mock.Of<IAuditMemory>(),
            Mock.Of<IChannelCapabilityPolicy>(),
            spamReputationRepo.Object,
            null,
            new[] { new TestChannelHandler(ChannelType.Api) },
            null,
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
        var requestFactory = new Mock<IChannelExecutionRequestFactory>();
        var spamReputationRepo = new Mock<IChannelSpamReputationRepository>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");
        session.LinkAgent("manager-agent");
        session.Metadata["routing.guard.stage"] = "classified";

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync(session.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        sessionRepo.Setup(x => x.UpdateAsync(session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.GetBySessionAsync(session.Id, "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChannelMessage>());

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
                SessionId = session.Id,
                ThreadId = session.Id,
                CorrelationId = session.Id,
                Ok = true,
                ResultJson = "{\"message\":\"Delegated reply\"}",
                StatePatch = new Dictionary<string, string> { ["lastExecutionId"] = "exec-sub" }
            });
        requestFactory.Setup(x => x.CreateAsync(It.IsAny<ChannelMessage>(), It.IsAny<ChannelDefinition>(), It.IsAny<ChannelSession?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelMessage incoming, ChannelDefinition _, ChannelSession? s, string a, CancellationToken _) =>
                BuildExecutionRequest(incoming, s, a));

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            requestFactory.Object,
            Mock.Of<IAgentDefinitionRepository>(),
            Mock.Of<IAuditMemory>(),
            Mock.Of<IChannelCapabilityPolicy>(),
            spamReputationRepo.Object,
            null,
            new[] { new TestChannelHandler(ChannelType.Api) },
            null,
            NullLogger<ChannelGateway>.Instance);

        var incoming = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "hello");

        var outgoing = await gateway.ProcessMessageAsync(incoming, CancellationToken.None);

        Assert.Equal("Delegated reply", outgoing.Content);
        handoffExecutor.Verify(x => x.ExecuteAsync(
            It.Is<AgentHandoffRequest>(h => h.TargetAgentKey == "collections-bot" && h.SourceAgentKey == "manager-agent"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessMessageAsync_BlocksLowSignalInboundBeforeExecutor()
    {
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        var sessionRepo = new Mock<IChannelSessionRepository>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        var executor = new Mock<IAgentExecutor>();
        var handoffExecutor = new Mock<IAgentHandoffExecutor>();
        var handoffPolicy = new Mock<IManagerHandoffPolicy>();
        var requestFactory = new Mock<IChannelExecutionRequestFactory>();
        var auditMemory = new Mock<IAuditMemory>();
        var spamReputationRepo = new Mock<IChannelSpamReputationRepository>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        channel.UpdateConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EscalationTarget"] = "ventas-n1",
            ["MinMessagesBeforeClassification"] = "3"
        });
        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");
        session.LinkAgent("router-agent");

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync(session.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        sessionRepo.Setup(x => x.UpdateAsync(session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.UpdateAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.GetBySessionAsync(session.Id, "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChannelMessage>());

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            requestFactory.Object,
            Mock.Of<IAgentDefinitionRepository>(),
            auditMemory.Object,
            Mock.Of<IChannelCapabilityPolicy>(),
            spamReputationRepo.Object,
            null,
            new[] { new TestChannelHandler(ChannelType.Api) },
            null,
            NullLogger<ChannelGateway>.Instance);

        var incoming = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "c");

        var outgoing = await gateway.ProcessMessageAsync(incoming, CancellationToken.None);

        Assert.Equal(MessageDirection.Incoming, outgoing.Direction);
        Assert.Equal("suppressed", outgoing.Metadata["agentflow.delivery"]);
        executor.Verify(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        auditMemory.Verify(x => x.RecordAsync(
            It.Is<AuditEntry>(a => a.EventJson.Contains("inbound_guard_blocked")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenSessionAlreadyEscalated_RecordsInboundWithoutReply()
    {
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        var sessionRepo = new Mock<IChannelSessionRepository>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        var executor = new Mock<IAgentExecutor>();
        var handoffExecutor = new Mock<IAgentHandoffExecutor>();
        var handoffPolicy = new Mock<IManagerHandoffPolicy>();
        var requestFactory = new Mock<IChannelExecutionRequestFactory>();
        var auditMemory = new Mock<IAuditMemory>();
        var spamReputationRepo = new Mock<IChannelSpamReputationRepository>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        channel.UpdateConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EscalationTarget"] = "ventas-n1"
        });

        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");
        session.LinkAgent("router-agent");
        session.Metadata["routing.fallback.state"] = "escalated_human";
        session.Metadata["routing.fallback.escalation_status"] = "delivered";

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync(session.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        sessionRepo.Setup(x => x.UpdateAsync(session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.UpdateAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.GetBySessionAsync(session.Id, "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChannelMessage>());

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            requestFactory.Object,
            Mock.Of<IAgentDefinitionRepository>(),
            auditMemory.Object,
            Mock.Of<IChannelCapabilityPolicy>(),
            spamReputationRepo.Object,
            null,
            new[] { new TestChannelHandler(ChannelType.Api) },
            null,
            NullLogger<ChannelGateway>.Instance);

        var incoming = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "sigo escribiendo");

        var result = await gateway.ProcessMessageAsync(incoming, CancellationToken.None);

        Assert.Equal(MessageDirection.Incoming, result.Direction);
        Assert.Equal("suppressed", result.Metadata["agentflow.delivery"]);
        Assert.Equal("inbox_only", result.Metadata["agentflow.visibility"]);
        executor.Verify(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        auditMemory.Verify(x => x.RecordAsync(
            It.Is<AuditEntry>(a => a.EventJson.Contains("session_already_escalated")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessMessageAsync_AccumulatesContextBeforeFirstClassification()
    {
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        var sessionRepo = new Mock<IChannelSessionRepository>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        var executor = new Mock<IAgentExecutor>();
        var handoffExecutor = new Mock<IAgentHandoffExecutor>();
        var handoffPolicy = new Mock<IManagerHandoffPolicy>();
        var requestFactory = new Mock<IChannelExecutionRequestFactory>();
        var spamReputationRepo = new Mock<IChannelSpamReputationRepository>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        channel.UpdateConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MinMessagesBeforeClassification"] = "3",
            ["HistoryWindowMessagesForClassification"] = "3"
        });

        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");
        var earlier = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "Hola");

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync(session.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        sessionRepo.Setup(x => x.UpdateAsync(session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.UpdateAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.GetBySessionAsync(session.Id, "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "Me pueden dar informacion?"), earlier });

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            requestFactory.Object,
            Mock.Of<IAgentDefinitionRepository>(),
            Mock.Of<IAuditMemory>(),
            Mock.Of<IChannelCapabilityPolicy>(),
            spamReputationRepo.Object,
            null,
            new[] { new TestChannelHandler(ChannelType.Api) },
            null,
            NullLogger<ChannelGateway>.Instance);

        var incoming = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "Me pueden dar informacion?");

        var result = await gateway.ProcessMessageAsync(incoming, CancellationToken.None);

        Assert.Equal(MessageDirection.Incoming, result.Direction);
        Assert.Equal("suppressed", result.Metadata["agentflow.delivery"]);
        executor.Verify(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessMessageAsync_PersistentSpamReputation_SkipsRouting()
    {
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        var sessionRepo = new Mock<IChannelSessionRepository>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        var executor = new Mock<IAgentExecutor>();
        var handoffExecutor = new Mock<IAgentHandoffExecutor>();
        var handoffPolicy = new Mock<IManagerHandoffPolicy>();
        var requestFactory = new Mock<IChannelExecutionRequestFactory>();
        var auditMemory = new Mock<IAuditMemory>();
        var spamReputationRepo = new Mock<IChannelSpamReputationRepository>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        channel.UpdateConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SpamEscalationTarget"] = "spam-review"
        });

        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");
        var reputation = ChannelSpamReputation.Create("tenant-1", channel.Id, "user-1");
        reputation.MarkSuspected("seed");
        spamReputationRepo.Setup(x => x.GetAsync("tenant-1", channel.Id, "user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reputation);

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync(session.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        sessionRepo.Setup(x => x.UpdateAsync(session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.UpdateAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            requestFactory.Object,
            Mock.Of<IAgentDefinitionRepository>(),
            auditMemory.Object,
            Mock.Of<IChannelCapabilityPolicy>(),
            spamReputationRepo.Object,
            null,
            new[] { new TestChannelHandler(ChannelType.Api) },
            null,
            NullLogger<ChannelGateway>.Instance);

        var incoming = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "hola");
        var result = await gateway.ProcessMessageAsync(incoming, CancellationToken.None);

        Assert.Equal("suppressed", result.Metadata["agentflow.delivery"]);
        executor.Verify(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        auditMemory.Verify(x => x.RecordAsync(
            It.Is<AuditEntry>(a => a.EventJson.Contains("spam_reputation_match")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessMessageAsync_UnclassifiedThresholdWithoutEscalationTarget_MarksPendingHumanReview()
    {
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        var sessionRepo = new Mock<IChannelSessionRepository>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        var executor = new Mock<IAgentExecutor>();
        var handoffExecutor = new Mock<IAgentHandoffExecutor>();
        var handoffPolicy = new Mock<IManagerHandoffPolicy>();
        var requestFactory = new Mock<IChannelExecutionRequestFactory>();
        var auditMemory = new Mock<IAuditMemory>();
        var spamReputationRepo = new Mock<IChannelSpamReputationRepository>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        channel.UpdateConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MinMessagesBeforeClassification"] = "3",
            ["MaxUnclassifiedMessagesBeforeEscalation"] = "4"
        });

        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");
        session.LinkAgent("router-agent");
        session.Metadata["routing.fallback.state"] = "no_match";
        session.Metadata["routing.guard.stage"] = "accumulating";

        var history = new[]
        {
            ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "hola"),
            ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "me ayudan"),
            ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "quiero informacion"),
            ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "si")
        };

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync(session.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        sessionRepo.Setup(x => x.UpdateAsync(session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.UpdateAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.GetBySessionAsync(session.Id, "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            requestFactory.Object,
            Mock.Of<IAgentDefinitionRepository>(),
            auditMemory.Object,
            Mock.Of<IChannelCapabilityPolicy>(),
            spamReputationRepo.Object,
            null,
            new[] { new TestChannelHandler(ChannelType.Api) },
            null,
            NullLogger<ChannelGateway>.Instance);

        var incoming = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "ok");
        var result = await gateway.ProcessMessageAsync(incoming, CancellationToken.None);

        Assert.Equal("suppressed", result.Metadata["agentflow.delivery"]);
        Assert.Equal("pending_human_review", session.Metadata["routing.fallback.state"]);
        Assert.Equal("pending_human_review", session.Metadata["routing.guard.stage"]);
        executor.Verify(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenSessionPendingHumanReview_RecordsInboundWithoutReply()
    {
        var channelRepo = new Mock<IChannelDefinitionRepository>();
        var sessionRepo = new Mock<IChannelSessionRepository>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        var executor = new Mock<IAgentExecutor>();
        var handoffExecutor = new Mock<IAgentHandoffExecutor>();
        var handoffPolicy = new Mock<IManagerHandoffPolicy>();
        var requestFactory = new Mock<IChannelExecutionRequestFactory>();
        var auditMemory = new Mock<IAuditMemory>();
        var spamReputationRepo = new Mock<IChannelSpamReputationRepository>();

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");
        session.LinkAgent("router-agent");
        session.Metadata["routing.fallback.state"] = "pending_human_review";
        session.Metadata["routing.guard.stage"] = "pending_human_review";

        channelRepo.Setup(x => x.GetByIdAsync(channel.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);
        sessionRepo.Setup(x => x.GetByIdAsync(session.Id, "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        sessionRepo.Setup(x => x.UpdateAsync(session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.InsertAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.UpdateAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageRepo.Setup(x => x.GetBySessionAsync(session.Id, "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChannelMessage>());

        var gateway = new ChannelGateway(
            channelRepo.Object,
            sessionRepo.Object,
            messageRepo.Object,
            executor.Object,
            handoffExecutor.Object,
            handoffPolicy.Object,
            requestFactory.Object,
            Mock.Of<IAgentDefinitionRepository>(),
            auditMemory.Object,
            Mock.Of<IChannelCapabilityPolicy>(),
            spamReputationRepo.Object,
            null,
            new[] { new TestChannelHandler(ChannelType.Api) },
            null,
            NullLogger<ChannelGateway>.Instance);

        var incoming = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "sigo escribiendo");
        var result = await gateway.ProcessMessageAsync(incoming, CancellationToken.None);

        Assert.Equal("suppressed", result.Metadata["agentflow.delivery"]);
        Assert.Equal("inbox_only", result.Metadata["agentflow.visibility"]);
        executor.Verify(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        auditMemory.Verify(x => x.RecordAsync(
            It.Is<AuditEntry>(a => a.EventJson.Contains("session_pending_human_review")),
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

    private static AgentExecutionRequest BuildExecutionRequest(ChannelMessage incoming, ChannelSession? session, string agentKey)
    {
        return new AgentExecutionRequest
        {
            TenantId = incoming.TenantId,
            AgentKey = agentKey,
            UserId = incoming.From,
            UserMessage = incoming.Content,
            ContextJson = "{}",
            CorrelationId = incoming.SessionId,
            ThreadId = session?.ThreadId,
            Priority = ExecutionPriority.Normal,
            Metadata = new Dictionary<string, string>()
        };
    }
}
