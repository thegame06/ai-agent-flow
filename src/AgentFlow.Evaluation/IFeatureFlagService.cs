using AgentFlow.Abstractions;

namespace AgentFlow.Evaluation;

// =========================================================================
// FEATURE FLAG SERVICE — Experimentation Layer
// =========================================================================

/// <summary>
/// Manages feature flags for agents, enabling/disabling features
/// at tenant, agent, or execution level without code deployment.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// Check if a feature is enabled for a specific context.
    /// </summary>
    Task<bool> IsEnabledAsync(
        string tenantId,
        string featureFlagKey,
        FeatureFlagContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Get all enabled features for a specific context.
    /// </summary>
    Task<IReadOnlyList<string>> GetEnabledFeaturesAsync(
        string tenantId,
        FeatureFlagContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Create or update a feature flag.
    /// </summary>
    Task<Result> SetFeatureFlagAsync(
        string tenantId,
        FeatureFlagDefinition definition,
        CancellationToken ct = default);
}

/// <summary>
/// Context for evaluating feature flags with segment-based rules.
/// </summary>
public sealed record FeatureFlagContext
{
    public string? AgentId { get; init; }
    public string? UserId { get; init; }
    public string? ExecutionId { get; init; }
    public IReadOnlyList<string> UserSegments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } 
        = new Dictionary<string, string>();
}

/// <summary>
/// Feature flag definition with segment-based targeting.
/// </summary>
public sealed record FeatureFlagDefinition
{
    public required string FlagKey { get; init; }
    public required string TenantId { get; init; }
    public required string Description { get; init; }
    public required bool IsEnabled { get; init; }
    public required FeatureFlagTargeting Targeting { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; init; } = string.Empty;
}

public sealed record FeatureFlagTargeting
{
    /// <summary>
    /// If specified, only applies to these agent IDs.
    /// Empty = all agents.
    /// </summary>
    public IReadOnlyList<string> AgentIds { get; init; } = [];

    /// <summary>
    /// If specified, only applies to users in these segments.
    /// Empty = all users.
    /// </summary>
    public IReadOnlyList<string> UserSegments { get; init; } = [];

    /// <summary>
    /// Percentage rollout (0.0-1.0). Default 1.0 = 100%.
    /// Uses deterministic hashing of userId to ensure consistency.
    /// </summary>
    public double RolloutPercentage { get; init; } = 1.0;
}

/// <summary>
/// In-memory implementation for development/testing.
/// Production should use a persistent store (MongoDB, Redis, etc.).
/// </summary>
public sealed class InMemoryFeatureFlagService : IFeatureFlagService
{
    private readonly Dictionary<string, Dictionary<string, FeatureFlagDefinition>> _flags = new();

    public Task<bool> IsEnabledAsync(
        string tenantId,
        string featureFlagKey,
        FeatureFlagContext context,
        CancellationToken ct = default)
    {
        if (!_flags.TryGetValue(tenantId, out var tenantFlags))
            return Task.FromResult(false);

        if (!tenantFlags.TryGetValue(featureFlagKey, out var flag))
            return Task.FromResult(false);

        if (!flag.IsEnabled)
            return Task.FromResult(false);

        // Check agent targeting
        if (flag.Targeting.AgentIds.Count > 0 
            && context.AgentId is not null 
            && !flag.Targeting.AgentIds.Contains(context.AgentId))
            return Task.FromResult(false);

        // Check segment targeting
        if (flag.Targeting.UserSegments.Count > 0 
            && !context.UserSegments.Any(s => flag.Targeting.UserSegments.Contains(s)))
            return Task.FromResult(false);

        // Check rollout percentage (deterministic based on userId)
        if (flag.Targeting.RolloutPercentage < 1.0 && context.UserId is not null)
        {
            var hash = GetDeterministicHash(context.UserId);
            var normalizedHash = (double)hash / uint.MaxValue;
            
            if (normalizedHash >= flag.Targeting.RolloutPercentage)
                return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<string>> GetEnabledFeaturesAsync(
        string tenantId,
        FeatureFlagContext context,
        CancellationToken ct = default)
    {
        if (!_flags.TryGetValue(tenantId, out var tenantFlags))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var enabledFeatures = new List<string>();

        foreach (var (key, _) in tenantFlags)
        {
            if (IsEnabledAsync(tenantId, key, context, ct).Result)
                enabledFeatures.Add(key);
        }

        return Task.FromResult<IReadOnlyList<string>>(enabledFeatures.AsReadOnly());
    }

    public Task<Result> SetFeatureFlagAsync(
        string tenantId,
        FeatureFlagDefinition definition,
        CancellationToken ct = default)
    {
        if (!_flags.ContainsKey(tenantId))
            _flags[tenantId] = new Dictionary<string, FeatureFlagDefinition>();

        _flags[tenantId][definition.FlagKey] = definition;
        return Task.FromResult(Result.Success());
    }

    private static uint GetDeterministicHash(string input)
    {
        const uint FnvPrime = 16777619;
        const uint FnvOffsetBasis = 2166136261;

        uint hash = FnvOffsetBasis;
        foreach (var c in input)
        {
            hash ^= c;
            hash *= FnvPrime;
        }
        return hash;
    }
}
