using AgentFlow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AgentFlow.Extensions;

// =========================================================================
// EXTENSION REGISTRY
// =========================================================================

public interface IExtensionRegistry
{
    void RegisterTool(IToolPlugin tool);
    void RegisterPolicy(IPolicyEvaluator evaluator);
    
    IReadOnlyList<IToolPlugin> GetTools();
    IToolPlugin? GetTool(string name);
    
    IReadOnlyList<IPolicyEvaluator> GetPolicies();
    
    IReadOnlyList<IAgentFlowExtension> GetAllExtensions();
}

public sealed class ExtensionRegistry : IExtensionRegistry
{
    private readonly ConcurrentDictionary<string, IToolPlugin> _tools = new();
    private readonly ConcurrentDictionary<string, IPolicyEvaluator> _policies = new();
    private readonly ILogger<ExtensionRegistry> _logger;

    public ExtensionRegistry(
        IEnumerable<IToolPlugin> tools,
        IEnumerable<IPolicyEvaluator> policies,
        ILogger<ExtensionRegistry> logger)
    {
        _logger = logger;
        foreach (var tool in tools) RegisterTool(tool);
        foreach (var policy in policies) RegisterPolicy(policy);
    }

    public void RegisterTool(IToolPlugin tool)
    {
        if (_tools.TryAdd(tool.Name, tool))
        {
            _logger.LogInformation("Extension Registered: Tool '{ToolName}' v{Version}", tool.Name, tool.Version);
        }
    }

    public void RegisterPolicy(IPolicyEvaluator evaluator)
    {
        // For policies, the ID is often inside the config or the type
        _policies.TryAdd(evaluator.PolicyType, evaluator);
    }

    public IReadOnlyList<IToolPlugin> GetTools() => _tools.Values.ToList();
    public IToolPlugin? GetTool(string name) => _tools.GetValueOrDefault(name);
    public IReadOnlyList<IPolicyEvaluator> GetPolicies() => _policies.Values.ToList();

    public IReadOnlyList<IAgentFlowExtension> GetAllExtensions()
    {
        var all = new List<IAgentFlowExtension>();
        all.AddRange(_tools.Values);
        // Note: IPolicyEvaluator should inherit from IAgentFlowExtension in Abstractions
        return all;
    }
}

// =========================================================================
// TOOL INVOKER (LangChain / Elsa style DX)
// =========================================================================

public interface IToolInvoker
{
    Task<ToolResult> InvokeAsync(
        string toolName, 
        string inputJson, 
        ToolExecutionContext? customContext = null,
        CancellationToken ct = default);
}

public sealed class ToolInvoker : IToolInvoker
{
    private readonly IExtensionRegistry _registry;
    private readonly ILogger<ToolInvoker> _logger;

    public ToolInvoker(IExtensionRegistry registry, ILogger<ToolInvoker> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<ToolResult> InvokeAsync(
        string toolName, 
        string inputJson, 
        ToolExecutionContext? customContext = null, 
        CancellationToken ct = default)
    {
        var tool = _registry.GetTool(toolName);
        if (tool == null)
        {
            return ToolResult.Failure("EXT001", $"Tool '{toolName}' not found in registry.");
        }

        var context = customContext ?? new ToolExecutionContext
        {
            TenantId = "system-debug",
            UserId = "developer",
            ExecutionId = $"dbg-{Guid.NewGuid():N}",
            StepId = "0",
            CorrelationId = "debug",
            InputJson = inputJson
        };

        _logger.LogDebug("Invoking Tool '{ToolName}' standalone", toolName);
        
        try 
        {
            return await tool.ExecuteAsync(context, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool '{ToolName}' failed during standalone invocation", toolName);
            return ToolResult.Failure("EXT_ERR", ex.Message);
        }
    }
}
