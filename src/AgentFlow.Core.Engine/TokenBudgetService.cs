namespace AgentFlow.Core.Engine;

/// <summary>
/// Token Budget Service — Prevents cost explosion from recursive agent calls.
/// Manages token allocation and enforcement across agent delegation chains.
/// </summary>
public sealed class TokenBudgetService
{
    private readonly TokenBudgetConfig _config;

    public TokenBudgetService(TokenBudgetConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Check if there are sufficient tokens remaining to proceed with an operation.
    /// Includes a safety margin to prevent budget exhaustion mid-operation.
    /// </summary>
    public bool CanProceed(int budgetRemaining, int estimatedCost)
    {
        if (budgetRemaining <= 0) return false;

        // Safety margin: Reserve 10% buffer to avoid mid-operation exhaustion
        var safeRemaining = budgetRemaining * _config.SafetyMargin;
        return safeRemaining >= estimatedCost;
    }

    /// <summary>
    /// Calculate remaining token budget after current usage.
    /// </summary>
    public int CalculateRemaining(int totalBudget, int tokensUsedSoFar)
    {
        return Math.Max(0, totalBudget - tokensUsedSoFar);
    }

    /// <summary>
    /// Allocate token budget to child agent executions.
    /// Divides remaining budget fairly among expected children.
    /// </summary>
    public int AllocateToChild(
        int totalBudget,
        int tokensUsedSoFar,
        int expectedChildrenCount)
    {
        var remaining = CalculateRemaining(totalBudget, tokensUsedSoFar);
        var childrenCount = Math.Max(expectedChildrenCount, 1);

        // Divide remaining budget equally
        var allocation = remaining / childrenCount;

        // Ensure minimum viable budget per child
        return Math.Max(allocation, _config.MinimumChildBudget);
    }

    /// <summary>
    /// Calculate estimated token cost for an agent invocation.
    /// Based on historical data and agent complexity.
    /// </summary>
    public int EstimateCost(string agentKey, string message)
    {
        // Simple heuristic: Base cost + message length factor
        // In production, this would use historical averages from database
        var baseCost = _config.BaseAgentCost;
        var messageCost = message.Length / 4; // Rough approximation: 1 token ≈ 4 chars

        return baseCost + messageCost;
    }

    /// <summary>
    /// Validate that token budget is not exhausted.
    /// Returns validation result with detailed error if budget exceeded.
    /// </summary>
    public TokenBudgetResult Validate(
        int totalBudget,
        int tokensUsedSoFar,
        int estimatedNextCost)
    {
        var remaining = CalculateRemaining(totalBudget, tokensUsedSoFar);

        if (remaining <= 0)
        {
            return TokenBudgetResult.Exhausted(
                $"Token budget ({totalBudget}) fully consumed. Used: {tokensUsedSoFar}",
                new Dictionary<string, object>
                {
                    ["totalBudget"] = totalBudget,
                    ["tokensUsed"] = tokensUsedSoFar,
                    ["remaining"] = 0
                });
        }

        if (!CanProceed(remaining, estimatedNextCost))
        {
            return TokenBudgetResult.Insufficient(
                $"Insufficient tokens for next operation. Remaining: {remaining}, Required: {estimatedNextCost}",
                new Dictionary<string, object>
                {
                    ["totalBudget"] = totalBudget,
                    ["tokensUsed"] = tokensUsedSoFar,
                    ["remaining"] = remaining,
                    ["requiredForNext"] = estimatedNextCost,
                    ["shortfall"] = estimatedNextCost - remaining
                });
        }

        return TokenBudgetResult.Sufficient(remaining);
    }

    /// <summary>
    /// Get recommended token budget for root-level executions.
    /// </summary>
    public int GetDefaultBudget() => _config.DefaultRootBudget;
}

/// <summary>
/// Configuration for token budget management.
/// </summary>
public sealed record TokenBudgetConfig
{
    /// <summary>
    /// Default token budget for root-level agent executions.
    /// Default: 100,000 tokens (~75,000 words).
    /// </summary>
    public int DefaultRootBudget { get; init; } = 100_000;

    /// <summary>
    /// Minimum token budget allocated to child agents.
    /// Prevents degenerate cases where child gets 0 tokens.
    /// Default: 1,000 tokens.
    /// </summary>
    public int MinimumChildBudget { get; init; } = 1_000;

    /// <summary>
    /// Safety margin factor (0.0 to 1.0).
    /// Reserves a percentage of budget to prevent mid-operation exhaustion.
    /// Default: 0.9 (90% usable, 10% buffer).
    /// </summary>
    public double SafetyMargin { get; init; } = 0.9;

    /// <summary>
    /// Base token cost for agent invocation (system prompt + overhead).
    /// Default: 500 tokens.
    /// </summary>
    public int BaseAgentCost { get; init; } = 500;

    /// <summary>
    /// Create default production configuration.
    /// </summary>
    public static TokenBudgetConfig Default => new();

    /// <summary>
    /// Create permissive configuration for development/testing.
    /// </summary>
    public static TokenBudgetConfig Permissive => new()
    {
        DefaultRootBudget = 500_000,
        MinimumChildBudget = 5_000,
        SafetyMargin = 0.95,
        BaseAgentCost = 500
    };
}

/// <summary>
/// Result of token budget validation.
/// </summary>
public sealed record TokenBudgetResult
{
    public TokenBudgetStatus Status { get; init; }
    public int RemainingTokens { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } 
        = new Dictionary<string, object>();

    public bool IsValid => Status == TokenBudgetStatus.Sufficient;

    public static TokenBudgetResult Sufficient(int remaining) => new()
    {
        Status = TokenBudgetStatus.Sufficient,
        RemainingTokens = remaining
    };

    public static TokenBudgetResult Insufficient(
        string errorMessage, 
        Dictionary<string, object>? metadata = null) => new()
    {
        Status = TokenBudgetStatus.Insufficient,
        RemainingTokens = 0,
        ErrorMessage = errorMessage,
        Metadata = metadata as IReadOnlyDictionary<string, object> ?? new Dictionary<string, object>()
    };

    public static TokenBudgetResult Exhausted(
        string errorMessage,
        Dictionary<string, object>? metadata = null) => new()
    {
        Status = TokenBudgetStatus.Exhausted,
        RemainingTokens = 0,
        ErrorMessage = errorMessage,
        Metadata = metadata as IReadOnlyDictionary<string, object> ?? new Dictionary<string, object>()
    };
}

/// <summary>
/// Token budget status.
/// </summary>
public enum TokenBudgetStatus
{
    /// <summary>
    /// Sufficient tokens available for next operation.
    /// </summary>
    Sufficient,

    /// <summary>
    /// Tokens available but insufficient for estimated next operation.
    /// </summary>
    Insufficient,

    /// <summary>
    /// Token budget fully exhausted.
    /// </summary>
    Exhausted
}
