using AgentFlow.Abstractions;
using AgentFlow.Api.Controllers;
using AgentFlow.Api.TestStudio;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AgentFlow.Tests.Unit.Api;

public sealed class TestStudioControllerTests
{
    [Fact]
    public async Task SendMessage_EnforcesSessionRateLimit()
    {
        var store = new InMemoryTestStudioSessionStore();
        var session = store.Create("tenant-a", AgentRuntimeKind.Voice, "call-1", "direct", "voice");
        var controller = CreateController(store, "tenant-a");

        IActionResult? last = null;
        for (var i = 0; i < 31; i++)
        {
            last = await controller.SendMessage(
                "tenant-a",
                session.TestSessionId,
                new TestStudioSendMessageRequest { Content = $"msg-{i}" },
                CancellationToken.None);
        }

        var limited = Assert.IsType<ObjectResult>(last);
        Assert.Equal(429, limited.StatusCode);
    }

    [Fact]
    public void UpdateCorrelation_UpdatesSession()
    {
        var store = new InMemoryTestStudioSessionStore();
        var session = store.Create("tenant-a", AgentRuntimeKind.Voice, "call-old", "direct", "voice");
        var controller = CreateController(store, "tenant-a");

        var result = controller.UpdateCorrelation(
            "tenant-a",
            session.TestSessionId,
            new UpdateCorrelationRequest { CorrelationId = "call-new" });

        Assert.IsType<OkObjectResult>(result);
        var updated = store.Get("tenant-a", session.TestSessionId);
        Assert.NotNull(updated);
        Assert.Equal("call-new", updated!.CorrelationId);
    }

    private static TestStudioController CreateController(ITestStudioSessionStore store, string tenantId)
    {
        var tenantContext = new Mock<ITenantContextAccessor>();
        tenantContext.SetupGet(x => x.Current).Returns(new TenantContext
        {
            TenantId = tenantId,
            UserId = "user-a",
            Roles = new[] { "developer" },
            Permissions = AgentFlowRoles.Developer.ToList()
        });

        return new TestStudioController(
            tenantContext.Object,
            store,
            Mock.Of<IAgentDefinitionRepository>(),
            Mock.Of<IAgentExecutor>(),
            Mock.Of<IConversationThreadRepository>(),
            Mock.Of<IChannelDefinitionRepository>(),
            Mock.Of<IChannelGateway>());
    }
}
