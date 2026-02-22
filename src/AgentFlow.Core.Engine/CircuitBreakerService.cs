using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AgentFlow.Core.Engine;

/// <summary>
/// Circuit Breaker Service — Prevents infinite recursion and runaway agent chains.
/// Enforces safety limits on call depth, total executions, token budget, and duration.
/// </summary>
public sealed class CircuitBreakerService
{
    private readonly CircuitBreakerConfig _config;
    private readonly ILogger<CircuitBreakerService> _logger;
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _activeExecutions;

    public CircuitBreakerService(
        CircuitBreakerConfig config,
        ILogger<CircuitBreakerService> logger)
    {
        _config = config;
        _logger = logger;
        _activeExecutions = new ConcurrentDictionary<string, CircuitBreakerState>();
    }

    /// <summary>
    /// Check if delegation is allowed for the given execution context.
    /// Returns validation result with detailed error if circuit should trip.
    /// </summary>
    public CircuitBreakerResult CanDelegate(
        string parentExecutionId,
        int currentCallDepth,
        int tokensUsedSoFar,
        int totalTokenBudget,
        DateTimeOffset executionStartedAt)
    {
        // Check 1: Max Call Depth
        if (currentCallDepth >= _config.MaxCallDepth)
        {
            _logger.LogWarning(
                "Circuit breaker tripped: Max call depth ({MaxDepth}) exceeded. Current depth: {CurrentDepth}, ExecutionId: {ExecutionId}",
                _config.MaxCallDepth, currentCallDepth + 1, parentExecutionId);

            return CircuitBreakerResult.Tripped(
                "MaxCallDepthExceeded",
                $"Maximum call depth ({_config.MaxCallDepth}) exceeded. Current depth: {currentCallDepth + 1}",
                new Dictionary<string, object>
                {
                    ["maxDepth"] = _config.MaxCallDepth,
                    ["currentDepth"] = currentCallDepth + 1,
                    ["executionId"] = parentExecutionId
                });
        }

        // Check 2: Execution Duration (Token budget validation delegated to TokenBudgetService)
        var duration = DateTimeOffset.UtcNow - executionStartedAt;
        if (duration > _config.MaxDuration)
        {
            _logger.LogWarning(
                "Circuit breaker tripped: Max duration ({MaxDuration}) exceeded. Duration: {Duration}, ExecutionId: {ExecutionId}",
                _config.MaxDuration, duration, parentExecutionId);

            return CircuitBreakerResult.Tripped(
                "MaxDurationExceeded",
                $"Maximum duration ({_config.MaxDuration}) exceeded. Current duration: {duration}",
                new Dictionary<string, object>
                {
                    ["maxDuration"] = _config.MaxDuration,
                    ["currentDuration"] = duration,
                    ["executionId"] = parentExecutionId
                });
        }

        // Check 3: Max Total Executions (if tracking enabled)
        if (_activeExecutions.TryGetValue(parentExecutionId, out var state))
        {
            if (state.TotalExecutionsInChain >= _config.MaxTotalExecutions)
            {
                _logger.LogWarning(
                    "Circuit breaker tripped: Max total executions ({MaxExecutions}) exceeded. Total: {Total}, ExecutionId: {ExecutionId}",
                    _config.MaxTotalExecutions, state.TotalExecutionsInChain, parentExecutionId);

                return CircuitBreakerResult.Tripped(
                    "MaxTotalExecutionsExceeded",
                    $"Maximum total executions ({_config.MaxTotalExecutions}) in chain exceeded. Total: {state.TotalExecutionsInChain}",
                    new Dictionary<string, object>
                    {
                        ["maxExecutions"] = _config.MaxTotalExecutions,
                        ["totalExecutions"] = state.TotalExecutionsInChain,
                        ["executionId"] = parentExecutionId
                    });
            }
        }

        return CircuitBreakerResult.Allowed();
    }

    /// <summary>
    /// Detect circular references in call chain (Agent A → Agent B → Agent A).
    /// </summary>
    public bool DetectCircularReference(IEnumerable<string> callChain, string targetAgentKey)
    {
        return callChain.Contains(targetAgentKey);
    }

    /// <summary>
    /// Track execution start for circuit breaker state.
    /// </summary>
    public void TrackExecutionStart(string executionId, string? parentExecutionId)
    {
        // Determine total executions count
        var totalExecutions = 1;
        if (parentExecutionId != null && _activeExecutions.TryGetValue(parentExecutionId, out var parentState))
        {
            totalExecutions = parentState.TotalExecutionsInChain + 1;
        }

        var state = new CircuitBreakerState
        {
            ExecutionId = executionId,
            ParentExecutionId = parentExecutionId,
            StartedAt = DateTimeOffset.UtcNow,
            TotalExecutionsInChain = totalExecutions
        };

        _activeExecutions[executionId] = state;
    }

    /// <summary>
    /// Track execution completion and clean up state.
    /// </summary>
    public void TrackExecutionEnd(string executionId)
    {
        _activeExecutions.TryRemove(executionId, out _);
    }

    /// <summary>
    /// Get current circuit breaker state for an execution.
    /// </summary>
    public CircuitBreakerState? GetState(string executionId)
    {
        return _activeExecutions.TryGetValue(executionId, out var state) ? state : null;
    }
}

/// <summary>
/// Configuration for circuit breaker limits.
/// </summary>
public sealed record CircuitBreakerConfig
{
    /// <summary>
    /// Maximum nesting depth for agent-to-agent delegation.
    /// Default: 5 levels (0 = root, 1-5 = nested).
    /// </summary>
    public int MaxCallDepth { get; init; } = 5;

    /// <summary>
    /// Maximum total agent executions in the entire chain (prevents fork bombs).
    /// Default: 50 executions.
    /// </summary>
    public int MaxTotalExecutions { get; init; } = 50;

    /// <summary>
    /// Maximum execution duration for the entire chain.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Create default production-safe configuration.
    /// </summary>
    public static CircuitBreakerConfig Default => new();

    /// <summary>
    /// Create permissive configuration for testing.
    /// </summary>
    public static CircuitBreakerConfig Permissive => new()
    {
        MaxCallDepth = 10,
        MaxTotalExecutions = 100,
        MaxDuration = TimeSpan.FromMinutes(10)
    };
}

/// <summary>
/// Circuit breaker state for an active execution.
/// </summary>
public sealed record CircuitBreakerState
{
    public required string ExecutionId { get; init; }
    public string? ParentExecutionId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public int TotalExecutionsInChain { get; init; }
}

/// <summary>
/// Result of circuit breaker validation.
/// </summary>
public sealed record CircuitBreakerResult
{
    public bool IsAllowed { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } 
        = new Dictionary<string, object>();

    public static CircuitBreakerResult Allowed() => new() { IsAllowed = true };

    public static CircuitBreakerResult Tripped(
        string errorCode, 
        string errorMessage, 
        Dictionary<string, object>? metadata = null) => new()
    {
        IsAllowed = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
        Metadata = metadata as IReadOnlyDictionary<string, object> ?? new Dictionary<string, object>()
    };
}
