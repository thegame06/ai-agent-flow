using AgentFlow.Domain.Aggregates;
using AgentFlow.Abstractions;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Repositories;
using AgentFlow.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace AgentFlow.Infrastructure.Repositories;

public sealed class AgentDefinitionRepository : MongoRepositoryBase<AgentDefinition>, IAgentDefinitionRepository
{
    private static readonly FilterDefinitionBuilder<AgentDefinition> F = Builders<AgentDefinition>.Filter;

    public AgentDefinitionRepository(IMongoDatabase database, ILogger<AgentDefinitionRepository> logger)
        : base(database, "agent_definitions", logger) { }

    protected override FilterDefinition<AgentDefinition> TenantFilter(string tenantId)
        => F.And(F.Eq(a => a.TenantId, tenantId), F.Eq(a => a.IsDeleted, false));

    protected override FilterDefinition<AgentDefinition> IdAndTenantFilter(string id, string tenantId)
        => F.And(F.Eq(a => a.Id, id), F.Eq(a => a.TenantId, tenantId), F.Eq(a => a.IsDeleted, false));

    protected override FilterDefinition<AgentDefinition> GetReplaceFilter(AgentDefinition entity)
        => F.And(F.Eq(a => a.Id, entity.Id), F.Eq(a => a.TenantId, entity.TenantId));

