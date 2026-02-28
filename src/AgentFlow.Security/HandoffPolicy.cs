using Microsoft.Extensions.Configuration;

namespace AgentFlow.Security;

public interface IManagerHandoffPolicy
{
    bool IsAllowed(string tenantId, string sourceAgentId, string targetAgentId);
    IReadOnlyList<string> GetAllowedTargets(string tenantId, string sourceAgentId);
    HandoffPolicyDecision Evaluate(string tenantId, string sourceAgentId, string targetAgentId);
}

public sealed record HandoffPolicyDecision(bool Allowed, string Reason, bool HasExplicitPolicy, IReadOnlyList<string> AllowedTargets);

/// <summary>
/// Tenant-scoped handoff allowlist policy.
/// Configuration schema:
/// HandoffPolicy:
///   Tenants:
///     <tenantId>:
///       Managers:
///         <sourceAgentId>: ["targetAgentA", "targetAgentB"]
/// </summary>
public sealed class ConfigurationManagerHandoffPolicy : IManagerHandoffPolicy
{
    private readonly IConfiguration _configuration;

    public ConfigurationManagerHandoffPolicy(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IReadOnlyList<string> GetAllowedTargets(string tenantId, string sourceAgentId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(sourceAgentId))
            return Array.Empty<string>();

        var managerSection = _configuration.GetSection($"HandoffPolicy:Tenants:{tenantId}:Managers:{sourceAgentId}");
        if (!managerSection.Exists())
            return Array.Empty<string>();

        return managerSection.Get<string[]>() ?? Array.Empty<string>();
    }

    public HandoffPolicyDecision Evaluate(string tenantId, string sourceAgentId, string targetAgentId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(sourceAgentId) ||
            string.IsNullOrWhiteSpace(targetAgentId))
            return new HandoffPolicyDecision(false, "invalid_input", false, Array.Empty<string>());

        if (string.Equals(sourceAgentId, targetAgentId, StringComparison.OrdinalIgnoreCase))
            return new HandoffPolicyDecision(false, "self_handoff_not_allowed", false, Array.Empty<string>());

        var targets = GetAllowedTargets(tenantId, sourceAgentId);
        var managerSection = _configuration.GetSection($"HandoffPolicy:Tenants:{tenantId}:Managers:{sourceAgentId}");
        var hasExplicitPolicy = managerSection.Exists();

        if (!hasExplicitPolicy)
            return new HandoffPolicyDecision(true, "no_explicit_policy_allow", false, targets);

        var allowed = targets.Any(x => string.Equals(x, targetAgentId, StringComparison.OrdinalIgnoreCase));
        return allowed
            ? new HandoffPolicyDecision(true, "target_in_allowlist", true, targets)
            : new HandoffPolicyDecision(false, "target_not_in_allowlist", true, targets);
    }

    public bool IsAllowed(string tenantId, string sourceAgentId, string targetAgentId)
    {
        return Evaluate(tenantId, sourceAgentId, targetAgentId).Allowed;
    }
}
