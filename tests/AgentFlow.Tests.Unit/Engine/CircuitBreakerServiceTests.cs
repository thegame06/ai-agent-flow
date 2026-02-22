using AgentFlow.Core.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentFlow.Tests.Unit.Engine;

public class CircuitBreakerServiceTests
{
    private readonly CircuitBreakerService _service;

    public CircuitBreakerServiceTests()
    {
        var config = CircuitBreakerConfig.Default;
        _service = new CircuitBreakerService(config, NullLogger<CircuitBreakerService>.Instance);
    }

    [Fact]
    public void CanDelegate_WithinDepthLimit_ReturnsAllowed()
    {
        // Arrange
        var executionId = "exec-123";
        var callDepth = 3; // Within limit of 5
        var tokensUsed = 10_000;
        var tokenBudget = 100_000;
        var startedAt = DateTimeOffset.UtcNow;

        // Act
        var result = _service.CanDelegate(executionId, callDepth, tokensUsed, tokenBudget, startedAt);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void CanDelegate_ExceedsMaxDepth_ReturnsTripped()
    {
        // Arrange
        var executionId = "exec-123";
        var callDepth = 5; // At limit, next would be 6
        var tokensUsed = 10_000;
        var tokenBudget = 100_000;
        var startedAt = DateTimeOffset.UtcNow;

        // Act
        var result = _service.CanDelegate(executionId, callDepth, tokensUsed, tokenBudget, startedAt);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal("MaxCallDepthExceeded", result.ErrorCode);
        Assert.Contains("Maximum call depth (5) exceeded", result.ErrorMessage);
    }

    // Test removed: Token budget validation is now handled by TokenBudgetService
    // (See TokenBudgetServiceTests.cs for token-related tests)

    [Fact]
    public void CanDelegate_MaxDurationExceeded_ReturnsTripped()
    {
        // Arrange
        var config = new CircuitBreakerConfig { MaxDuration = TimeSpan.FromSeconds(1) };
        var service = new CircuitBreakerService(config, NullLogger<CircuitBreakerService>.Instance);
        var executionId = "exec-123";
        var callDepth = 2;
        var tokensUsed = 10_000;
        var tokenBudget = 100_000;
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-2); // Started 2 seconds ago

        // Act
        var result = service.CanDelegate(executionId, callDepth, tokensUsed, tokenBudget, startedAt);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal("MaxDurationExceeded", result.ErrorCode);
    }

    [Fact]
    public void DetectCircularReference_WhenAgentInChain_ReturnsTrue()
    {
        // Arrange
        var callChain = new List<string> { "agent-a", "agent-b", "agent-c" };
        var targetAgent = "agent-b"; // Already in chain

        // Act
        var result = _service.DetectCircularReference(callChain, targetAgent);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DetectCircularReference_WhenAgentNotInChain_ReturnsFalse()
    {
        // Arrange
        var callChain = new List<string> { "agent-a", "agent-b", "agent-c" };
        var targetAgent = "agent-d"; // Not in chain

        // Act
        var result = _service.DetectCircularReference(callChain, targetAgent);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TrackExecutionStart_WithNoParent_CreatesRootState()
    {
        // Arrange
        var executionId = "exec-123";

        // Act
        _service.TrackExecutionStart(executionId, parentExecutionId: null);
        var state = _service.GetState(executionId);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(executionId, state.ExecutionId);
        Assert.Null(state.ParentExecutionId);
        Assert.Equal(1, state.TotalExecutionsInChain);
    }

    [Fact]
    public void TrackExecutionStart_WithParent_IncrementsExecutionCount()
    {
        // Arrange
        var parentId = "exec-parent";
        var childId = "exec-child";
        _service.TrackExecutionStart(parentId, parentExecutionId: null);

        // Act
        _service.TrackExecutionStart(childId, parentId);
        var childState = _service.GetState(childId);

        // Assert
        Assert.NotNull(childState);
        Assert.Equal(childId, childState.ExecutionId);
        Assert.Equal(parentId, childState.ParentExecutionId);
        Assert.Equal(2, childState.TotalExecutionsInChain); // Parent = 1, Child = 2
    }

    [Fact]
    public void TrackExecutionEnd_RemovesState()
    {
        // Arrange
        var executionId = "exec-123";
        _service.TrackExecutionStart(executionId, parentExecutionId: null);

        // Act
        _service.TrackExecutionEnd(executionId);
        var state = _service.GetState(executionId);

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public void CanDelegate_MaxTotalExecutionsExceeded_ReturnsTripped()
    {
        // Arrange
        var config = new CircuitBreakerConfig { MaxTotalExecutions = 3 };
        var service = new CircuitBreakerService(config, NullLogger<CircuitBreakerService>.Instance);
        
        // Create chain: root → child1 → child2 → child3 (4 total, exceeds limit of 3)
        service.TrackExecutionStart("exec-root", null);
        service.TrackExecutionStart("exec-child1", "exec-root");
        service.TrackExecutionStart("exec-child2", "exec-child1");
        service.TrackExecutionStart("exec-child3", "exec-child2");

        // Act
        var result = service.CanDelegate(
            "exec-child3", 
            3, 
            10_000, 
            100_000, 
            DateTimeOffset.UtcNow);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal("MaxTotalExecutionsExceeded", result.ErrorCode);
    }

    [Fact]
    public void PermissiveConfig_AllowsMoreDelegations()
    {
        // Arrange
        var config = CircuitBreakerConfig.Permissive; // Max depth = 10
        var service = new CircuitBreakerService(config, NullLogger<CircuitBreakerService>.Instance);

        // Act
        var result = service.CanDelegate(
            "exec-123", 
            8, // Would fail with default config (max 5)
            10_000, 
            100_000, 
            DateTimeOffset.UtcNow);

        // Assert
        Assert.True(result.IsAllowed); // Permissive allows up to depth 10
    }
}
