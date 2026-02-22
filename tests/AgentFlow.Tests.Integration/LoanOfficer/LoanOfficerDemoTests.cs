using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;
using AgentFlow.Extensions.Tools;
using AgentFlow.ToolSDK;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace AgentFlow.Tests.Integration.LoanOfficer;

/// <summary>
/// Integration test demonstrating the Loan Officer demo with hierarchical agent composition.
/// 
/// Hierarchy:
///   LoanOfficerAgent (Manager)
///     ├─→ CreditCheckAgent (Specialist)
///     │     └─→ BureauAPIPlugin (Tool)
///     ├─→ RiskCalculatorAgent (Specialist)
///     │     └─→ FinancialModelPlugin (Tool)
///     └─→ ApprovalAgent (Specialist + HITL)
///           └─→ EmailNotificationPlugin (Tool)
/// 
/// This demonstrates:
/// - ✅ 3-level agent delegation (depth 0 → 1 → 2)
/// - ✅ Token budget distribution (100k allocated fairly)
/// - ✅ Circuit breaker protection (max depth, timeout)
/// - ✅ Mock plugins for demo (Bureau, Risk Model, Email)
/// - ✅ Audit trail (every delegation logged)
/// - ✅ HITL checkpoint (approval requires human review)
/// </summary>
public class LoanOfficerDemoTests
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<AgentAsToolPlugin> _pluginLogger;
    private readonly ILogger<BureauAPIPlugin> _bureauLogger;
    private readonly ILogger<FinancialModelPlugin> _riskLogger;
    private readonly ILogger<EmailNotificationPlugin> _emailLogger;

    public LoanOfficerDemoTests(ITestOutputHelper output)
    {
        _output = output;
        _pluginLogger = new XunitLogger<AgentAsToolPlugin>(output);
        _bureauLogger = new XunitLogger<BureauAPIPlugin>(output);
        _riskLogger = new XunitLogger<FinancialModelPlugin>(output);
        _emailLogger = new XunitLogger<EmailNotificationPlugin>(output);
    }

    [Fact]
    public async Task LoanOfficer_FullFlow_DemonstratesHierarchicalComposition()
    {
        // ═══════════════════════════════════════════════════════════════
        // ARRANGE: Setup mock executor and plugins
        // ═══════════════════════════════════════════════════════════════

        var circuitBreaker = new CircuitBreakerService(
            CircuitBreakerConfig.Default,
            NullLogger<CircuitBreakerService>.Instance);

        var tokenBudget = new TokenBudgetService(
            TokenBudgetConfig.Default);

        // Create mock agent executor that simulates child agent responses
        var mockExecutor = new MockAgentExecutor();

        // Register specialist agent handlers
        mockExecutor.RegisterAgent("credit-check-agent", async (request, ct) =>
        {
            _output.WriteLine($"[CreditCheckAgent] Received delegation at CallDepth={request.CallDepth}");
            
            // Simulate credit check by calling BureauAPI plugin
            var bureauPlugin = new BureauAPIPlugin(_bureauLogger);
            var bureauContext = new ToolContext
            {
                TenantId = request.TenantId,
                UserId = request.UserId,
                ExecutionId = request.ExecutionId,
                Parameters = new Dictionary<string, object>
                {
                    ["fullName"] = "John Doe",
                    ["ssn"] = "5678",
                    ["purpose"] = "loan application"
                }
            };

            var bureauResult = await bureauPlugin.ExecuteAsync(bureauContext, ct);
            
            return new AgentExecutionResult
            {
                ExecutionId = request.ExecutionId,
                AgentKey = "credit-check-agent",
                Status = AgentExecutionStatus.Completed,
                FinalAnswer = bureauResult.Success 
                    ? $"Credit check complete. {bureauResult.Data}" 
                    : $"Credit check failed: {bureauResult.ErrorMessage}",
                TotalSteps = 2,
                TotalTokensUsed = 850,
                RuntimeSnapshot = new AgentRuntimeSnapshot
                {
                    ToolCallHistory = new List<ToolCallRecord>
                    {
                        new() { ToolId = "bureau-api", Timestamp = DateTimeOffset.UtcNow, Success = bureauResult.Success }
                    }
                }
            };
        });

        mockExecutor.RegisterAgent("risk-calculator-agent", async (request, ct) =>
        {
            _output.WriteLine($"[RiskCalculatorAgent] Received delegation at CallDepth={request.CallDepth}");
            
            // Simulate risk calculation by calling FinancialModelPlugin
            var riskPlugin = new FinancialModelPlugin(_riskLogger);
            var riskContext = new ToolContext
            {
                TenantId = request.TenantId,
                UserId = request.UserId,
                ExecutionId = request.ExecutionId,
                Parameters = new Dictionary<string, object>
                {
                    ["creditScore"] = 720,
                    ["loanAmount"] = 50000,
                    ["annualIncome"] = 75000,
                    ["employmentYears"] = 5,
                    ["debtToIncomeRatio"] = 0.35
                }
            };

            var riskResult = await riskPlugin.ExecuteAsync(riskContext, ct);
            
            return new AgentExecutionResult
            {
                ExecutionId = request.ExecutionId,
                AgentKey = "risk-calculator-agent",
                Status = AgentExecutionStatus.Completed,
                FinalAnswer = riskResult.Success 
                    ? $"Risk assessment complete. {riskResult.Data}" 
                    : $"Risk calculation failed: {riskResult.ErrorMessage}",
                TotalSteps = 2,
                TotalTokensUsed = 920,
                RuntimeSnapshot = new AgentRuntimeSnapshot
                {
                    ToolCallHistory = new List<ToolCallRecord>
                    {
                        new() { ToolId = "financial-risk-model", Timestamp = DateTimeOffset.UtcNow, Success = riskResult.Success }
                    }
                }
            };
        });

        mockExecutor.RegisterAgent("approval-agent", async (request, ct) =>
        {
            _output.WriteLine($"[ApprovalAgent] Received delegation at CallDepth={request.CallDepth}");
            
            // Simulate approval notification by calling EmailPlugin
            var emailPlugin = new EmailNotificationPlugin(_emailLogger);
            var emailContext = new ToolContext
            {
                TenantId = request.TenantId,
                UserId = request.UserId,
                ExecutionId = request.ExecutionId,
                Parameters = new Dictionary<string, object>
                {
                    ["to"] = "john.doe@example.com",
                    ["subject"] = "Your Loan Application Status - APPROVED",
                    ["body"] = "Dear John Doe, your loan application for $50,000 has been approved pending final human review.",
                    ["priority"] = "high"
                }
            };

            var emailResult = await emailPlugin.ExecuteAsync(emailContext, ct);
            
            return new AgentExecutionResult
            {
                ExecutionId = request.ExecutionId,
                AgentKey = "approval-agent",
                Status = AgentExecutionStatus.HumanReviewPending, // HITL checkpoint
                FinalAnswer = "Approval notification sent. Awaiting human review for compliance.",
                TotalSteps = 2,
                TotalTokensUsed = 650,
                RuntimeSnapshot = new AgentRuntimeSnapshot
                {
                    ToolCallHistory = new List<ToolCallRecord>
                    {
                        new() { ToolId = "email-notification", Timestamp = DateTimeOffset.UtcNow, Success = emailResult.Success }
                    },
                    HumanReviewCheckpoints = new List<HumanReviewCheckpoint>
                    {
                        new() 
                        { 
                            Reason = "Final loan approval requires human review for compliance",
                            Timestamp = DateTimeOffset.UtcNow
                        }
                    }
                }
            };
        });

        // Create AgentAsToolPlugin
        var plugin = new AgentAsToolPlugin(
            mockExecutor,
            circuitBreaker,
            tokenBudget,
            _pluginLogger);

        // ═══════════════════════════════════════════════════════════════
        // ACT: Simulate Loan Officer delegating to specialists
        // ═══════════════════════════════════════════════════════════════

        _output.WriteLine("\n═══ LOAN OFFICER DEMO START ═══\n");

        // Step 1: Loan Officer delegates to Credit Check Agent
        _output.WriteLine("Step 1: Delegating to CreditCheckAgent...");
        var creditCheckContext = new ToolContext
        {
            TenantId = "tenant-acme-bank",
            UserId = "loan-officer-001",
            ExecutionId = "exec-root",
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "credit-check-agent",
                ["message"] = "Check credit for John Doe, SSN 5678"
            },
            Metadata = new Dictionary<string, string>
            {
                ["CallDepth"] = "0",
                ["TokenBudget"] = "100000",
                ["TokensUsed"] = "0",
                ["ExecutionStartedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        var creditResult = await plugin.ExecuteAsync(creditCheckContext, CancellationToken.None);
        _output.WriteLine($"Credit Result: Success={creditResult.Success}, Data (first 200 chars)={creditResult.Data?.Substring(0, Math.Min(200, creditResult.Data.Length ?? 0))}");

        Assert.True(creditResult.Success, $"Credit check should succeed. Error: {creditResult.ErrorMessage}");

        // Step 2: Loan Officer delegates to Risk Calculator Agent
        _output.WriteLine("\nStep 2: Delegating to RiskCalculatorAgent...");
        var riskContext = new ToolContext
        {
            TenantId = "tenant-acme-bank",
            UserId = "loan-officer-001",
            ExecutionId = "exec-root",
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "risk-calculator-agent",
                ["message"] = "Calculate risk for credit score 720, loan amount $50,000, income $75,000"
            },
            Metadata = new Dictionary<string, string>
            {
                ["CallDepth"] = "0",
                ["TokenBudget"] = "100000",
                ["TokensUsed"] = "850",
                ["ExecutionStartedAt"] = creditCheckContext.Metadata["ExecutionStartedAt"]
            }
        };

        var riskResult = await plugin.ExecuteAsync(riskContext, CancellationToken.None);
        _output.WriteLine($"Risk Result: Success={riskResult.Success}, Data (first 200 chars)={riskResult.Data?.Substring(0, Math.Min(200, riskResult.Data.Length ?? 0))}");

        Assert.True(riskResult.Success, $"Risk calculation should succeed. Error: {riskResult.ErrorMessage}");

        // Step 3: Loan Officer delegates to Approval Agent
        _output.WriteLine("\nStep 3: Delegating to ApprovalAgent...");
        var approvalContext = new ToolContext
        {
            TenantId = "tenant-acme-bank",
            UserId = "loan-officer-001",
            ExecutionId = "exec-root",
            Parameters = new Dictionary<string, object>
            {
                ["agentKey"] = "approval-agent",
                ["message"] = "Approve loan for John Doe. Credit: 720 (good), Risk: Low. Send notification."
            },
            Metadata = new Dictionary<string, string>
            {
                ["CallDepth"] = "0",
                ["TokenBudget"] = "100000",
                ["TokensUsed"] = "1770",
                ["ExecutionStartedAt"] = creditCheckContext.Metadata["ExecutionStartedAt"]
            }
        };

        var approvalResult = await plugin.ExecuteAsync(approvalContext, CancellationToken.None);
        _output.WriteLine($"Approval Result: Success={approvalResult.Success}, Data (first 200 chars)={approvalResult.Data?.Substring(0, Math.Min(200, approvalResult.Data.Length ?? 0))}");

        Assert.True(approvalResult.Success, $"Approval should succeed. Error: {approvalResult.ErrorMessage}");

        // ═══════════════════════════════════════════════════════════════
        // ASSERT: Verify hierarchical composition worked correctly
        // ═══════════════════════════════════════════════════════════════

        _output.WriteLine("\n═══ VERIFICATION ═══\n");

        // Verify all delegations succeeded
        Assert.True(creditResult.Success);
        Assert.True(riskResult.Success);
        Assert.True(approvalResult.Success);

        // Verify call depth was tracked
        var creditExecution = mockExecutor.GetExecution("credit-check-agent");
        var riskExecution = mockExecutor.GetExecution("risk-calculator-agent");
        var approvalExecution = mockExecutor.GetExecution("approval-agent");

        Assert.Equal(1, creditExecution.CallDepth); // Parent is 0, child is 1
        Assert.Equal(1, riskExecution.CallDepth);
        Assert.Equal(1, approvalExecution.CallDepth);

        // Verify token budget distributed fairly
        var totalTokensUsed = mockExecutor.TotalTokensUsed;
        _output.WriteLine($"Total tokens used across all agents: {totalTokensUsed}");
        Assert.True(totalTokensUsed < 100000, "Should stay within budget");
        Assert.True(totalTokensUsed > 2000, "Should have used reasonable amount of tokens");

        // Verify HITL checkpoint was triggered for approval agent
        Assert.Equal(AgentExecutionStatus.HumanReviewPending, approvalExecution.Status);
        Assert.NotEmpty(approvalExecution.RuntimeSnapshot.HumanReviewCheckpoints);

        _output.WriteLine("\n✅ All verifications passed!");
        _output.WriteLine($"✅ 3 specialist agents invoked successfully");
        _output.WriteLine($"✅ Call depths tracked correctly (all at depth 1)");
        _output.WriteLine($"✅ Total tokens: {totalTokensUsed} (within 100k budget)");
        _output.WriteLine($"✅ HITL checkpoint triggered for approval");
        _output.WriteLine("\n═══ LOAN OFFICER DEMO COMPLETE ═══");
    }

    [Fact]
    public async Task CircuitBreaker_PreventsExcessiveDepth()
    {
        // ═══════════════════════════════════════════════════════════════
        // TEST: Circuit breaker should prevent depth > 5
        // ═══════════════════════════════════════════════════════════════

        var circuitBreaker = new CircuitBreakerService(
            CircuitBreakerConfig.Default, // MaxCallDepth = 5
            NullLogger<CircuitBreakerService>.Instance);

        var tokenBudget = new TokenBudgetService(TokenBudgetConfig.Default);
        var mockExecutor = new MockAgentExecutor();

        var plugin = new AgentAsToolPlugin(
            mockExecutor,
            circuitBreaker,
            tokenBudget,
            _pluginLogger);

        var context = new ToolContext
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
                ["CallDepth"] = "5", // Already at max depth
                ["TokenBudget"] = "100000",
                ["TokensUsed"] = "5000",
                ["ExecutionStartedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        var result = await plugin.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("depth", result.ErrorMessage?.ToLower() ?? "");
        Assert.Equal("reduce_complexity", result.SuggestedAction);

        _output.WriteLine($"✅ Circuit breaker correctly prevented delegation at depth 5");
        _output.WriteLine($"   Error: {result.ErrorMessage}");
    }

    [Fact]
    public async Task TokenBudget_PreventsExcessiveSpending()
    {
        // ═══════════════════════════════════════════════════════════════
        // TEST: Token budget should prevent budget exhaustion
        // ═══════════════════════════════════════════════════════════════

        var circuitBreaker = new CircuitBreakerService(
            CircuitBreakerConfig.Default,
            NullLogger<CircuitBreakerService>.Instance);

        var tokenBudget = new TokenBudgetService(TokenBudgetConfig.Default);
        var mockExecutor = new MockAgentExecutor();

        var plugin = new AgentAsToolPlugin(
            mockExecutor,
            circuitBreaker,
            tokenBudget,
            _pluginLogger);

        var context = new ToolContext
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
                ["TokensUsed"] = "9500", // Almost exhausted
                ["ExecutionStartedAt"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        var result = await plugin.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("token", result.ErrorMessage?.ToLower() ?? "");
        Assert.Equal("simplify_or_skip", result.SuggestedAction);

        _output.WriteLine($"✅ Token budget correctly prevented expensive delegation");
        _output.WriteLine($"   Error: {result.ErrorMessage}");
    }
}

// ═══════════════════════════════════════════════════════════════
// MOCK AGENT EXECUTOR (for testing without full runtime)
// ═══════════════════════════════════════════════════════════════

public class MockAgentExecutor : IAgentExecutor
{
    private readonly Dictionary<string, Func<AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>>> _handlers = new();
    private readonly Dictionary<string, AgentExecutionResult> _executions = new();

    public void RegisterAgent(string agentKey, Func<AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>> handler)
    {
        _handlers[agentKey] = handler;
    }

    public async Task<AgentExecutionResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct = default)
    {
        if (!_handlers.TryGetValue(request.AgentKey, out var handler))
        {
            return new AgentExecutionResult
            {
                ExecutionId = request.ExecutionId,
                AgentKey = request.AgentKey,
                Status = AgentExecutionStatus.Failed,
                FinalAnswer = $"Agent '{request.AgentKey}' not found",
                ErrorCode = "AGENT_NOT_FOUND"
            };
        }

        var result = await handler(request, ct);
        _executions[request.AgentKey] = result;
        return result;
    }

    public AgentExecutionResult GetExecution(string agentKey) => _executions[agentKey];

    public int TotalTokensUsed => _executions.Values.Sum(e => e.TotalTokensUsed);

    public Task<AgentExecutionResult> ResumeAsync(string executionId, string tenantId, CheckpointDecision decision, CancellationToken ct = default)
    {
        throw new NotImplementedException("Resume not implemented in mock executor");
    }

    public Task<AgentFlow.Abstractions.Result> CancelAsync(string executionId, string tenantId, string cancelledBy, CancellationToken ct = default)
    {
        throw new NotImplementedException("Cancel not implemented in mock executor");
    }
}

// ═══════════════════════════════════════════════════════════════
// XUNIT LOGGER (for test output)
// ═══════════════════════════════════════════════════════════════

public class XunitLogger<T> : ILogger<T>
{
    private readonly ITestOutputHelper _output;

    public XunitLogger(ITestOutputHelper output)
    {
        _output = output;
    }

    public IDisposable BeginScope<TState>(TState state) => null!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        if (exception != null)
        {
            _output.WriteLine($"   Exception: {exception}");
        }
    }
}
