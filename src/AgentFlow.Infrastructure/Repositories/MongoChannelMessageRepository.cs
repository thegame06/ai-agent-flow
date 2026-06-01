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

        _collection.Indexes.CreateOne(new CreateIndexModel<ChannelMessage>(
            Builders<ChannelMessage>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.ChannelId)
                .Ascending(x => x.Direction)
                .Ascending(x => x.ExternalMessageId),
            new CreateIndexOptions
            {
                Name = "ux_channel_messages_external_message",
                Unique = true,
                Sparse = true
            }
        ));
    }

    public async Task<ChannelMessage?> GetByIdAsync(string messageId, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.Id == messageId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ChannelMessage?> GetByExternalMessageIdAsync(
        string tenantId,
        string channelId,
        string externalMessageId,
        MessageDirection direction,
        CancellationToken ct = default)
    {
        return await _collection.Find(x =>
                x.TenantId == tenantId &&
                x.ChannelId == channelId &&
                x.Direction == direction &&
                x.ExternalMessageId == externalMessageId)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ChannelMessage>> GetBySessionAsync(string sessionId, string tenantId, int limit = 50, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.SessionId == sessionId && x.TenantId == tenantId)
            .SortByDescending(x => x.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<ChannelMessage> Items, long Total)> GetBySessionPagedAsync(
        string sessionId,
        string tenantId,
        int page = 0,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(0, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var filter = Builders<ChannelMessage>.Filter.Eq(x => x.SessionId, sessionId) &
            Builders<ChannelMessage>.Filter.Eq(x => x.TenantId, tenantId);
        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _collection.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip(page * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<ChannelMessage>> GetByChannelAsync(string channelId, string tenantId, int limit = 50, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.ChannelId == channelId && x.TenantId == tenantId)
            .SortByDescending(x => x.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<ChannelMessage?> GetLatestOutgoingByExecutionIdAsync(string tenantId, string executionId, CancellationToken ct = default)
    {
        return await _collection.Find(x =>
                x.TenantId == tenantId &&
                x.Direction == MessageDirection.Outgoing &&
                x.AgentExecutionId == executionId)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
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
