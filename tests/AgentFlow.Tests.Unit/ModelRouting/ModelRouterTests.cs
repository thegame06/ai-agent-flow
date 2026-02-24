using AgentFlow.Abstractions;
using AgentFlow.ModelRouting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentFlow.Tests.Unit.ModelRouting;

public class ModelRouterTests
{
    private readonly InMemoryModelRegistry _registry;
    private readonly ModelRouter _router;

    public ModelRouterTests()
    {
        _registry = new InMemoryModelRegistry();
        _router = new ModelRouter(_registry, NullLogger<ModelRouter>.Instance);
    }

    [Fact]
    public async Task SelectModelAsync_DefaultHealthy_ReturnsDefault()
    {
        // Arrange
        var provider = new StubModelProvider("gpt-4", healthCheck: _ => Task.FromResult(true));
        _registry.Register(provider);

        var request = CreateRequest(defaultModelId: "gpt-4");

        // Act
        var result = await _router.SelectModelAsync(request);

        // Assert
        Assert.Equal("gpt-4", result.ModelId);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public async Task SelectModelAsync_DefaultUnhealthy_EntersPanicMode()
    {
        // Scenario: Primary model is dead due to outage. Secondary model is alive.
        // Expectation: Router should automatically failover to ANY healthy model.

        // Arrange
        var deadProvider = new StubModelProvider("gpt-4", healthCheck: _ => Task.FromResult(false)); // Health check fails!
        var aliveProvider = new StubModelProvider("llama-3-70b", healthCheck: _ => Task.FromResult(true));
        
        _registry.Register(deadProvider);
        _registry.Register(aliveProvider);

        var request = CreateRequest(defaultModelId: "gpt-4");

        // Act
        var result = await _router.SelectModelAsync(request);

        // Assert
        Assert.Equal("llama-3-70b", result.ModelId); // Should pick the healthy one
        Assert.True(result.IsFallback);
        Assert.Contains("Panic", result.Reason ?? "");
    }

    [Fact]
    public async Task SelectByTask_SpecificTaskHealthy_ReturnsTaskModel()
    {
        // Arrange
        var codeModel = new StubModelProvider("claude-3-opus", healthCheck: _ => Task.FromResult(true));
        var generalModel = new StubModelProvider("gpt-3.5", healthCheck: _ => Task.FromResult(true));

        _registry.Register(codeModel);
        _registry.Register(generalModel);

        var config = new ModelRoutingConfig
        {
            DefaultModelId = "gpt-3.5",
            Strategy = ModelRoutingStrategy.TaskBased,
            RoutingRules = new[]
            {
                new TaskRoutingRule { TaskType = "coding", ModelId = "claude-3-opus" }
            }
        };

        var request = new ModelRoutingRequest
        {
            TenantId = "test-tenant",
            AgentKey = "test-agent",
            Config = config,
            TaskType = "coding"
        };

        // Act
        var result = await _router.SelectModelAsync(request);

        // Assert
        Assert.Equal("claude-3-opus", result.ModelId);
    }

    private static ModelRoutingRequest CreateRequest(string defaultModelId)
    {
        return new ModelRoutingRequest
        {
            TenantId = "test-tenant",
            AgentKey = "test-agent",
            Config = new ModelRoutingConfig
            {
                DefaultModelId = defaultModelId,
                Strategy = ModelRoutingStrategy.Static
            }
        };
    }
}
