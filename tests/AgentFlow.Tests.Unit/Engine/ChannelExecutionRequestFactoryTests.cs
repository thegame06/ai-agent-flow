using AgentFlow.Core.Engine;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Moq;

namespace AgentFlow.Tests.Unit.Engine;

public sealed class ChannelExecutionRequestFactoryTests
{
    [Fact]
    public async Task CreateAsync_MarksRoutingPoolAgentAsRouter_AndDisablesAssistantInferenceByDefault()
    {
        var store = new Mock<IIntentRoutingStore>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        store.Setup(x => x.GetRulesByChannelAsync("tenant-1", "api", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IntentRoutingRule>());
        messageRepo.Setup(x => x.GetBySessionAsync(It.IsAny<string>(), "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChannelMessage>());

        var tenantContext = new Mock<ITenantContextAccessor>();
        tenantContext.SetupGet(x => x.Current).Returns((TenantContext?)null);

        var factory = new ChannelExecutionRequestFactory(store.Object, tenantContext.Object, messageRepo.Object);

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        channel.UpdateConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IntentAgents"] = "router-pool-1,router-pool-2",
            ["NoMatchAction"] = "clarify_then_route"
        });

        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");
        var incoming = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "hola");

        var request = await factory.CreateAsync(
            incoming,
            channel,
            session,
            "router-pool-2",
            CancellationToken.None);

        Assert.Equal("true", request.Metadata["routing.is_router_agent"]);
        Assert.Equal("false", request.Metadata["routing.assistant_inference_enabled"]);
    }

    [Fact]
    public async Task CreateAsync_AggregatesInboundContext_WhenAccumulationThresholdIsReached()
    {
        var store = new Mock<IIntentRoutingStore>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        var tenantContext = new Mock<ITenantContextAccessor>();
        tenantContext.SetupGet(x => x.Current).Returns((TenantContext?)null);

        store.Setup(x => x.GetRulesByChannelAsync("tenant-1", "api", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IntentRoutingRule>());

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        channel.UpdateConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IntentAgents"] = "router-pool-1",
            ["HistoryWindowMessagesForClassification"] = "3",
            ["MinMessagesBeforeClassification"] = "3"
        });

        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");

        var m1 = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "Hola");
        var m2 = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "Me pueden dar informacion?");
        var m3 = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "Quiero comprar un celular");

        messageRepo.Setup(x => x.GetBySessionAsync(session.Id, "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { m3, m2, m1 });

        var factory = new ChannelExecutionRequestFactory(store.Object, tenantContext.Object, messageRepo.Object);

        var request = await factory.CreateAsync(
            m3,
            channel,
            session,
            "router-pool-1",
            CancellationToken.None);

        Assert.Equal("Hola\nMe pueden dar informacion?\nQuiero comprar un celular", request.UserMessage);
        Assert.Equal("3", request.Metadata["routing.inbound_message_count"]);
        Assert.Equal("true", request.Metadata["routing.accumulation_active"]);
        Assert.Equal("Quiero comprar un celular", request.Metadata["channel.latest_user_message"]);
    }

    [Fact]
    public async Task CreateAsync_AggregatesAvailableContext_BeforeConfiguredThreshold()
    {
        var store = new Mock<IIntentRoutingStore>();
        var messageRepo = new Mock<IChannelMessageRepository>();
        var tenantContext = new Mock<ITenantContextAccessor>();
        tenantContext.SetupGet(x => x.Current).Returns((TenantContext?)null);

        store.Setup(x => x.GetRulesByChannelAsync("tenant-1", "api", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IntentRoutingRule>());

        var channel = ChannelDefinition.Create("tenant-1", "api", ChannelType.Api);
        channel.UpdateConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IntentAgents"] = "router-pool-1",
            ["HistoryWindowMessagesForClassification"] = "3",
            ["MinMessagesBeforeClassification"] = "3"
        });

        var session = ChannelSession.Create("tenant-1", channel.Id, ChannelType.Api, "user-1");

        var m1 = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "Hola");
        var m2 = ChannelMessage.CreateIncoming("tenant-1", channel.Id, session.Id, "user-1", "Quiero comprar un celular");

        messageRepo.Setup(x => x.GetBySessionAsync(session.Id, "tenant-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { m2, m1 });

        var factory = new ChannelExecutionRequestFactory(store.Object, tenantContext.Object, messageRepo.Object);

        var request = await factory.CreateAsync(
            m2,
            channel,
            session,
            "router-pool-1",
            CancellationToken.None);

        Assert.Equal("Hola\nQuiero comprar un celular", request.UserMessage);
        Assert.Equal("2", request.Metadata["routing.inbound_message_count"]);
        Assert.Equal("true", request.Metadata["routing.accumulation_active"]);
    }
}