    public async Task<AgentDefinition?> GetByNameAsync(string name, string tenantId, CancellationToken ct = default)
    {
        var filter = F.And(
            F.Eq(a => a.TenantId, tenantId),
            F.Eq(a => a.Name, name),
            F.Eq(a => a.IsDeleted, false));

        return await Collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<AgentDefinition>> GetPublishedAsync(string tenantId, CancellationToken ct = default)
    {
        var filter = F.And(
            F.Eq(a => a.TenantId, tenantId),
            F.Eq(a => a.Status, AgentStatus.Published),
            F.Eq(a => a.IsDeleted, false));

        var results = await Collection.Find(filter).ToListAsync(ct);
        return results.AsReadOnly();
    }

    public async Task<int> CountAsync(string tenantId, CancellationToken ct = default)
    {
        var filter = F.And(F.Eq(a => a.TenantId, tenantId), F.Eq(a => a.IsDeleted, false));
        return (int)await Collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    /// <summary>
    /// Called at startup to ensure MongoDB indices are correct.
    /// CRITICAL: TenantId must be the leading key in all compound indices.
    /// </summary>
    public static async Task EnsureIndexesAsync(IMongoCollection<AgentDefinition> collection)
    {
        var indexBuilder = Builders<AgentDefinition>.IndexKeys;
        var indexes = new[]
        {
            new CreateIndexModel<AgentDefinition>(
                indexBuilder.Combine(
                    indexBuilder.Ascending(a => a.TenantId),
                    indexBuilder.Ascending(a => a.Status),
                    indexBuilder.Descending(a => a.CreatedAt)),
                new CreateIndexOptions { Name = "tenantId_status_createdAt" }),

            new CreateIndexModel<AgentDefinition>(
                indexBuilder.Combine(
                    indexBuilder.Ascending(a => a.TenantId),
                    indexBuilder.Ascending(a => a.Name)),
                new CreateIndexOptions<AgentDefinition>
                {
                    Unique = true,
                    Name = "tenantId_name_unique",
                    PartialFilterExpression = Builders<AgentDefinition>.Filter.Eq(a => a.IsDeleted, false)
                }),
        };

        await collection.Indexes.CreateManyAsync(indexes);
    }
}

public sealed class AgentExecutionRepository : MongoRepositoryBase<AgentExecution>, IAgentExecutionRepository
{
    private static readonly FilterDefinitionBuilder<AgentExecution> F = Builders<AgentExecution>.Filter;
    private static readonly UpdateDefinitionBuilder<AgentExecution> U = Builders<AgentExecution>.Update;

    public AgentExecutionRepository(IMongoDatabase database, ILogger<AgentExecutionRepository> logger)
        : base(database, "agent_executions", logger) { }

    protected override FilterDefinition<AgentExecution> TenantFilter(string tenantId)
        => F.Eq(e => e.TenantId, tenantId);

    protected override FilterDefinition<AgentExecution> IdAndTenantFilter(string id, string tenantId)
        => F.And(F.Eq(e => e.Id, id), F.Eq(e => e.TenantId, tenantId));

    protected override FilterDefinition<AgentExecution> GetReplaceFilter(AgentExecution entity)
        => F.And(F.Eq(e => e.Id, entity.Id), F.Eq(e => e.TenantId, entity.TenantId));

    /// <summary>
    /// Atomic append of a step to an execution.
    /// Uses $push to prevent race conditions.
    /// The execution aggregate in-memory already validated the append;
    /// this persists it atomically.
    /// </summary>
    public async Task<Result> AppendStepAsync(
        string executionId,
        string tenantId,
        AgentStep step,
        CancellationToken ct = default)
    {
        var filter = F.And(
            F.Eq(e => e.Id, executionId),
            F.Eq(e => e.TenantId, tenantId),
            F.Eq(e => e.Status, ExecutionStatus.Running));

        var update = U.Push(e => e.Steps, step)
                      .Inc(e => e.CurrentIteration, step.StepType == StepType.Think ? 1 : 0)
                      .Set(e => e.UpdatedAt, DateTimeOffset.UtcNow);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: ct);

        return result.MatchedCount > 0
            ? Result.Success()
            : Result.Failure(Error.NotFound($"Running execution {executionId}"));
    }

    public async Task<IReadOnlyList<AgentExecution>> GetByAgentIdAsync(
        string agentId, string tenantId, int limit = 20, CancellationToken ct = default)
    {
        var filter = F.And(
            F.Eq(e => e.TenantId, tenantId),
            F.Eq(e => e.AgentDefinitionId, agentId));

        var results = await Collection.Find(filter)
            .SortByDescending(e => e.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyList<AgentExecution>> GetByStatusAsync(
        ExecutionStatus status, string tenantId, CancellationToken ct = default)
    {
        var filter = F.And(F.Eq(e => e.TenantId, tenantId), F.Eq(e => e.Status, status));
        var results = await Collection.Find(filter).ToListAsync(ct);
        return results.AsReadOnly();
    }

    public async Task<IReadOnlyList<AgentExecution>> GetRunningAsync(
        string tenantId, CancellationToken ct = default)
        => await GetByStatusAsync(ExecutionStatus.Running, tenantId, ct);

    public async Task<long> GetExecutionCountForDayAsync(
        string tenantId, DateTimeOffset date, CancellationToken ct = default)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);

        var filter = F.And(
            F.Eq(e => e.TenantId, tenantId),
            F.Gte(e => e.CreatedAt, startOfDay),
            F.Lt(e => e.CreatedAt, endOfDay));

        return await Collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public static async Task EnsureIndexesAsync(IMongoCollection<AgentExecution> collection)
    {
        var ib = Builders<AgentExecution>.IndexKeys;
        var indexes = new[]
        {
            new CreateIndexModel<AgentExecution>(
                ib.Combine(
                    ib.Ascending(e => e.TenantId),
                    ib.Descending(e => e.CreatedAt)),
                new CreateIndexOptions { Name = "tenantId_createdAt" }),

            new CreateIndexModel<AgentExecution>(
                ib.Combine(
                    ib.Ascending(e => e.TenantId),
                    ib.Ascending(e => e.Status),
                    ib.Descending(e => e.CreatedAt)),
                new CreateIndexOptions { Name = "tenantId_status_createdAt" }),

            new CreateIndexModel<AgentExecution>(
                ib.Combine(
                    ib.Ascending(e => e.TenantId),
                    ib.Ascending(e => e.AgentDefinitionId),
                    ib.Descending(e => e.CreatedAt)),
                new CreateIndexOptions { Name = "tenantId_agentId_createdAt" }),

            new CreateIndexModel<AgentExecution>(
                ib.Ascending(e => e.CorrelationId),
                new CreateIndexOptions { Name = "correlationId" }),
        };

        await collection.Indexes.CreateManyAsync(indexes);
    }
}
