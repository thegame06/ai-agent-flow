using AgentFlow.Domain.Aggregates;
using Result = AgentFlow.Abstractions.Result;

namespace AgentFlow.Domain.Repositories;

public interface IPolicyRepository
{
    Task<PolicySetDefinition?> GetByIdAsync(string id, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<PolicySetDefinition>> GetByTenantAsync(string tenantId, bool onlyPublished = false, CancellationToken ct = default);
    Task<Result> AddAsync(PolicySetDefinition policySet, CancellationToken ct = default);
    Task<Result> UpdateAsync(PolicySetDefinition policySet, CancellationToken ct = default);
    Task<PolicySetDefinition?> GetLatestPublishedAsync(string tenantId, CancellationToken ct = default);
}
