using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ToolSDK = AgentFlow.ToolSDK;

namespace AgentFlow.Tests.Unit.Engine;

public class AgentAsToolPluginTests
{
    private readonly Mock<IAgentExecutor> _mockExecutor;
    private readonly CircuitBreakerService _circuitBreaker;
    private readonly TokenBudgetService _tokenBudget;
    private readonly AgentAsToolPlugin _plugin;

    public AgentAsToolPluginTests()
    {
        _mockExecutor = new Mock<IAgentExecutor>();
        _circuitBreaker = new CircuitBreakerService(
            CircuitBreakerConfig.Default, 
            NullLogger<CircuitBreakerService>.Instance);
        _tokenBudget = new TokenBudgetService(TokenBudgetConfig.Default);
        _plugin = new AgentAsToolPlugin(
            _mockExecutor.Object,
            _circuitBreaker,
            _tokenBudget,
            NullLogger<AgentAsToolPlugin>.Instance);
    }

    [Fact]
    public void Metadata_HasCorrectProperties()
    {
        // Act
        var metadata = _plugin.Metadata;

        // Assert
        Assert.Equal("agent-as-tool", metadata.Id);
        Assert.Equal("Delegate to Agent", metadata.Name);
        Assert.Equal("1.0.0", metadata.Version);
        Assert.Equal(ToolSDK.ToolRiskLevel.Medium, metadata.RiskLevel);
        Assert.Contains("delegation", metadata.Tags);
    }

    [Fact]
    public void GetSchema_ReturnsValidSchema()
    {
        // Act
        var schema = _plugin.GetSchema();

        // Assert
        Assert.Contains("agentKey", schema.Parameters.Keys);
        Assert.Contains("message", schema.Parameters.Keys);
        Assert.Contains("variables", schema.Parameters.Keys);
        Assert.Contains("agentKey", schema.Required);
        Assert.Contains("message", schema.Required);
    }

    [Fact]
    public void RequiredPolicies_IncludesAgentDelegation()
    {
        // Act
        var policies = _plugin.RequiredPolicies;

        // Assert
        Assert.Single(policies);
        Assert.Equal("agent-delegation", policies[0].PolicyGroupId);
        Assert.True(policies[0].IsMandatory);
    }

