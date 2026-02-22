using System.Collections.Concurrent;
using System.Reflection;

namespace AgentFlow.ToolSDK;

/// <summary>
/// Registry for dynamically loading and managing tool plugins.
/// Supports both embedded plugins and external plugins loaded from assemblies.
/// </summary>
public sealed class PluginRegistry
{
    private readonly ConcurrentDictionary<string, IToolPlugin> _plugins = new();
    private readonly ConcurrentDictionary<string, PluginConfiguration> _configurations = new();

    /// <summary>
    /// Register a plugin instance directly.
    /// </summary>
    public async Task<Result> RegisterPluginAsync(
        IToolPlugin plugin, 
        PluginConfiguration? config = null,
        CancellationToken ct = default)
    {
        if (plugin == null) throw new ArgumentNullException(nameof(plugin));

        var pluginId = plugin.Metadata.Id;

        if (_plugins.ContainsKey(pluginId))
        {
            return Result.Failure(new Error("PLUGIN_ALREADY_REGISTERED", $"Plugin '{pluginId}' is already registered."));
        }

        // Initialize plugin if configuration provided
        if (config != null)
        {
            try
            {
                await plugin.InitializeAsync(config, ct);
                _configurations[pluginId] = config;
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("PLUGIN_INIT_FAILED", $"Plugin '{pluginId}' initialization failed: {ex.Message}"));
            }
        }

        _plugins[pluginId] = plugin;
        return Result.Success();
    }

    /// <summary>
    /// Load plugins from an external assembly.
    /// Scans for all types implementing IToolPlugin.
    /// </summary>
    public async Task<Result<int>> LoadPluginsFromAssemblyAsync(
        string assemblyPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(assemblyPath))
        {
            return Result<int>.Failure(new Error("ASSEMBLY_NOT_FOUND", $"Assembly not found: {assemblyPath}"));
        }

        try
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            var pluginTypes = assembly.GetTypes()
                .Where(t => typeof(IToolPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();

            var loadedCount = 0;

            foreach (var pluginType in pluginTypes)
            {
                try
                {
                    var instance = Activator.CreateInstance(pluginType) as IToolPlugin;
                    if (instance != null)
                    {
                        var result = await RegisterPluginAsync(instance, null, ct);
                        if (result.IsSuccess) loadedCount++;
                    }
                }
                catch
                {
                    // Skip plugins that fail to instantiate (might require constructor parameters)
                    continue;
                }
            }

            return Result<int>.Success(loadedCount);
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(new Error("ASSEMBLY_LOAD_FAILED", $"Failed to load assembly: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get a registered plugin by ID.
    /// </summary>
    public IToolPlugin? GetPlugin(string pluginId)
    {
        _plugins.TryGetValue(pluginId, out var plugin);
        return plugin;
    }

    /// <summary>
    /// Get all registered plugins.
    /// </summary>
    public IReadOnlyList<IToolPlugin> GetAllPlugins()
    {
        return _plugins.Values.ToList();
    }

    /// <summary>
    /// Check if a plugin is registered.
    /// </summary>
    public bool IsPluginRegistered(string pluginId)
    {
        return _plugins.ContainsKey(pluginId);
    }

    /// <summary>
    /// Unregister a plugin and dispose resources.
    /// </summary>
    public async Task<Result> UnregisterPluginAsync(string pluginId, CancellationToken ct = default)
    {
        if (!_plugins.TryRemove(pluginId, out var plugin))
        {
            return Result.Failure(new Error("PLUGIN_NOT_FOUND", $"Plugin '{pluginId}' not found."));
        }

        try
        {
            await plugin.DisposeAsync();
            _configurations.TryRemove(pluginId, out _);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("PLUGIN_DISPOSE_FAILED", $"Plugin '{pluginId}' disposal failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Execute a plugin by ID.
    /// </summary>
    public async Task<ToolResult> ExecutePluginAsync(
        string pluginId,
        ToolContext context,
        CancellationToken ct = default)
    {
        var plugin = GetPlugin(pluginId);
        if (plugin == null)
        {
            return ToolResult.FromError($"Plugin '{pluginId}' not found.", "PLUGIN_NOT_FOUND");
        }

        // Validate parameters first
        var validationResult = await plugin.ValidateAsync(context, ct);
        if (!validationResult.IsValid)
        {
            return ToolResult.FromError(
                $"Validation failed: {string.Join(", ", validationResult.Errors)}",
                "VALIDATION_FAILED");
        }

        // Execute plugin
        return await plugin.ExecuteAsync(context, ct);
    }

    /// <summary>
    /// Get metadata for all registered plugins (for marketplace/discovery).
    /// </summary>
    public IReadOnlyList<ToolMetadata> GetAllMetadata()
    {
        return _plugins.Values.Select(p => p.Metadata).ToList();
    }

    /// <summary>
    /// Search plugins by tag.
    /// </summary>
    public IReadOnlyList<IToolPlugin> SearchByTag(string tag)
    {
        return _plugins.Values
            .Where(p => p.Metadata.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Search plugins by risk level.
    /// </summary>
    public IReadOnlyList<IToolPlugin> GetPluginsByRiskLevel(ToolRiskLevel maxRiskLevel)
    {
        return _plugins.Values
            .Where(p => p.Metadata.RiskLevel <= maxRiskLevel)
            .ToList();
    }
}

// Simple Result type for SDK (not depending on Domain)
public record Result
{
    public bool IsSuccess { get; init; }
    public Error? Error { get; init; }

    public static Result Success() => new() { IsSuccess = true };
    public static Result Failure(Error error) => new() { IsSuccess = false, Error = error };
}

public record Result<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public Error? Error { get; init; }

    public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static Result<T> Failure(Error error) => new() { IsSuccess = false, Error = error };
}

public record Error(string Code, string Message);
