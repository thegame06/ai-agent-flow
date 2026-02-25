using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using MongoDB.Driver;
using Result = AgentFlow.Abstractions.Result;

namespace AgentFlow.Infrastructure.Repositories;

public sealed class MongoChannelMessageRepository : IChannelMessageRepository
{
    private readonly IMongoCollection<ChannelMessage> _collection;

    public MongoChannelMessageRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ChannelMessage>("channel_messages");

        _collection.Indexes.CreateOne(new CreateIndexModel<ChannelMessage>(
            Builders<ChannelMessage>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.SessionId)
                .Descending(x => x.CreatedAt)
        ));

        _collection.Indexes.CreateOne(new CreateIndexModel<ChannelMessage>(
            Builders<ChannelMessage>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.ChannelId)
                .Descending(x => x.CreatedAt)
        ));
    }

    public async Task<ChannelMessage?> GetByIdAsync(string messageId, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.Id == messageId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ChannelMessage>> GetBySessionAsync(string sessionId, string tenantId, int limit = 50, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.SessionId == sessionId && x.TenantId == tenantId)
            .SortByDescending(x => x.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ChannelMessage>> GetByChannelAsync(string channelId, string tenantId, int limit = 50, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.ChannelId == channelId && x.TenantId == tenantId)
            .SortByDescending(x => x.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<Result> InsertAsync(ChannelMessage message, CancellationToken ct = default)
    {
        try
        {
            await _collection.InsertOneAsync(message, cancellationToken: ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("ChannelMessage.InsertFailed", $"Failed to insert message: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }

    public async Task<Result> UpdateAsync(ChannelMessage message, CancellationToken ct = default)
    {
        try
        {
            var result = await _collection.ReplaceOneAsync(
                x => x.Id == message.Id && x.TenantId == message.TenantId,
                message,
                cancellationToken: ct
            );

            if (result.MatchedCount == 0)
                return Result.Failure(Error.NotFound("Message not found"));

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("ChannelMessage.UpdateFailed", $"Failed to update message: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }

    public async Task<Result> DeleteAsync(string messageId, string tenantId, CancellationToken ct = default)
    {
        try
        {
            var result = await _collection.DeleteOneAsync(
                x => x.Id == messageId && x.TenantId == tenantId,
                ct
            );

            if (result.DeletedCount == 0)
                return Result.Failure(Error.NotFound("Message not found"));

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("ChannelMessage.DeleteFailed", $"Failed to delete message: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }
}
