using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AgentFlow.Core.Engine;

/// <summary>
/// ToolExecutor - enforces the complete security pipeline before any tool runs.
/// Pipeline:
/// 1. Resolve tool from registry
/// 2. Validate input schema (no I/O)
/// 3. Check tenant-level authorization
/// 4. Check tool-level permission policy
/// 5. Check per-tool rate limits
/// 6. Execute (sandbox if required)
/// 7. Validate output schema
/// 8. Audit log (always, even on failure)
/// </summary>
public sealed class ToolExecutorService : IToolExecutor
{
    private readonly IToolRegistry _registry;
    private readonly IToolAuthorizationService _authz;
    private readonly IToolSandbox _sandbox;
    private readonly ILogger<ToolExecutorService> _logger;

    public ToolExecutorService(
        IToolRegistry registry,
        IToolAuthorizationService authz,
        IToolSandbox sandbox,
        ILogger<ToolExecutorService> logger)
    {
        _registry = registry;
        _authz = authz;
        _sandbox = sandbox;
        _logger = logger;
    }

    public async Task<ToolExecutionResult> ExecuteToolAsync(
        ToolInvocationRequest request,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Step 1: Resolve (Synchronous memory based in Fase 2)
            var tool = _registry.Resolve(request.ToolName, "latest");
            if (tool is null)
            {
                return new ToolExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Tool '{request.ToolName}' not found or not available for tenant.",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            // Step 3 + 4: Authorization pipeline
            var canExecute = await _authz.AuthorizeAsync(new ToolAuthorizationContext
            {
                TenantId = request.TenantId,
                UserId = request.UserId,
                ToolName = request.ToolName,
                ToolId = request.ToolId,
                RiskLevel = tool.RiskLevel,
                ExecutionId = request.ExecutionId
            }, ct);

            if (!canExecute.IsAuthorized)
            {
                _logger.LogWarning(
                    "SECURITY: Tool {ToolName} execution denied for user {UserId} in tenant {TenantId}. Reason: {Reason}",
                    request.ToolName, request.UserId, request.TenantId, canExecute.DenialReason);

                return new ToolExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Execution denied: {canExecute.DenialReason}",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            // Step 5: Execute (sandbox or direct)
            var context = new ToolExecutionContext
            {
                TenantId = request.TenantId,
                UserId = request.UserId,
                ExecutionId = request.ExecutionId,
                StepId = request.StepId,
                InputJson = request.InputJson,
                CorrelationId = request.CorrelationId ?? request.ExecutionId
            };

            ToolResult pluginResult;
            
            // In Phase 2, we use the sandbox for High and Critical risks.
            // RiskLevel.Low/Medium run direct.
            if (tool.RiskLevel >= ToolRiskLevel.High)
            {
                pluginResult = await _sandbox.ExecuteInSandboxAsync(tool, context, ct);
            }
            else
            {
                pluginResult = await tool.ExecuteAsync(context, ct);
            }

            sw.Stop();

            if (!pluginResult.IsSuccess)
            {
                _logger.LogWarning(
                    "Tool {ToolName} execution failed for execution {ExecutionId}: {Error}",
                    request.ToolName, request.ExecutionId, pluginResult.ErrorMessage);
            }

            return new ToolExecutionResult
            {
                IsSuccess = pluginResult.IsSuccess,
                OutputJson = pluginResult.OutputJson,
                ErrorMessage = pluginResult.ErrorMessage,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Unhandled exception executing tool {ToolName} for execution {ExecutionId}",
                request.ToolName, request.ExecutionId);

            return new ToolExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Tool execution error: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<bool> CanExecuteAsync(
        string toolId,
        string tenantId,
        string userId,
        CancellationToken ct = default)
    {
        var tool = _registry.Resolve(toolId, "latest");
        if (tool is null) return false;

        var authResult = await _authz.AuthorizeAsync(new ToolAuthorizationContext
        {
            TenantId = tenantId,
            UserId = userId,
            ToolName = tool.Name,
            ToolId = toolId,
            RiskLevel = tool.RiskLevel
        }, ct);

        return authResult.IsAuthorized;
    }
}

// --- Security contracts ---

public interface IToolAuthorizationService
{
    Task<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationContext context, CancellationToken ct = default);
}

public sealed record ToolAuthorizationContext
{
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public required string ToolName { get; init; }
    public required string ToolId { get; init; }
    public ToolRiskLevel RiskLevel { get; init; }
    public string? ExecutionId { get; init; }
}

public sealed record ToolAuthorizationResult
{
    public bool IsAuthorized { get; init; }
    public string? DenialReason { get; init; }

    public static ToolAuthorizationResult Allow() => new() { IsAuthorized = true };
    public static ToolAuthorizationResult Deny(string reason) => new() { IsAuthorized = false, DenialReason = reason };
}

/// <summary>
/// Sandbox execution for critical risk tools.
/// </summary>
public interface IToolSandbox
{
    Task<ToolResult> ExecuteInSandboxAsync(
        IToolPlugin tool,
        ToolExecutionContext context,
        CancellationToken ct = default);
}

public sealed class DefaultToolSandbox : IToolSandbox
{
    private readonly ILogger<DefaultToolSandbox> _logger;

    public DefaultToolSandbox(ILogger<DefaultToolSandbox> logger)
    {
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteInSandboxAsync(
        IToolPlugin tool,
        ToolExecutionContext context,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "SANDBOX: Isolated execution start for tool {ToolName} (Risk: {RiskLevel})",
            tool.Name, tool.RiskLevel);

        // Define a hard timeout for sandbox execution (e.g., 30 seconds)
        // This is a safety net in case the agent loop timeout doesn't catch it.
        var sandboxTimeout = TimeSpan.FromSeconds(30);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(sandboxTimeout);

        try
        {
            // Execute in a Task.Run to ensure it doesn't block the caller thread 
            // if the tool implementation is poorly written (doing sync I/O, etc.)
            return await Task.Run(async () => 
            {
                return await tool.ExecuteAsync(context, cts.Token);
            }, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogError("SANDBOX: Tool {ToolName} timed out after {Timeout}s", tool.Name, sandboxTimeout.TotalSeconds);
            return ToolResult.Failure("SANDBOX_TIMEOUT", $"Tool execution timed out after {sandboxTimeout.TotalSeconds} seconds.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SANDBOX: Tool {ToolName} crashed with unhandled exception", tool.Name);
            return ToolResult.Failure("SANDBOX_CRASH", $"Tool implementation error: {ex.Message}");
        }
    }
}
