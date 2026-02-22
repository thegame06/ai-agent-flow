using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using ToolSDK = AgentFlow.ToolSDK;

namespace AgentFlow.Core.Engine;

/// <summary>
/// AgentAsToolPlugin — Enables hierarchical agent composition (Agents as Tools).
/// Allows a "Manager" agent to delegate tasks to "Specialist" agents.
/// 
/// This is the KILLER FEATURE that separates AgentFlow from chatbot frameworks.
/// Each delegation is fully governed, audited, and budget-controlled.
/// </summary>
public sealed class AgentAsToolPlugin : ToolSDK.IToolPlugin
{
    private readonly IAgentExecutor _executor;
    private readonly CircuitBreakerService _circuitBreaker;
    private readonly TokenBudgetService _tokenBudget;
    private readonly ILogger<AgentAsToolPlugin> _logger;

    public AgentAsToolPlugin(
        IAgentExecutor executor,
        CircuitBreakerService circuitBreaker,
        TokenBudgetService tokenBudget,
        ILogger<AgentAsToolPlugin> logger)
    {
        _executor = executor;
        _circuitBreaker = circuitBreaker;
        _tokenBudget = tokenBudget;
        _logger = logger;
    }

    public ToolSDK.ToolMetadata Metadata => new()
    {
        Id = "agent-as-tool",
        Name = "Delegate to Agent",
        Version = "1.0.0",
        Author = "AgentFlow Core Team",
        Description = "Delegate a task to another specialized agent. Enables hierarchical composition of agents.",
        Tags = new[] { "delegation", "composition", "meta" },
        RiskLevel = ToolSDK.ToolRiskLevel.Medium, // Delegation has moderate risk
        License = "MIT"
    };

    public ToolSDK.ToolSchema GetSchema()
    {
        return new ToolSDK.ToolSchema
        {
            Parameters = new Dictionary<string, ToolSDK.ParameterSchema>
            {
                ["agentKey"] = new ToolSDK.ParameterSchema
                {
                    Type = "string",
                    Description = "Unique identifier of the agent to invoke (e.g., 'credit-check-agent', 'risk-calculator')"
                },
                ["message"] = new ToolSDK.ParameterSchema
                {
                    Type = "string",
                    Description = "The task/question to send to the delegated agent"
                },
                ["variables"] = new ToolSDK.ParameterSchema
                {
                    Type = "object",
                    Description = "Optional context variables to pass to the delegated agent (e.g., userId, loanAmount)"
                }
            },
            Required = new[] { "agentKey", "message" },
            Example = new
            {
                agentKey = "credit-check-agent",
                message = "Verify credit score for user John Doe",
                variables = new
                {
                    userId = "user-123",
                    requestedAmount = 50000
                }
            }
        };
    }

