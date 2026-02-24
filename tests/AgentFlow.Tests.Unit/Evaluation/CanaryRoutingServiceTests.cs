using AgentFlow.Evaluation;
using Xunit;

namespace AgentFlow.Tests.Unit.Evaluation;

public sealed class CanaryRoutingServiceTests
{
    private readonly ICanaryRoutingService _service = new CanaryRoutingService();

    [Fact]
    public void NoCanary_ReturnsMainAgent()
    {
        var result = _service.SelectAgentForExecution(
            agentDefinitionId: "agent-main",
            canaryAgentId: null,
            canaryWeight: 0.0,
            requestId: "req-123");

        Assert.Equal("agent-main", result);
    }

    [Fact]
    public void CanaryWeight_Zero_ReturnsMainAgent()
    {
        var result = _service.SelectAgentForExecution(
            agentDefinitionId: "agent-main",
            canaryAgentId: "agent-canary",
            canaryWeight: 0.0,
            requestId: "req-123");

        Assert.Equal("agent-main", result);
    }

    [Fact]
    public void CanaryWeight_100Percent_ReturnsCanary()
    {
        var result = _service.SelectAgentForExecution(
            agentDefinitionId: "agent-main",
            canaryAgentId: "agent-canary",
            canaryWeight: 1.0,
            requestId: "req-123");

        Assert.Equal("agent-canary", result);
    }

    [Fact]
    public void CanaryWeight_10Percent_Distributes_Deterministically()
    {
        // Test multiple requests - same requestId should always return the same result
        var result1 = _service.SelectAgentForExecution(
            agentDefinitionId: "agent-main",
            canaryAgentId: "agent-canary",
            canaryWeight: 0.10,
            requestId: "req-123");

        var result2 = _service.SelectAgentForExecution(
            agentDefinitionId: "agent-main",
            canaryAgentId: "agent-canary",
            canaryWeight: 0.10,
            requestId: "req-123");

        Assert.Equal(result1, result2); // Deterministic
    }

    [Fact]
    public void CanaryWeight_10Percent_RoughlyDistributes()
    {
        // Test that over many requests, approximately 10% go to canary
        int canaryCount = 0;
        int totalRequests = 1000;

        for (int i = 0; i < totalRequests; i++)
        {
            var result = _service.SelectAgentForExecution(
                agentDefinitionId: "agent-main",
                canaryAgentId: "agent-canary",
                canaryWeight: 0.10,
                requestId: $"req-{i}");

            if (result == "agent-canary")
                canaryCount++;
        }

        double actualRatio = (double)canaryCount / totalRequests;

        // Should be roughly 10% ± 4% (statistical variance)
        Assert.InRange(actualRatio, 0.06, 0.14);
    }

    [Fact]
    public void IsCanaryActive_NoCanaryId_ReturnsFalse()
    {
        Assert.False(_service.IsCanaryActive(null, 0.5));
        Assert.False(_service.IsCanaryActive("", 0.5));
        Assert.False(_service.IsCanaryActive("  ", 0.5));
    }

    [Fact]
    public void IsCanaryActive_ZeroWeight_ReturnsFalse()
    {
        Assert.False(_service.IsCanaryActive("agent-canary", 0.0));
        Assert.False(_service.IsCanaryActive("agent-canary", -0.1));
    }

    [Fact]
    public void IsCanaryActive_ValidConfig_ReturnsTrue()
    {
        Assert.True(_service.IsCanaryActive("agent-canary", 0.10));
        Assert.True(_service.IsCanaryActive("agent-canary", 1.0));
    }

    [Fact]
    public void SelectWithRationale_IncludesAuditInfo()
    {
        var service = (CanaryRoutingService)_service;

        var decision = service.SelectWithRationale(
            agentDefinitionId: "agent-main",
            canaryAgentId: "agent-canary",
            canaryWeight: 0.10,
            requestId: "req-test-123");

        Assert.NotNull(decision);
        Assert.Equal(0.10, decision.CanaryWeight);
        Assert.NotEmpty(decision.Reason);
        Assert.NotEmpty(decision.RequestHash);
        Assert.Contains(decision.SelectedAgentId, new[] { "agent-main", "agent-canary" });
    }

    [Fact]
    public void SelectWithRationale_NoCanary_ExplainsWhy()
    {
        var service = (CanaryRoutingService)_service;

        var decision = service.SelectWithRationale(
            agentDefinitionId: "agent-main",
            canaryAgentId: null,
            canaryWeight: 0.0,
            requestId: "req-test");

        Assert.Equal("agent-main", decision.SelectedAgentId);
        Assert.False(decision.IsCanaryExecution);
        Assert.Equal("No canary configured", decision.Reason);
    }

    [Fact]
    public void SelectWithRationale_100Percent_ExplainsWhy()
    {
        var service = (CanaryRoutingService)_service;

        var decision = service.SelectWithRationale(
            agentDefinitionId: "agent-main",
            canaryAgentId: "agent-canary",
            canaryWeight: 1.0,
            requestId: "req-test");

        Assert.Equal("agent-canary", decision.SelectedAgentId);
        Assert.True(decision.IsCanaryExecution);
        Assert.Equal("Canary weight is 100%", decision.Reason);
    }
}
