using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentFlow.ModelRouting;

public enum ModelRole
{
    Reasoning = 0,
    Embeddings = 1,
    Moderation = 2,
    SpeechToText = 3,
    TextToSpeech = 4
}

public sealed record ModelRoleRoutingRequest
{
    public required string TenantId { get; init; }
    public required ModelRole Role { get; init; }
    public required IReadOnlyList<string> CandidateModelIds { get; init; }
    public double? MaxCostPer1KTokensUsd { get; init; }
    public int? MinContextTokens { get; init; }
    public IReadOnlyList<string> PreferredProviders { get; init; } = [];
    public bool EnforceCostCeiling { get; init; } = true;
}

public interface IModelRoleOrchestrator
{
    Task<ModelSelection> ResolveAsync(ModelRoleRoutingRequest request, CancellationToken ct = default);
}

public sealed class ModelRoleOrchestrator : IModelRoleOrchestrator
{
    private static readonly IReadOnlyDictionary<ModelRole, ModelRolePolicyOptions> DefaultRolePolicies =
        new Dictionary<ModelRole, ModelRolePolicyOptions>
        {
            [ModelRole.Reasoning] = new() { MaxCostPer1KTokensUsd = 0.03, MinContextTokens = 8000, PreferredProviders = ["openai", "azureopenai"], EnforceCostCeiling = true },
            [ModelRole.Embeddings] = new() { MaxCostPer1KTokensUsd = 0.01, MinContextTokens = 1024, PreferredProviders = ["openai"], EnforceCostCeiling = true },
            [ModelRole.Moderation] = new() { MaxCostPer1KTokensUsd = 0.005, PreferredProviders = ["openai"], EnforceCostCeiling = true },
            [ModelRole.SpeechToText] = new() { MaxCostPer1KTokensUsd = 0.03, PreferredProviders = ["openai"], EnforceCostCeiling = false },
            [ModelRole.TextToSpeech] = new() { MaxCostPer1KTokensUsd = 0.03, PreferredProviders = ["openai"], EnforceCostCeiling = false }
        };

    private readonly IModelRegistry _registry;
    private readonly ILogger<ModelRoleOrchestrator> _logger;
    private readonly ModelRoleOrchestrationOptions _options;

    public ModelRoleOrchestrator(
        IModelRegistry registry,
        ILogger<ModelRoleOrchestrator> logger,
        IOptions<ModelRoleOrchestrationOptions> options)
    {
        _registry = registry;
        _logger = logger;
        _options = options.Value ?? new ModelRoleOrchestrationOptions();
    }

    public async Task<ModelSelection> ResolveAsync(ModelRoleRoutingRequest request, CancellationToken ct = default)
    {
        if (request.CandidateModelIds.Count == 0)
            throw new ArgumentException("At least one candidate model is required.", nameof(request));

        var rolePolicy = ResolveRolePolicy(request.Role);
        var effectiveMaxCost = request.MaxCostPer1KTokensUsd ?? rolePolicy.MaxCostPer1KTokensUsd;
        var effectiveMinContext = request.MinContextTokens ?? rolePolicy.MinContextTokens;
        var enforceCost = request.EnforceCostCeiling && rolePolicy.EnforceCostCeiling;
        var preferredProviders = request.PreferredProviders.Count > 0
            ? request.PreferredProviders
            : rolePolicy.PreferredProviders;

        var chain = request.CandidateModelIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sortedChain = chain
            .OrderBy(modelId => PreferredProviderRank(preferredProviders, _registry.GetProvider(modelId)?.ProviderId))
            .ToList();

        foreach (var modelId in sortedChain)
        {
            var provider = _registry.GetProvider(modelId);
            if (provider is null)
                continue;

            if (effectiveMinContext.HasValue && provider.Metadata.MaxContextTokens < effectiveMinContext.Value)
            {
                _logger.LogDebug("Model rejected by context policy. Role={Role} Model={ModelId} Required={Required} Available={Available}",
                    request.Role, modelId, effectiveMinContext.Value, provider.Metadata.MaxContextTokens);
                continue;
            }

            if (enforceCost && effectiveMaxCost.HasValue && provider.Metadata.CostPer1KTokens > effectiveMaxCost.Value)
            {
                _logger.LogDebug("Model rejected by cost policy. Role={Role} Model={ModelId} Max={MaxCost} Current={CurrentCost}",
                    request.Role, modelId, effectiveMaxCost.Value, provider.Metadata.CostPer1KTokens);
                continue;
            }

            try
            {
                if (!await provider.IsHealthyAsync(ct))
                {
                    _logger.LogDebug("Model unhealthy for role. Role={Role} Model={ModelId}", request.Role, modelId);
                    continue;
                }

                var isFallback = !string.Equals(modelId, sortedChain[0], StringComparison.OrdinalIgnoreCase);
                return new ModelSelection
                {
                    ModelId = modelId,
                    Provider = provider,
                    IsFallback = isFallback,
                    FallbackReason = isFallback ? "Primary model unavailable or policy-rejected" : null,
                    Reason = $"Role={request.Role};PolicyApplied=true"
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Model health check failed for role {Role}. Model={ModelId}", request.Role, modelId);
            }
        }

        throw new InvalidOperationException(
            $"No healthy model available for role '{request.Role}'. Chain: [{string.Join(", ", chain)}]");
    }

    private ModelRolePolicyOptions ResolveRolePolicy(ModelRole role)
    {
        if (_options.Roles.TryGetValue(role.ToString(), out var configured))
            return configured;

        if (DefaultRolePolicies.TryGetValue(role, out var rolePolicy))
            return rolePolicy;
        return new ModelRolePolicyOptions();
    }

    private static int PreferredProviderRank(IReadOnlyList<string> preferredProviders, string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return int.MaxValue;

        for (var i = 0; i < preferredProviders.Count; i++)
        {
            if (string.Equals(preferredProviders[i], providerId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return int.MaxValue - 1;
    }
}
