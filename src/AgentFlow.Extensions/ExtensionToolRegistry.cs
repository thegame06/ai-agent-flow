using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Extensions;

/// <summary>
/// Bridge between the AgentFlow core engine and the extension system.
/// Implements the IToolRegistry interface required by the executor.
/// </summary>
public sealed class ExtensionToolRegistry : IToolRegistry
{
    private readonly IExtensionRegistry _registry;
    private readonly ILogger<ExtensionToolRegistry> _logger;

    public ExtensionToolRegistry(IExtensionRegistry registry, ILogger<ExtensionToolRegistry> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public void Register(IToolPlugin tool)
    {
        _registry.RegisterTool(tool);
    }

    public IToolPlugin? Resolve(string name, string? version = null)
    {
        var tool = _registry.GetTool(name);
        
        // If version is specified, check it. (Simple semver match for now)
        if (tool != null && version != null && tool.Version != version)
        {
            _logger.LogWarning("Tool '{ToolName}' found but version mismatch. Requested: {Requested}, Found: {Found}", 
                name, version, tool.Version);
            return null;
        }

        return tool;
    }

    public IReadOnlyList<IToolPlugin> GetAll(string tenantId)
    {
        // For now, return all tools from the current process.
        // In the future, this will filter by tenant assignments in Mongo.
        return _registry.GetTools();
    }

    public Task<bool> IsAuthorizedAsync(string toolName, string tenantId, string userId, CancellationToken ct = default)
    {
        // Baseline: Always true for Phase 2 system-level exploration.
        // Will be replaced by Policy-based RBAC in Phase 3.
        return Task.FromResult(true);
    }
}
