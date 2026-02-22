using AgentFlow.Abstractions;
using AgentFlow.Domain.Common;

namespace AgentFlow.Domain.Repositories;

/// <summary>
/// Base repository interface.
/// NOTE: All implementations MUST enforce TenantId filtering.
/// Never expose a variant without tenantId parameter.
/// </summary>
public interface IRepository<TAggregate> where TAggregate : class
{
    Task<TAggregate?> GetByIdAsync(string id, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<TAggregate>> GetAllAsync(string tenantId, int skip = 0, int limit = 50, CancellationToken ct = default);
    Task<Result> InsertAsync(TAggregate aggregate, CancellationToken ct = default);
    Task<Result> UpdateAsync(TAggregate aggregate, CancellationToken ct = default);
    Task<Result> DeleteAsync(string id, string tenantId, CancellationToken ct = default);
    Task<bool> ExistsAsync(string id, string tenantId, CancellationToken ct = default);
}

/// <summary>
/// Extension for aggregates that need cross-tenant queries (platform-admin only).
/// DANGER: Usage must be explicitly audited. Only allowed for TenantTier.Platform.
/// </summary>
public interface ICrossTenatReadRepository<TAggregate> where TAggregate : class
{
    [Obsolete("CROSS-TENANT: Only used by platform admins with explicit audit logging.")]
    Task<IReadOnlyList<TAggregate>> GetAcrossTenantsAsync(int skip = 0, int limit = 50, CancellationToken ct = default);
}
