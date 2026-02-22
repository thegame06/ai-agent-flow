using AgentFlow.Abstractions;

namespace AgentFlow.Evaluation;

// =========================================================================
// CANARY ROUTING SERVICE
// =========================================================================

/// <summary>
/// Determines whether a request should be routed to the canary version
/// of an agent based on canaryWeight and deterministic hashing.
/// </summary>
public interface ICanaryRoutingService
{
    /// <summary>
    /// Decides if the request should go to the canary version.
    /// Uses deterministic hash of requestId to ensure consistency.
    /// </summary>
    /// <param name="agentDefinitionId">Primary agent ID</param>
    /// <param name="canaryAgentId">Canary agent ID (new version)</param>
    /// <param name="canaryWeight">Weight 0.0-1.0 (e.g., 0.10 = 10% traffic)</param>
    /// <param name="requestId">Unique request ID for deterministic routing</param>
    /// <returns>The agent ID to execute (primary or canary)</returns>
    string SelectAgentForExecution(
        string agentDefinitionId,
        string? canaryAgentId,
        double canaryWeight,
        string requestId);

    /// <summary>
    /// Check if canary routing is active for an agent.
    /// </summary>
    bool IsCanaryActive(string? canaryAgentId, double canaryWeight);
}

/// <summary>
/// Canary Routing Decision with rationale for audit trail.
/// </summary>
public sealed record CanaryRoutingDecision
{
    public required string SelectedAgentId { get; init; }
    public required bool IsCanaryExecution { get; init; }
    public required string Reason { get; init; }
    public required double CanaryWeight { get; init; }
    public required string RequestHash { get; init; }
}

/// <summary>
/// Deterministic canary routing using consistent hashing.
/// Ensures same requestId always routes to same version (idempotency).
/// </summary>
public sealed class CanaryRoutingService : ICanaryRoutingService
{
    public string SelectAgentForExecution(
        string agentDefinitionId,
        string? canaryAgentId,
        double canaryWeight,
        string requestId)
    {
        // No canary configured
        if (string.IsNullOrWhiteSpace(canaryAgentId) || canaryWeight <= 0.0)
            return agentDefinitionId;

        // Canary weight is 100% → always use canary
        if (canaryWeight >= 1.0)
            return canaryAgentId;

        // Deterministic hash-based routing
        var hash = GetDeterministicHash(requestId);
        var normalizedHash = (double)hash / uint.MaxValue; // 0.0 - 1.0

        // If hash falls within canary weight range → canary
        return normalizedHash < canaryWeight
            ? canaryAgentId
            : agentDefinitionId;
    }

    public bool IsCanaryActive(string? canaryAgentId, double canaryWeight)
    {
        return !string.IsNullOrWhiteSpace(canaryAgentId) && canaryWeight > 0.0;
    }

    /// <summary>
    /// Creates a more detailed routing decision with audit information.
    /// </summary>
    public CanaryRoutingDecision SelectWithRationale(
        string agentDefinitionId,
        string? canaryAgentId,
        double canaryWeight,
        string requestId)
    {
        if (string.IsNullOrWhiteSpace(canaryAgentId) || canaryWeight <= 0.0)
        {
            return new CanaryRoutingDecision
            {
                SelectedAgentId = agentDefinitionId,
                IsCanaryExecution = false,
                Reason = "No canary configured",
                CanaryWeight = 0.0,
                RequestHash = "N/A"
            };
        }

        if (canaryWeight >= 1.0)
        {
            return new CanaryRoutingDecision
            {
                SelectedAgentId = canaryAgentId,
                IsCanaryExecution = true,
                Reason = "Canary weight is 100%",
                CanaryWeight = canaryWeight,
                RequestHash = "N/A"
            };
        }

        var hash = GetDeterministicHash(requestId);
        var normalizedHash = (double)hash / uint.MaxValue;
        var isCanary = normalizedHash < canaryWeight;

        return new CanaryRoutingDecision
        {
            SelectedAgentId = isCanary ? canaryAgentId : agentDefinitionId,
            IsCanaryExecution = isCanary,
            Reason = $"Deterministic hash routing: {normalizedHash:F4} {(isCanary ? "<" : ">=")} {canaryWeight:F4}",
            CanaryWeight = canaryWeight,
            RequestHash = hash.ToString("X8")
        };
    }

    /// <summary>
    /// FNV-1a hash for deterministic distribution.
    /// Same input always produces same hash (idempotent).
    /// </summary>
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
