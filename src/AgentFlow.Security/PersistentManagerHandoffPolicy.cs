using Microsoft.Extensions.Logging;

namespace AgentFlow.Security;

public sealed class PersistentManagerHandoffPolicy : IManagerHandoffPolicy
{
    private readonly IIntentRoutingStore _store;
    private readonly IManagerHandoffPolicy _fallback;
    private readonly ILogger<PersistentManagerHandoffPolicy> _logger;

    public PersistentManagerHandoffPolicy(
        IIntentRoutingStore store,
        ConfigurationManagerHandoffPolicy fallback,
        ILogger<PersistentManagerHandoffPolicy> logger)
    {
        _store = store;
        _fallback = fallback;
        _logger = logger;
    }

    public IReadOnlyList<string> GetAllowedTargets(string tenantId, string sourceAgentId)
    {
        var rules = _store.GetRulesAsync(tenantId).GetAwaiter().GetResult();
        var targets = rules
            .Where(x => x.Enabled && string.Equals(x.SourceAgentId, sourceAgentId, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TargetAgentId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToArray();

        if (targets.Length == 0)
        {
            _logger.LogDebug("No persisted handoff rules found for {Tenant}/{SourceAgent}; using fallback policy", tenantId, sourceAgentId);
            return _fallback.GetAllowedTargets(tenantId, sourceAgentId);
        }

        return targets;
    }

    public HandoffPolicyDecision Evaluate(string tenantId, string sourceAgentId, string targetAgentId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(sourceAgentId) || string.IsNullOrWhiteSpace(targetAgentId))
            return new HandoffPolicyDecision(false, "invalid_input", false, Array.Empty<string>());

        if (string.Equals(sourceAgentId, targetAgentId, StringComparison.OrdinalIgnoreCase))
            return new HandoffPolicyDecision(false, "self_handoff_not_allowed", false, Array.Empty<string>());

        var targets = GetAllowedTargets(tenantId, sourceAgentId);
        var allowed = targets.Any(x => string.Equals(x, targetAgentId, StringComparison.OrdinalIgnoreCase));

        return allowed
            ? new HandoffPolicyDecision(true, "target_in_persisted_rules", true, targets)
            : new HandoffPolicyDecision(false, "target_not_in_persisted_rules", true, targets);
    }

    public bool IsAllowed(string tenantId, string sourceAgentId, string targetAgentId)
        => Evaluate(tenantId, sourceAgentId, targetAgentId).Allowed;
}
