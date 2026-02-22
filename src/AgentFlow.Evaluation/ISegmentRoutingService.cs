using AgentFlow.Abstractions;

namespace AgentFlow.Evaluation;

// =========================================================================
// SEGMENT-BASED ROUTING SERVICE — Experimentation Layer
// =========================================================================

/// <summary>
/// Routes execution requests to different agent versions based on user segments.
/// More sophisticated than canary routing: allows multiple versions per segment.
/// 
/// Use Cases:
/// - Premium users → Advanced agent version
/// - Free users → Basic agent version  
/// - Beta testers → Experimental agent version
/// - Geographic regions → Localized agent versions
/// </summary>
public interface ISegmentRoutingService
{
    /// <summary>
    /// Selects the appropriate agent version based on user segments.
    /// Returns the original agentId if no segment routing is configured.
    /// </summary>
    Task<SegmentRoutingDecision> SelectAgentForSegmentAsync(
        string tenantId,
        string agentId,
        SegmentRoutingContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Configure segment routing rules for an agent.
    /// </summary>
    Task<Result> SetSegmentRoutingAsync(
        string tenantId,
        string agentId,
        SegmentRoutingConfiguration config,
        CancellationToken ct = default);

    /// <summary>
    /// Get current segment routing configuration for an agent.
    /// </summary>
    Task<SegmentRoutingConfiguration?> GetSegmentRoutingAsync(
        string tenantId,
        string agentId,
        CancellationToken ct = default);
}

/// <summary>
/// Context for evaluating segment routing with user characteristics.
/// </summary>
public sealed record SegmentRoutingContext
{
    public required string UserId { get; init; }
    public required IReadOnlyList<string> UserSegments { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } 
        = new Dictionary<string, string>();
}

/// <summary>
/// Segment routing configuration defining which segments get which agent versions.
/// </summary>
public sealed record SegmentRoutingConfiguration
{
    public required string AgentId { get; init; }
    public required string TenantId { get; init; }
    public required bool IsEnabled { get; init; }
    
    /// <summary>
    /// Routing rules evaluated in order. First match wins.
    /// </summary>
    public required IReadOnlyList<SegmentRoutingRule> Rules { get; init; }
    
    /// <summary>
    /// Default agent to use if no rules match.
    /// If null, uses the original agentId.
    /// </summary>
    public string? DefaultTargetAgentId { get; init; }
    
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; init; } = string.Empty;
}

/// <summary>
/// A single routing rule: if user matches these segments, route to this agent.
/// </summary>
public sealed record SegmentRoutingRule
{
    public required string RuleName { get; init; }
    public required IReadOnlyList<string> MatchSegments { get; init; }
    public required string TargetAgentId { get; init; }
    public int Priority { get; init; } = 0; // Higher = evaluated first
    public bool RequireAllSegments { get; init; } = false; // true = AND, false = OR
}

/// <summary>
/// Result of segment routing with rationale for audit.
/// </summary>
public sealed record SegmentRoutingDecision
{
    public required string SelectedAgentId { get; init; }
    public required bool WasRouted { get; init; }
    public SegmentRoutingRule? MatchedRule { get; init; }
    public required string Reason { get; init; }
    public required IReadOnlyList<string> EvaluatedSegments { get; init; }
}

/// <summary>
/// In-memory implementation for development/testing.
/// Production should use MongoDB for persistence.
/// </summary>
public sealed class InMemorySegmentRoutingService : ISegmentRoutingService
{
    private readonly Dictionary<string, Dictionary<string, SegmentRoutingConfiguration>> _configs = new();

    public Task<SegmentRoutingDecision> SelectAgentForSegmentAsync(
        string tenantId,
        string agentId,
        SegmentRoutingContext context,
        CancellationToken ct = default)
    {
        // No configuration for this tenant
        if (!_configs.TryGetValue(tenantId, out var tenantConfigs))
        {
            return Task.FromResult(new SegmentRoutingDecision
            {
                SelectedAgentId = agentId,
                WasRouted = false,
                Reason = "No segment routing configured for tenant",
                EvaluatedSegments = context.UserSegments
            });
        }

        // No configuration for this agent
        if (!tenantConfigs.TryGetValue(agentId, out var config))
        {
            return Task.FromResult(new SegmentRoutingDecision
            {
                SelectedAgentId = agentId,
                WasRouted = false,
                Reason = "No segment routing configured for this agent",
                EvaluatedSegments = context.UserSegments
            });
        }

        // Routing disabled
        if (!config.IsEnabled)
        {
            return Task.FromResult(new SegmentRoutingDecision
            {
                SelectedAgentId = agentId,
                WasRouted = false,
                Reason = "Segment routing is disabled",
                EvaluatedSegments = context.UserSegments
            });
        }

        // Evaluate rules in priority order
        var sortedRules = config.Rules.OrderByDescending(r => r.Priority).ToList();

        foreach (var rule in sortedRules)
        {
            var matches = rule.RequireAllSegments
                ? rule.MatchSegments.All(s => context.UserSegments.Contains(s))
                : rule.MatchSegments.Any(s => context.UserSegments.Contains(s));

            if (matches)
            {
                return Task.FromResult(new SegmentRoutingDecision
                {
                    SelectedAgentId = rule.TargetAgentId,
                    WasRouted = true,
                    MatchedRule = rule,
                    Reason = $"Matched rule '{rule.RuleName}' (priority {rule.Priority})",
                    EvaluatedSegments = context.UserSegments
                });
            }
        }

        // No rules matched - use default or original
        var finalAgentId = config.DefaultTargetAgentId ?? agentId;
        
        return Task.FromResult(new SegmentRoutingDecision
        {
            SelectedAgentId = finalAgentId,
            WasRouted = config.DefaultTargetAgentId is not null,
            Reason = config.DefaultTargetAgentId is not null
                ? "No rules matched, using default target"
                : "No rules matched, using original agent",
            EvaluatedSegments = context.UserSegments
        });
    }

    public Task<Result> SetSegmentRoutingAsync(
        string tenantId,
        string agentId,
        SegmentRoutingConfiguration config,
        CancellationToken ct = default)
    {
        if (!_configs.ContainsKey(tenantId))
            _configs[tenantId] = new Dictionary<string, SegmentRoutingConfiguration>();

        _configs[tenantId][agentId] = config;
        
        return Task.FromResult(Result.Success());
    }

    public Task<SegmentRoutingConfiguration?> GetSegmentRoutingAsync(
        string tenantId,
        string agentId,
        CancellationToken ct = default)
    {
        if (!_configs.TryGetValue(tenantId, out var tenantConfigs))
            return Task.FromResult<SegmentRoutingConfiguration?>(null);

        tenantConfigs.TryGetValue(agentId, out var config);
        return Task.FromResult(config);
    }
}
