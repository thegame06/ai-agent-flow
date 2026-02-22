using AgentFlow.Domain.Aggregates;
using Result = AgentFlow.Abstractions.Result;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace AgentFlow.Infrastructure.Repositories;

public sealed class PolicyRepository : MongoRepositoryBase<PolicySetDefinition>, IPolicyRepository
{
    private static readonly FilterDefinitionBuilder<PolicySetDefinition> F = Builders<PolicySetDefinition>.Filter;

    public PolicyRepository(IMongoDatabase database, ILogger<PolicyRepository> logger)
        : base(database, "policy_sets", logger) { }

    protected override FilterDefinition<PolicySetDefinition> TenantFilter(string tenantId)
        => F.Eq(p => p.TenantId, tenantId);

    protected override FilterDefinition<PolicySetDefinition> IdAndTenantFilter(string id, string tenantId)
        => F.And(F.Eq(p => p.Id, id), F.Eq(p => p.TenantId, tenantId));

    protected override FilterDefinition<PolicySetDefinition> GetReplaceFilter(PolicySetDefinition entity)
        => F.And(F.Eq(p => p.Id, entity.Id), F.Eq(p => p.TenantId, entity.TenantId));

    public async Task<IReadOnlyList<PolicySetDefinition>> GetByTenantAsync(
        string tenantId, 
        bool onlyPublished = false, 
        CancellationToken ct = default)
    {
        var filter = F.Eq(p => p.TenantId, tenantId);
        
        if (onlyPublished)
        {
            filter = F.And(filter, F.Eq(p => p.IsPublished, true));
        }

        var results = await Collection.Find(filter).ToListAsync(ct);
        return results.AsReadOnly();
    }

    public Task<Result> AddAsync(PolicySetDefinition policySet, CancellationToken ct = default)
        => InsertAsync(policySet, ct);

    public async Task<PolicySetDefinition?> GetLatestPublishedAsync(string tenantId, CancellationToken ct = default)
    {
        var filter = F.And(
            F.Eq(p => p.TenantId, tenantId),
            F.Eq(p => p.IsPublished, true));

        return await Collection
            .Find(filter)
            .SortByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }
}
