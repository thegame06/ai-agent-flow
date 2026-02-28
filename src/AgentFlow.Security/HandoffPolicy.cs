using Microsoft.Extensions.Configuration;

namespace AgentFlow.Security;

public interface IManagerHandoffPolicy
{
    bool IsAllowed(string tenantId, string sourceAgentId, string targetAgentId);
    IReadOnlyList<string> GetAllowedTargets(string tenantId, string sourceAgentId);
}

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

    public bool IsAllowed(string tenantId, string sourceAgentId, string targetAgentId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(sourceAgentId) ||
            string.IsNullOrWhiteSpace(targetAgentId))
            return false;

        if (string.Equals(sourceAgentId, targetAgentId, StringComparison.OrdinalIgnoreCase))
            return false;

        var managerSection = _configuration.GetSection($"HandoffPolicy:Tenants:{tenantId}:Managers:{sourceAgentId}");
        if (!managerSection.Exists())
        {
            // Backward-compatible default: if no policy configured, allow.
            return true;
        }

        var allowedTargets = managerSection.Get<string[]>() ?? Array.Empty<string>();
        return allowedTargets.Any(x => string.Equals(x, targetAgentId, StringComparison.OrdinalIgnoreCase));
    }
}
