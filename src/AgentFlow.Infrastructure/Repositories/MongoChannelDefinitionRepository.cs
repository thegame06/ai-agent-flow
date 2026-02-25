using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using MongoDB.Driver;
using Result = AgentFlow.Abstractions.Result;

namespace AgentFlow.Infrastructure.Repositories;

public sealed class MongoChannelDefinitionRepository : IChannelDefinitionRepository
{
    private readonly IMongoCollection<ChannelDefinition> _collection;

    public MongoChannelDefinitionRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ChannelDefinition>("channels");
        
        _collection.Indexes.CreateOne(new CreateIndexModel<ChannelDefinition>(
            Builders<ChannelDefinition>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.Name),
            new CreateIndexOptions { Unique = true }
        ));
    }

    public async Task<ChannelDefinition?> GetByIdAsync(string channelId, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.Id == channelId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ChannelDefinition>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.TenantId == tenantId)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ChannelDefinition>> GetByTypeAsync(ChannelType type, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.TenantId == tenantId && x.Type == type)
            .ToListAsync(ct);
    }

    public async Task<ChannelDefinition?> GetByNameAsync(string name, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.Name == name && x.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Result> InsertAsync(ChannelDefinition channel, CancellationToken ct = default)
    {
        try
        {
            await _collection.InsertOneAsync(channel, cancellationToken: ct);
            return Result.Success();
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return Result.Failure(Error.Validation("Name", "A channel with this name already exists."));
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("Channel.InsertFailed", $"Failed to insert channel: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }

    public async Task<Result> UpdateAsync(ChannelDefinition channel, CancellationToken ct = default)
    {
        try
        {
            var result = await _collection.ReplaceOneAsync(
                x => x.Id == channel.Id && x.TenantId == channel.TenantId,
                channel,
                cancellationToken: ct
            );

            if (result.MatchedCount == 0)
                return Result.Failure(Error.NotFound("Channel not found"));

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("Channel.UpdateFailed", $"Failed to update channel: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }

    public async Task<Result> DeleteAsync(string channelId, string tenantId, CancellationToken ct = default)
    {
        try
        {
            var result = await _collection.DeleteOneAsync(
                x => x.Id == channelId && x.TenantId == tenantId,
                ct
            );

            if (result.DeletedCount == 0)
                return Result.Failure(Error.NotFound("Channel not found"));

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("Channel.DeleteFailed", $"Failed to delete channel: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }
}
