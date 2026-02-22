using AgentFlow.Abstractions;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;

namespace AgentFlow.Infrastructure.Repositories;

/// <summary>
/// Base MongoDB repository.
/// 
/// CRITICAL invariant: EVERY query includes { tenantId: Filter } as the FIRST filter.
/// MongoDB partial index on TenantId ensures query plans always use it.
/// 
/// Pattern: TenantId first in compound index → MongoDB uses index efficiently.
/// Example index: { tenantId: 1, createdAt: -1 }
/// </summary>
public abstract class MongoRepositoryBase<TEntity> : IRepository<TEntity>
    where TEntity : class
{
    protected readonly IMongoCollection<TEntity> Collection;
    protected readonly ILogger Logger;

    protected MongoRepositoryBase(IMongoDatabase database, string collectionName, ILogger logger)
    {
        Collection = database.GetCollection<TEntity>(collectionName);
        Logger = logger;
    }

    protected abstract FilterDefinition<TEntity> TenantFilter(string tenantId);
    protected abstract FilterDefinition<TEntity> IdAndTenantFilter(string id, string tenantId);

    public virtual async Task<TEntity?> GetByIdAsync(string id, string tenantId, CancellationToken ct = default)
    {
        // ALWAYS filter by tenant first
        var filter = IdAndTenantFilter(id, tenantId);
        return await Collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync(
        string tenantId, int skip = 0, int limit = 50, CancellationToken ct = default)
    {
        var filter = TenantFilter(tenantId);
        var results = await Collection
            .Find(filter)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync(ct);

        return results.AsReadOnly();
    }

    public virtual async Task<Result> InsertAsync(TEntity aggregate, CancellationToken ct = default)
    {
        try
        {
            await Collection.InsertOneAsync(aggregate, cancellationToken: ct);
            return Result.Success();
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return Result.Failure(new Error("DB.DuplicateKey", ex.Message, ErrorCategory.Validation));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "InsertAsync failed for {EntityType}", typeof(TEntity).Name);
            return Result.Failure(new Error("DB.InsertFailed", ex.Message, ErrorCategory.Infrastructure));
        }
    }

    public virtual async Task<Result> UpdateAsync(TEntity aggregate, CancellationToken ct = default)
    {
        try
        {
            var filter = GetReplaceFilter(aggregate);
            var options = new ReplaceOptions { IsUpsert = false };
            var result = await Collection.ReplaceOneAsync(filter, aggregate, options, ct);

            if (result.MatchedCount == 0)
                return Result.Failure(Error.NotFound(typeof(TEntity).Name));

            return Result.Success();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "UpdateAsync failed for {EntityType}", typeof(TEntity).Name);
            return Result.Failure(new Error("DB.UpdateFailed", ex.Message, ErrorCategory.Infrastructure));
        }
    }

    public virtual async Task<Result> DeleteAsync(string id, string tenantId, CancellationToken ct = default)
    {
        var filter = IdAndTenantFilter(id, tenantId);
        var result = await Collection.DeleteOneAsync(filter, ct);

        return result.DeletedCount > 0
            ? Result.Success()
            : Result.Failure(Error.NotFound(typeof(TEntity).Name));
    }

    public virtual async Task<bool> ExistsAsync(string id, string tenantId, CancellationToken ct = default)
    {
        var filter = IdAndTenantFilter(id, tenantId);
        return await Collection.CountDocumentsAsync(filter, cancellationToken: ct) > 0;
    }

    /// <summary>
    /// Override to provide the filter used in ReplaceOne (must include TenantId + Id)
    /// </summary>
    protected abstract FilterDefinition<TEntity> GetReplaceFilter(TEntity entity);
}
