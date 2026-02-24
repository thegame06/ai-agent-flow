using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentFlow.ModelRouting;

// =========================================================================
// MODEL REGISTRY
// =========================================================================

public interface IModelRegistry
{
    void Register(IModelProvider provider);
    IModelProvider? GetProvider(string modelId);
    IReadOnlyList<string> GetAvailableModelIds();
    IReadOnlyList<IModelProvider> GetProviders();
    bool Remove(string modelId);
    Task<IReadOnlyList<string>> GetHealthyModelIdsAsync(CancellationToken ct = default);
}

public sealed class InMemoryModelRegistry : IModelRegistry
{
    private readonly Dictionary<string, IModelProvider> _providers = new();
    private readonly object _sync = new();

    public void Register(IModelProvider provider)
    {
        lock (_sync)
        {
            _providers[provider.ModelId] = provider;
        }
    }

    public IModelProvider? GetProvider(string modelId)
    {
        lock (_sync)
        {
            return _providers.GetValueOrDefault(modelId);
        }
    }

    public IReadOnlyList<string> GetAvailableModelIds()
    {
        lock (_sync)
        {
            return [.. _providers.Keys];
        }
    }

    public IReadOnlyList<IModelProvider> GetProviders()
    {
        lock (_sync)
        {
            return [.. _providers.Values];
        }
    }

    public bool Remove(string modelId)
    {
        lock (_sync)
        {
            return _providers.Remove(modelId);
        }
    }

    public async Task<IReadOnlyList<string>> GetHealthyModelIdsAsync(CancellationToken ct = default)
    {
        var healthy = new List<string>();
        foreach (var (id, provider) in _providers)
        {
            try
            {
                if (await provider.IsHealthyAsync(ct))
                    healthy.Add(id);
            }
            catch { /* unhealthy providers are excluded */ }
        }
        return healthy;
    }
}

// =========================================================================
// MODEL ROUTER IMPLEMENTATIONS
// =========================================================================

/// <summary>
/// Routes model selection based on strategy:
/// - Static: always use default
/// - Task-based: match taskType to routing rules
/// - Policy-based: evaluate policy context
/// - FallbackChain: try each in order until healthy
/// </summary>
public sealed class ModelRouter : IModelRouter
{
    private readonly IModelRegistry _registry;
    private readonly ILogger<ModelRouter> _logger;

