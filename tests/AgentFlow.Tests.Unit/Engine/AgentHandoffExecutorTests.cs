using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;
using Moq;

namespace AgentFlow.Tests.Unit.Engine;

public sealed class AgentHandoffExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_MapsToAgentExecutionRequest()
    {
        var executor = new Mock<IAgentExecutor>();
        executor.Setup(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult
            {
                ExecutionId = "exec-123",
                AgentKey = "collections-bot",
                AgentVersion = "v1",
                Status = ExecutionStatus.Completed,
                FinalResponse = "{\"ok\":true}"
            });

        var handoff = new AgentHandoffExecutor(executor.Object);

        var response = await handoff.ExecuteAsync(new AgentHandoffRequest
        {
            TenantId = "tenant-1",
            SessionId = "sess-1",
            CorrelationId = "corr-1",
            SourceAgentKey = "manager-agent",
            TargetAgentKey = "collections-bot",
            Intent = "collections_reminder",
            PayloadJson = "{\"customerId\":\"c1\"}"
        }, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal("{\"ok\":true}", response.ResultJson);

        executor.Verify(x => x.ExecuteAsync(
            It.Is<AgentExecutionRequest>(r =>
                r.TenantId == "tenant-1" &&
                r.AgentKey == "collections-bot" &&
                r.SessionId == "sess-1" &&
                r.CorrelationId == "corr-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