    [Fact]
    public void Capabilities_HasCorrectSettings()
    {
        // Act
        var capabilities = _plugin.Capabilities;

        // Assert
        Assert.True(capabilities.SupportsAsync);
        Assert.False(capabilities.IsCacheable);
        Assert.False(capabilities.RequiresNetwork);
        Assert.False(capabilities.IsReadOnly);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingAgentKey_ReturnsError()
    {
        // Arrange
        var context = new ToolSDK.ToolContext
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            ExecutionId = "exec-1",
            Parameters = new Dictionary<string, object>
            {
                ["message"] = "test message"
                // agentKey missing
            }
        };

        // Act
        var result = await _plugin.ExecuteAsync(context, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("InvalidParameter", result.ErrorCode);
        Assert.Contains("agentKey", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingMessage_ReturnsError()
    {
        // Arrange
        var context = new ToolSDK.ToolContext
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            ExecutionId = "exec-1",
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "test-agent"
                // message missing
            }
        };

        // Act
        var result = await _plugin.ExecuteAsync(context, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("InvalidParameter", result.ErrorCode);
        Assert.Contains("message", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulDelegation_ReturnsSuccess()
    {
        // Arrange
        var context = new ToolSDK.ToolContext
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            ExecutionId = "exec-parent",
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "child-agent",
                ["message"] = "Do something"
            },
            Metadata = new Dictionary<string, string>
            {
                ["CallDepth"] = "0",
                ["TokenBudget"] = "100000",
                ["TokensUsed"] = "5000",
                ["ExecutionStartedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["CorrelationId"] = "corr-123"
            }
        };

        var mockResult = new AgentExecutionResult
        {
            ExecutionId = "exec-child",
            AgentKey = "child-agent",
            AgentVersion = "1.0.0",
            Status = ExecutionStatus.Completed,
            FinalResponse = "Task completed",
            TotalSteps = 3,
            TotalTokensUsed = 1500,
            DurationMs = 2000
        };

        _mockExecutor
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult);

        // Act
        var result = await _plugin.ExecuteAsync(context, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        Assert.Contains("1500", result.Metadata["tokensUsed"]);
        Assert.Contains("1", result.Metadata["callDepth"]);
    }

    [Fact]
    public async Task ExecuteAsync_CircuitBreakerTrips_ReturnsError()
    {
        // Arrange - Set call depth at limit
        var context = new ToolSDK.ToolContext
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            ExecutionId = "exec-deep",
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "child-agent",
                ["message"] = "Do something"
            },
            Metadata = new Dictionary<string, string>
            {
                ["CallDepth"] = "5", // At max depth
                ["TokenBudget"] = "100000",
                ["TokensUsed"] = "5000",
                ["ExecutionStartedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        // Act
        var result = await _plugin.ExecuteAsync(context, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("MaxCallDepthExceeded", result.ErrorCode);
        Assert.Equal("reduce_complexity", result.SuggestedAction);
    }

    [Fact]
    public async Task ExecuteAsync_TokenBudgetExhausted_ReturnsError()
    {
        // Arrange
        var context = new ToolSDK.ToolContext
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            ExecutionId = "exec-expensive",
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "child-agent",
                ["message"] = "Do something"
            },
            Metadata = new Dictionary<string, string>
            {
                ["CallDepth"] = "2",
                ["TokenBudget"] = "10000",
                ["TokensUsed"] = "10000", // Budget exhausted
                ["ExecutionStartedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        // Act
        var result = await _plugin.ExecuteAsync(context, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Exhausted", result.ErrorCode);
        Assert.Equal("simplify_or_skip", result.SuggestedAction);
    }

    [Fact]
    public async Task ExecuteAsync_ChildAgentFails_ReturnsError()
    {
        // Arrange
        var context = new ToolSDK.ToolContext
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            ExecutionId = "exec-parent",
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "child-agent",
                ["message"] = "Do something"
            },
            Metadata = new Dictionary<string, string>
            {
                ["CallDepth"] = "0",
                ["TokenBudget"] = "100000",
                ["TokensUsed"] = "5000",
                ["ExecutionStartedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        var mockResult = new AgentExecutionResult
        {
            ExecutionId = "exec-child",
            AgentKey = "child-agent",
            AgentVersion = "1.0.0",
            Status = ExecutionStatus.Failed,
            ErrorCode = "ToolExecutionFailed",
            ErrorMessage = "Tool failed to execute",
            TotalSteps = 1,
            TotalTokensUsed = 500,
            DurationMs = 1000
        };

        _mockExecutor
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult);

        // Act
        var result = await _plugin.ExecuteAsync(context, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("failed", result.ErrorMessage);
        Assert.Equal("retry_or_escalate", result.SuggestedAction);
    }

    [Fact]
    public async Task ExecuteAsync_PassesCorrectParentExecutionId()
    {
        // Arrange
        var parentExecutionId = "exec-parent-123";
        var context = new ToolSDK.ToolContext
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            ExecutionId = parentExecutionId,
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "child-agent",
                ["message"] = "Do something"
            },
            Metadata = new Dictionary<string, string>
            {
                ["CallDepth"] = "0",
                ["TokenBudget"] = "100000",
                ["TokensUsed"] = "0",
                ["ExecutionStartedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        var mockResult = new AgentExecutionResult
        {
            ExecutionId = "exec-child",
            AgentKey = "child-agent",
            AgentVersion = "1.0.0",
            Status = ExecutionStatus.Completed,
            FinalResponse = "Done",
            TotalSteps = 1,
            TotalTokensUsed = 500,
            DurationMs = 1000
        };

        AgentExecutionRequest capturedRequest = null!;
        _mockExecutor
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentExecutionRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(mockResult);

        // Act
        await _plugin.ExecuteAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal(parentExecutionId, capturedRequest.ParentExecutionId);
    }

    [Fact]
    public async Task ExecuteAsync_IncrementsCallDepth()
    {
        // Arrange
        var context = new ToolSDK.ToolContext
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            ExecutionId = "exec-parent",
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "child-agent",
                ["message"] = "Do something"
            },
            Metadata = new Dictionary<string, string>
            {
                ["CallDepth"] = "2", // Parent at depth 2
                ["TokenBudget"] = "100000",
                ["TokensUsed"] = "5000",
                ["ExecutionStartedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        var mockResult = new AgentExecutionResult
        {
            ExecutionId = "exec-child",
            AgentKey = "child-agent",
            AgentVersion = "1.0.0",
            Status = ExecutionStatus.Completed,
            FinalResponse = "Done",
            TotalSteps = 1,
            TotalTokensUsed = 500,
            DurationMs = 1000
        };

        AgentExecutionRequest capturedRequest = null!;
        _mockExecutor
            .Setup(x => x.ExecuteAsync(It.IsAny<AgentExecutionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentExecutionRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(mockResult);

        // Act
        await _plugin.ExecuteAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal(3, capturedRequest.CallDepth); // Should be parent depth + 1
    }
}
