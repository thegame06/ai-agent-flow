using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using MongoDB.Driver;

namespace AgentFlow.Infrastructure.Repositories;

public sealed class MongoChannelSessionRepository : IChannelSessionRepository
{
    private readonly IMongoCollection<ChannelSession> _collection;

    public MongoChannelSessionRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ChannelSession>("channel_sessions");

        _collection.Indexes.CreateOne(new CreateIndexModel<ChannelSession>(
            Builders<ChannelSession>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.ChannelId)
                .Ascending(x => x.Identifier)
        ));

        _collection.Indexes.CreateOne(new CreateIndexModel<ChannelSession>(
            Builders<ChannelSession>.IndexKeys
                .Ascending(x => x.ExpiresAt)
        ));
    }

    public async Task<ChannelSession?> GetByIdAsync(string sessionId, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.Id == sessionId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ChannelSession?> GetByChannelAndIdentifierAsync(string channelId, string identifier, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => 
                x.ChannelId == channelId && 
                x.Identifier == identifier && 
                x.TenantId == tenantId &&
                x.Status == SessionStatus.Active)
            .SortByDescending(x => x.LastActivityAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ChannelSession>> GetActiveByChannelAsync(string channelId, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => 
                x.ChannelId == channelId && 
                x.TenantId == tenantId && 
                x.Status == SessionStatus.Active)
            .SortByDescending(x => x.LastActivityAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ChannelSession>> GetActiveByUserAsync(string userIdentifier, string tenantId, CancellationToken ct = default)
    {
        var filter = Builders<ChannelSession>.Filter.And(
            Builders<ChannelSession>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ChannelSession>.Filter.Eq(x => x.Status, SessionStatus.Active)
        );

        if (userIdentifier != "%")
        {
            filter &= Builders<ChannelSession>.Filter.Eq(x => x.Identifier, userIdentifier);
        }

        return await _collection.Find(filter)
            .SortByDescending(x => x.LastActivityAt)
            .ToListAsync(ct);
    }

    public async Task<Result> InsertAsync(ChannelSession session, CancellationToken ct = default)
    {
        try
        {
            await _collection.InsertOneAsync(session, cancellationToken: ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("ChannelSession.InsertFailed", $"Failed to insert session: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }

    public async Task<Result> UpdateAsync(ChannelSession session, CancellationToken ct = default)
    {
        try
        {
            var expectedVersion = session.LockVersion;
            session.IncrementLockVersion();

            var result = await _collection.ReplaceOneAsync(
                x => x.Id == session.Id && x.TenantId == session.TenantId && x.LockVersion == expectedVersion,
                session,
                cancellationToken: ct
            );

            if (result.MatchedCount == 0)
            {
                var exists = await _collection.Find(x => x.Id == session.Id && x.TenantId == session.TenantId)
                    .AnyAsync(ct);

                return exists
                    ? Result.Failure(new Error("ChannelSession.ConcurrentUpdate", "Concurrent session update detected", ErrorCategory.Infrastructure))
                    : Result.Failure(Error.NotFound("Session not found"));
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("ChannelSession.UpdateFailed", $"Failed to update session: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }

    public async Task<Result> DeleteAsync(string sessionId, string tenantId, CancellationToken ct = default)
    {
        try
        {
            var result = await _collection.DeleteOneAsync(
                x => x.Id == sessionId && x.TenantId == tenantId,
                ct
            );

            if (result.DeletedCount == 0)
                return Result.Failure(Error.NotFound("Session not found"));

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("ChannelSession.DeleteFailed", $"Failed to delete session: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }

    public async Task<int> GetActiveCountAsync(string tenantId, CancellationToken ct = default)
    {
        return (int)await _collection.CountDocumentsAsync(
            x => x.TenantId == tenantId && x.Status == SessionStatus.Active,
            cancellationToken: ct
        );
    }
}
