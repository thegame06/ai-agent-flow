using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentFlow.Tests.Integration.LoanOfficer;

public class LoanOfficerDemoTests
{
    [Fact]
    public async Task AgentAsTool_Delegates_ToSpecialist_Successfully()
    {
        var plugin = BuildPlugin(out var executor);

        executor.Register("credit-check-agent", request =>
            Task.FromResult(new AgentExecutionResult
            {
                ExecutionId = $"exec-{Guid.NewGuid():N}",
                AgentKey = request.AgentKey,
                AgentVersion = request.AgentVersion ?? "1.0.0",
                Status = ExecutionStatus.Completed,
                FinalResponse = "Credit approved",
                TotalSteps = 2,
                TotalTokensUsed = 320,
                DurationMs = 120
            }));

        var result = await plugin.ExecuteAsync(new AgentFlow.ToolSDK.ToolContext
        {
            TenantId = "tenant-acme",
            UserId = "loan-officer-1",
            ExecutionId = "exec-root-1",
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "credit-check-agent",
                ["message"] = "Check customer credit file"
            },
            Metadata = new Dictionary<string, string>
            {
                ["CallDepth"] = "0",
                ["TokenBudget"] = "100000",
                ["TokensUsed"] = "0",
                ["ExecutionStartedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Output);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Output);
        Assert.Contains("credit-check-agent", json);
        Assert.Contains("Completed", json);
    }

    [Fact]
    public async Task AgentAsTool_Fails_When_AgentKey_IsMissing()
    {
        var plugin = BuildPlugin(out _);

        var result = await plugin.ExecuteAsync(new AgentFlow.ToolSDK.ToolContext
        {
            TenantId = "tenant-acme",
            UserId = "loan-officer-1",
            ExecutionId = "exec-root-2",
            Parameters = new Dictionary<string, object>
            {
                ["message"] = "Check customer credit file"
            }
        });

        Assert.False(result.Success);
        Assert.Equal("InvalidParameter", result.ErrorCode);
    }

    [Fact]
    public async Task AgentAsTool_Trips_CircuitBreaker_On_MaxDepth()
    {
        var plugin = BuildPlugin(out var executor);

        executor.Register("risk-agent", request =>
            Task.FromResult(new AgentExecutionResult
            {
                ExecutionId = $"exec-{Guid.NewGuid():N}",
                AgentKey = request.AgentKey,
                AgentVersion = request.AgentVersion ?? "1.0.0",
                Status = ExecutionStatus.Completed,
                FinalResponse = "Risk ok",
                TotalSteps = 1,
                TotalTokensUsed = 100,
                DurationMs = 50
            }));

        var result = await plugin.ExecuteAsync(new AgentFlow.ToolSDK.ToolContext
        {
            TenantId = "tenant-acme",
            UserId = "loan-officer-1",
            ExecutionId = "exec-root-3",
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "risk-agent",
                ["message"] = "Assess risk"
            },
            Metadata = new Dictionary<string, string>
            {
                ["CallDepth"] = "5", // default max depth is 5, so this should trip
                ["TokenBudget"] = "100000",
                ["TokensUsed"] = "0",
                ["ExecutionStartedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        });

        Assert.False(result.Success);
        Assert.Equal("MaxCallDepthExceeded", result.ErrorCode);
    }

    private static AgentAsToolPlugin BuildPlugin(out FakeAgentExecutor executor)
    {
        executor = new FakeAgentExecutor();
        var circuitBreaker = new CircuitBreakerService(CircuitBreakerConfig.Default, NullLogger<CircuitBreakerService>.Instance);
        var tokenBudget = new TokenBudgetService(TokenBudgetConfig.Default);

        return new AgentAsToolPlugin(
            executor,
            circuitBreaker,
            tokenBudget,
            NullLogger<AgentAsToolPlugin>.Instance);
    }

    private sealed class FakeAgentExecutor : IAgentExecutor
    {
        private readonly Dictionary<string, Func<AgentExecutionRequest, Task<AgentExecutionResult>>> _handlers = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string agentKey, Func<AgentExecutionRequest, Task<AgentExecutionResult>> handler)
            => _handlers[agentKey] = handler;

        public Task<AgentExecutionResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct = default)
        {
            if (_handlers.TryGetValue(request.AgentKey, out var handler))
            {
                return handler(request);
            }

            return Task.FromResult(new AgentExecutionResult
            {
                ExecutionId = $"exec-{Guid.NewGuid():N}",
                AgentKey = request.AgentKey,
                AgentVersion = request.AgentVersion ?? "1.0.0",
                Status = ExecutionStatus.Failed,
                FinalResponse = null,
                TotalSteps = 0,
                TotalTokensUsed = 0,
                DurationMs = 1,
                ErrorCode = "AgentNotRegistered",
                ErrorMessage = $"No handler registered for '{request.AgentKey}'"
            });
        }

        public Task<AgentExecutionResult> ResumeAsync(string executionId, string tenantId, CheckpointDecision decision, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Result> CancelAsync(string executionId, string tenantId, string cancelledBy, CancellationToken ct = default)
            => Task.FromResult(Result.Success());
    }
}
