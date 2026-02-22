using AgentFlow.Core.Engine;
using Xunit;

namespace AgentFlow.Tests.Unit.Engine;

public class TokenBudgetServiceTests
{
    private readonly TokenBudgetService _service;

    public TokenBudgetServiceTests()
    {
        var config = TokenBudgetConfig.Default;
        _service = new TokenBudgetService(config);
    }

    [Fact]
    public void CanProceed_WithSufficientBudget_ReturnsTrue()
    {
        // Arrange
        var budgetRemaining = 50_000;
        var estimatedCost = 10_000;

        // Act
        var result = _service.CanProceed(budgetRemaining, estimatedCost);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanProceed_WithInsufficientBudget_ReturnsFalse()
    {
        // Arrange
        var budgetRemaining = 5_000;
        var estimatedCost = 10_000; // Cost exceeds remaining budget

        // Act
        var result = _service.CanProceed(budgetRemaining, estimatedCost);

        // Assert
        Assert.False(result); // Even with safety margin (90%), 5k * 0.9 = 4.5k < 10k
    }

    [Fact]
    public void CanProceed_WithZeroBudget_ReturnsFalse()
    {
        // Arrange
        var budgetRemaining = 0;
        var estimatedCost = 1;

        // Act
        var result = _service.CanProceed(budgetRemaining, estimatedCost);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CalculateRemaining_WithUsage_ReturnsCorrectValue()
    {
        // Arrange
        var totalBudget = 100_000;
        var tokensUsed = 30_000;

        // Act
        var remaining = _service.CalculateRemaining(totalBudget, tokensUsed);

        // Assert
        Assert.Equal(70_000, remaining);
    }

    [Fact]
    public void CalculateRemaining_WhenExceeded_ReturnsZero()
    {
        // Arrange
        var totalBudget = 100_000;
        var tokensUsed = 150_000; // Over budget

        // Act
        var remaining = _service.CalculateRemaining(totalBudget, tokensUsed);

        // Assert
        Assert.Equal(0, remaining); // Should not go negative
    }

    [Fact]
    public void AllocateToChild_DividesRemainingBudgetEqually()
    {
        // Arrange
        var totalBudget = 100_000;
        var tokensUsed = 10_000;
        var expectedChildren = 3;

        // Act
        var allocation = _service.AllocateToChild(totalBudget, tokensUsed, expectedChildren);

        // Assert
        // Remaining: 90,000 / 3 children = 30,000 per child
        Assert.Equal(30_000, allocation);
    }

    [Fact]
    public void AllocateToChild_WithMinimumBudget_ReturnsMinimum()
    {
        // Arrange
        var totalBudget = 5_000;
        var tokensUsed = 4_500; // Only 500 remaining
        var expectedChildren = 10; // Would allocate 50 per child

        // Act
        var allocation = _service.AllocateToChild(totalBudget, tokensUsed, expectedChildren);

        // Assert
        // Should return minimum budget (1000) instead of calculated 50
        Assert.Equal(1_000, allocation);
    }

    [Fact]
    public void EstimateCost_ReturnsReasonableEstimate()
    {
        // Arrange
        var agentKey = "test-agent";
        var message = "This is a test message with some words"; // ~10 words, ~40 chars

        // Act
        var estimate = _service.EstimateCost(agentKey, message);

        // Assert
        // Base cost (500) + message cost (~40/4 = 10) = ~510
        Assert.InRange(estimate, 500, 600);
    }

    [Fact]
    public void Validate_WithSufficientBudget_ReturnsSufficient()
    {
        // Arrange
        var totalBudget = 100_000;
        var tokensUsed = 10_000;
        var estimatedNext = 5_000;

        // Act
        var result = _service.Validate(totalBudget, tokensUsed, estimatedNext);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(TokenBudgetStatus.Sufficient, result.Status);
        Assert.Equal(90_000, result.RemainingTokens);
    }

    [Fact]
    public void Validate_WithExhaustedBudget_ReturnsExhausted()
    {
        // Arrange
        var totalBudget = 100_000;
        var tokensUsed = 100_000; // Fully consumed
        var estimatedNext = 1_000;

        // Act
        var result = _service.Validate(totalBudget, tokensUsed, estimatedNext);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(TokenBudgetStatus.Exhausted, result.Status);
        Assert.Contains("fully consumed", result.ErrorMessage);
    }

    [Fact]
    public void Validate_WithInsufficientBudget_ReturnsInsufficient()
    {
        // Arrange
        var totalBudget = 100_000;
        var tokensUsed = 95_000; // 5k remaining
        var estimatedNext = 10_000; // Requires more than available

        // Act
        var result = _service.Validate(totalBudget, tokensUsed, estimatedNext);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(TokenBudgetStatus.Insufficient, result.Status);
        Assert.Contains("Insufficient tokens", result.ErrorMessage);
    }

    [Fact]
    public void GetDefaultBudget_ReturnsConfiguredValue()
    {
        // Act
        var defaultBudget = _service.GetDefaultBudget();

        // Assert
        Assert.Equal(100_000, defaultBudget);
    }

    [Fact]
    public void PermissiveConfig_AllowsLargerBudgets()
    {
        // Arrange
        var config = TokenBudgetConfig.Permissive; // 500k default budget
        var service = new TokenBudgetService(config);

        // Act
        var defaultBudget = service.GetDefaultBudget();

        // Assert
        Assert.Equal(500_000, defaultBudget);
    }

    [Fact]
    public void SafetyMargin_PreventsExactBoundaryFailures()
    {
        // Arrange
        var budgetRemaining = 10_000;
        var estimatedCost = 9_500; // Very close to remaining

        // Act
        var canProceed = _service.CanProceed(budgetRemaining, estimatedCost);

        // Assert
        // With 90% safety margin: 10,000 * 0.9 = 9,000
        // 9,000 < 9,500 → Cannot proceed
        Assert.False(canProceed);
    }

    [Theory]
    [InlineData(100_000, 0, 1, 100_000)] // No usage, 1 child → full budget
    [InlineData(100_000, 50_000, 1, 50_000)] // Half used, 1 child → remaining half
    [InlineData(100_000, 50_000, 2, 25_000)] // Half used, 2 children → split remaining
    [InlineData(100_000, 90_000, 5, 2_000)] // Little remaining, 5 children → split
    [InlineData(100_000, 99_000, 10, 1_000)] // Almost exhausted → returns minimum (1000)
    public void AllocateToChild_VariousScenarios_ReturnsExpectedAllocation(
        int totalBudget, 
        int tokensUsed, 
        int childrenCount, 
        int expectedAllocation)
    {
        // Act
        var allocation = _service.AllocateToChild(totalBudget, tokensUsed, childrenCount);

        // Assert
        Assert.Equal(expectedAllocation, allocation);
    }
}