    public async Task<ToolSDK.ToolResult> ExecuteAsync(ToolSDK.ToolContext context, CancellationToken ct = default)
    {
        try
        {
            // --- 1. Extract Parameters ---
            if (!context.Parameters.TryGetValue("agentKey", out var agentKeyObj) || agentKeyObj is not string agentKey)
            {
                return ToolSDK.ToolResult.FromError(
                    "Missing or invalid 'agentKey' parameter",
                    "InvalidParameter");
            }

            if (!context.Parameters.TryGetValue("message", out var messageObj) || messageObj is not string message)
            {
                return ToolSDK.ToolResult.FromError(
                    "Missing or invalid 'message' parameter",
                    "InvalidParameter");
            }

            // Optional variables
            var variables = new Dictionary<string, string>();
            if (context.Parameters.TryGetValue("variables", out var varsObj) && varsObj is JsonElement varsJson)
            {
                variables = JsonSerializer.Deserialize<Dictionary<string, string>>(varsJson.GetRawText()) 
                    ?? new Dictionary<string, string>();
            }

            // --- 2. Extract Execution Context (CallDepth, TokenBudget) ---
            var currentCallDepth = 0;
            var tokenBudget = _tokenBudget.GetDefaultBudget();
            var tokensUsedSoFar = 0;
            var executionStartedAt = DateTimeOffset.UtcNow;

            // Try to get from metadata (will be populated by AgentExecutionEngine)
            if (context.Metadata.TryGetValue("CallDepth", out var depthStr) && int.TryParse(depthStr, out var depth))
            {
                currentCallDepth = depth;
            }

            if (context.Metadata.TryGetValue("TokenBudget", out var budgetStr) && int.TryParse(budgetStr, out var budget))
            {
                tokenBudget = budget;
            }

            if (context.Metadata.TryGetValue("TokensUsed", out var usedStr) && int.TryParse(usedStr, out var used))
            {
                tokensUsedSoFar = used;
            }

            if (context.Metadata.TryGetValue("ExecutionStartedAt", out var startedStr) && DateTimeOffset.TryParse(startedStr, out var started))
            {
                executionStartedAt = started;
            }

            // --- 3. Circuit Breaker Check ---
            var circuitResult = _circuitBreaker.CanDelegate(
                context.ExecutionId,
                currentCallDepth,
                tokensUsedSoFar,
                tokenBudget,
                executionStartedAt);

            if (!circuitResult.IsAllowed)
            {
                _logger.LogWarning(
                    "Circuit breaker prevented delegation. ExecutionId: {ExecutionId}, Reason: {ErrorCode}",
                    context.ExecutionId, circuitResult.ErrorCode);

                return ToolSDK.ToolResult.FromError(
                    circuitResult.ErrorMessage!,
                    circuitResult.ErrorCode,
                    suggestedAction: "reduce_complexity");
            }

            // --- 4. Circular Reference Detection ---
            var callChain = new List<string>();
            if (context.Metadata.TryGetValue("CallChain", out var chainStr))
            {
                callChain = JsonSerializer.Deserialize<List<string>>(chainStr) ?? new List<string>();
            }

            if (_circuitBreaker.DetectCircularReference(callChain, agentKey))
            {
                _logger.LogWarning(
                    "Circular reference detected. Agent {AgentKey} already in call chain: {CallChain}",
                    agentKey, string.Join(" → ", callChain));

                return ToolSDK.ToolResult.FromError(
                    $"Circular reference detected: Agent '{agentKey}' is already in the call chain",
                    "CircularReferenceDetected",
                    suggestedAction: "choose_different_agent");
            }

            // --- 5. Token Budget Allocation ---
            var estimatedCost = _tokenBudget.EstimateCost(agentKey, message);
            var budgetValidation = _tokenBudget.Validate(tokenBudget, tokensUsedSoFar, estimatedCost);

            if (!budgetValidation.IsValid)
            {
                _logger.LogWarning(
                    "Token budget insufficient for delegation. ExecutionId: {ExecutionId}, Status: {Status}",
                    context.ExecutionId, budgetValidation.Status);

                return ToolSDK.ToolResult.FromError(
                    budgetValidation.ErrorMessage!,
                    budgetValidation.Status.ToString(),
                    suggestedAction: "simplify_or_skip");
            }

            // Allocate budget to child (assuming 1 child for now, can be enhanced for parallel)
            var childBudget = _tokenBudget.AllocateToChild(tokenBudget, tokensUsedSoFar, expectedChildrenCount: 1);

            // --- 6. Track Circuit Breaker State ---
            var childExecutionId = $"exec-child-{Guid.NewGuid():N}";
            _circuitBreaker.TrackExecutionStart(childExecutionId, context.ExecutionId);

            try
            {
                // --- 7. Delegate to Child Agent ---
                _logger.LogInformation(
                    "Delegating to agent '{AgentKey}'. Parent: {ParentExecutionId}, CallDepth: {CallDepth}, ChildBudget: {ChildBudget}",
                    agentKey, context.ExecutionId, currentCallDepth + 1, childBudget);

                var delegationRequest = new AgentExecutionRequest
                {
                    TenantId = context.TenantId,
                    AgentKey = agentKey,
                    UserId = context.UserId,
                    UserMessage = message,
                    ParentExecutionId = context.ExecutionId, // Link parent-child relationship
                    CorrelationId = context.Metadata.TryGetValue("CorrelationId", out var corrId) ? corrId : Guid.NewGuid().ToString(),
                    Metadata = variables.AsReadOnly(),
                    CallDepth = currentCallDepth + 1, // Increment depth
                    TokenBudget = childBudget
                };

                var result = await _executor.ExecuteAsync(delegationRequest, ct);

                // --- 8. Return Child Result ---
                var output = new
                {
                    executionId = result.ExecutionId,
                    agentKey = result.AgentKey,
                    status = result.Status.ToString(),
                    finalResponse = result.FinalResponse,
                    totalSteps = result.TotalSteps,
                    tokensUsed = result.TotalTokensUsed,
                    durationMs = result.DurationMs,
                    errorCode = result.ErrorCode,
                    errorMessage = result.ErrorMessage
                };

                var metadata = new Dictionary<string, string>
                {
                    ["childExecutionId"] = result.ExecutionId,
                    ["childAgentKey"] = agentKey,
                    ["tokensUsed"] = result.TotalTokensUsed.ToString(),
                    ["callDepth"] = (currentCallDepth + 1).ToString()
                };

                if (result.Status == ExecutionStatus.Failed)
                {
                    _logger.LogWarning(
                        "Child agent execution failed. Agent: {AgentKey}, ExecutionId: {ExecutionId}, Error: {ErrorCode}",
                        agentKey, result.ExecutionId, result.ErrorCode);

                    return ToolSDK.ToolResult.FromError(
                        $"Delegated agent '{agentKey}' failed: {result.ErrorMessage}",
                        result.ErrorCode ?? "ChildAgentFailed",
                        suggestedAction: "retry_or_escalate");
                }

                return ToolSDK.ToolResult.FromSuccess(output, metadata);
            }
            finally
            {
                // --- 9. Clean Up Circuit Breaker State ---
                _circuitBreaker.TrackExecutionEnd(childExecutionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Unhandled exception during agent delegation. ExecutionId: {ExecutionId}",
                context.ExecutionId);

            return ToolSDK.ToolResult.FromError(
                $"Internal error during delegation: {ex.Message}",
                "DelegationFailed");
        }
    }

    public IReadOnlyList<ToolSDK.PolicyRequirement> RequiredPolicies => new[]
    {
        new ToolSDK.PolicyRequirement
        {
            PolicyGroupId = "agent-delegation",
            IsMandatory = true,
            Reason = "Agent-to-agent delegation requires explicit policy approval"
        }
    };

    public ToolSDK.PluginCapabilities Capabilities => new()
    {
        SupportsAsync = true, // Child execution can be async
        SupportsStreaming = false,
        IsCacheable = false, // Agent executions are not deterministic
        RequiresNetwork = false, // Internal delegation
        IsReadOnly = false, // Delegation can trigger state changes
        EstimatedExecutionMs = 5000 // Depends on child agent complexity
    };
}
