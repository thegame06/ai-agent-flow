using AgentFlow.Observability;
using System.Diagnostics.Metrics;

namespace AgentFlow.Core.Engine;

public interface IExecutionGovernancePolicy
{
    bool IsCostAllowed(string tenantId, string flow, double estimatedCostUsd, out string? denialReason);
    void RecordFallback(
        string policy,
        string decision,
        string? tenantId = null,
        string? flow = null,
        string? provider = null,
        string? model = null,
        double? estimatedCostUsd = null);
}

public sealed class ExecutionGovernancePolicy : IExecutionGovernancePolicy
{
    private readonly double _maxExecutionCostUsd;

    public ExecutionGovernancePolicy(double maxExecutionCostUsd = 0.50d)
    {
        _maxExecutionCostUsd = maxExecutionCostUsd <= 0 ? 0.50d : maxExecutionCostUsd;
    }

    public bool IsCostAllowed(string tenantId, string flow, double estimatedCostUsd, out string? denialReason)
    {
        if (estimatedCostUsd <= _maxExecutionCostUsd)
        {
            denialReason = null;
            return true;
        }

        denialReason = $"execution_cost_exceeded:{estimatedCostUsd:F4}>{_maxExecutionCostUsd:F4}";
        RecordFallback(
            "execution_cost_guardrail",
            "deny",
            tenantId: tenantId,
            flow: flow,
            estimatedCostUsd: estimatedCostUsd);
        return false;
    }

    public void RecordFallback(
        string policy,
        string decision,
        string? tenantId = null,
        string? flow = null,
        string? provider = null,
        string? model = null,
        double? estimatedCostUsd = null)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new("policy", policy),
            new("decision", decision),
            new("tenant_id", tenantId),
            new("flow", flow),
            new("provider", provider),
            new("model", model),
            new("estimated_cost_usd", estimatedCostUsd)
        };
        AgentFlowTelemetry.ExecutionDenials.Add(1, tags.ToArray());
    }
}