    public ModelRouter(IModelRegistry registry, ILogger<ModelRouter> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<ModelSelection> SelectModelAsync(
        ModelRoutingRequest request,
        CancellationToken ct = default)
    {
        var config = request.Config;

        return config.Strategy switch
        {
            ModelRoutingStrategy.TaskBased    => await SelectByTaskAsync(config, request.TaskType, ct),
            ModelRoutingStrategy.FallbackChain => await SelectByFallbackChainAsync(config, ct),
            _                                 => await SelectStaticAsync(config, ct)
        };
    }

    private async Task<ModelSelection> SelectStaticAsync(
        ModelRoutingConfig config,
        CancellationToken ct)
    {
        // 1. Try default model
        var provider = _registry.GetProvider(config.DefaultModelId);
        
        if (provider is not null)
        {
            try 
            {
                if (await provider.IsHealthyAsync(ct))
                {
                    return new ModelSelection
                    {
                        ModelId = config.DefaultModelId,
                        Provider = provider,
                        Reason = "Default configuration (Healthy)"
                    };
                }
                _logger.LogWarning("Default model '{ModelId}' is unhealthy. Entering panic mode.", 
                    config.DefaultModelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed for default model '{ModelId}'", config.DefaultModelId);
            }
        }
        else
        {
            _logger.LogCritical("Default model '{ModelId}' is not registered! This is a configuration error.", 
                config.DefaultModelId);
        }

        // 2. PANIC MODE: Find ANY healthy provider
        var healthyIds = await _registry.GetHealthyModelIdsAsync(ct);
        var saviourId = healthyIds.FirstOrDefault();

        if (saviourId is not null)
        {
            var saviour = _registry.GetProvider(saviourId)!;
            _logger.LogWarning("PANIC MODE ACTIVATED: Routing to '{SaviourId}' because default is broken.", saviourId);
            
            return new ModelSelection
            {
                ModelId = saviourId,
                Provider = saviour,
                IsFallback = true,
                FallbackReason = "PANIC: Default model unavailable",
                Reason = "Panic Mode Selection"
            };
        }

        throw new InvalidOperationException(
            $"Routing failure: Default model '{config.DefaultModelId}' is dead and no healthy substitutes found.");
    }

    private async Task<ModelSelection> SelectByTaskAsync(
        ModelRoutingConfig config,
        string? taskType,
        CancellationToken ct)
    {
        if (taskType is not null)
        {
            var rule = config.RoutingRules.FirstOrDefault(r => r.TaskType == taskType);
            if (rule is not null)
            {
                var provider = _registry.GetProvider(rule.ModelId);
                if (provider is not null && await provider.IsHealthyAsync(ct))
                {
                    _logger.LogDebug("Task-based routing: taskType='{TaskType}' → model='{ModelId}'",
                        taskType, rule.ModelId);
                    return new ModelSelection { ModelId = rule.ModelId, Provider = provider };
                }
            }
        }

        // Fallback to static if no rule matches or model is unhealthy
        return await SelectStaticAsync(config, ct);
    }

    private async Task<ModelSelection> SelectByFallbackChainAsync(
        ModelRoutingConfig config,
        CancellationToken ct)
    {
        var chain = new[] { config.DefaultModelId }.Concat(config.FallbackChain).Distinct();

        foreach (var modelId in chain)
        {
            var provider = _registry.GetProvider(modelId);
            if (provider is null) continue;

            try
            {
                if (await provider.IsHealthyAsync(ct))
                {
                    var isFallback = modelId != config.DefaultModelId;

                    if (isFallback)
                        _logger.LogWarning(
                            "Primary model unavailable. Falling back to '{ModelId}'", modelId);

                    return new ModelSelection
                    {
                        ModelId = modelId,
                        Provider = provider,
                        IsFallback = isFallback,
                        FallbackReason = isFallback ? "Primary model health check failed" : null
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health check failed for model '{ModelId}'", modelId);
            }
        }

        throw new InvalidOperationException(
            $"All models in fallback chain are unavailable. Chain: [{string.Join(", ", chain)}]");
    }
}

// =========================================================================
// NULL/STUB PROVIDER (for tests)
// =========================================================================

public sealed class StubModelProvider : IModelProvider
{
    public string ProviderId { get; }
    public string ModelId { get; }
    public ModelMetadata Metadata { get; init; }
    private readonly Func<LlmRequest, LlmResponse> _handler;
    private readonly Func<CancellationToken, Task<bool>> _healthCheck;

    public StubModelProvider(
        string modelId,
        string providerId = "stub",
        Func<LlmRequest, LlmResponse>? handler = null,
        Func<CancellationToken, Task<bool>>? healthCheck = null)
    {
        ModelId = modelId;
        ProviderId = providerId;
        Metadata = new ModelMetadata
        {
            DisplayName = modelId,
            CostPer1KTokens = 0.001,
            MaxContextTokens = 128000,
            Tier = "Secondary"
        };
        _handler = handler ?? (_ => new LlmResponse
        {
            Content = """{"decision": "ProvideFinalAnswer", "finalAnswer": "Stub response", "rationale": "stub"}""",
            InputTokens = 100,
            OutputTokens = 50,
            ModelId = modelId
        });
        _healthCheck = healthCheck ?? (_ => Task.FromResult(true));
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default) =>
        Task.FromResult(_handler(request));

    public Task<bool> IsHealthyAsync(CancellationToken ct = default) =>
        _healthCheck(ct);
}
