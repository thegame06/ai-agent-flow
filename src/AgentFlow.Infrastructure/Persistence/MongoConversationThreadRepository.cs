using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using MongoDB.Driver;

namespace AgentFlow.Infrastructure.Persistence;

/// <summary>
/// MongoDB implementation with TTL index on ExpiresAt.
/// Auto-cleanup of expired threads.
/// Enterprise-ready with tenant isolation and performance indexes.
/// </summary>
public sealed class MongoConversationThreadRepository : IConversationThreadRepository
{
    private readonly IMongoCollection<ConversationThread> _collection;
    
    public MongoConversationThreadRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ConversationThread>("conversation_threads");
        
        // TTL Index: Auto-delete expired threads
        _collection.Indexes.CreateOne(new CreateIndexModel<ConversationThread>(
            Builders<ConversationThread>.IndexKeys.Ascending(x => x.ExpiresAt),
            new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }
        ));
        
        // Performance index: TenantId + ThreadKey (unique)
        _collection.Indexes.CreateOne(new CreateIndexModel<ConversationThread>(
            Builders<ConversationThread>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.ThreadKey),
            new CreateIndexOptions { Unique = true }
        ));
        
        // Performance index: TenantId + UserId + Status
        _collection.Indexes.CreateOne(new CreateIndexModel<ConversationThread>(
            Builders<ConversationThread>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.UserId)
                .Ascending(x => x.Status)
        ));
        
        // Performance index: TenantId + AgentDefinitionId
        _collection.Indexes.CreateOne(new CreateIndexModel<ConversationThread>(
            Builders<ConversationThread>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.AgentDefinitionId)
        ));
    }
    
    public async Task<ConversationThread?> GetByIdAsync(string threadId, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => 
            x.Id == threadId && 
            x.TenantId == tenantId
        ).FirstOrDefaultAsync(ct);
    }
    
    public async Task<ConversationThread?> GetByKeyAsync(string threadKey, string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => 
            x.ThreadKey == threadKey && 
            x.TenantId == tenantId
        ).FirstOrDefaultAsync(ct);
    }
    
    public async Task<IReadOnlyList<ConversationThread>> GetActiveByUserAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        var results = await _collection.Find(x => 
            x.UserId == userId && 
            x.TenantId == tenantId &&
            x.Status == ThreadStatus.Active
        )
        .SortByDescending(x => x.LastActivityAt)
        .ToListAsync(ct);
        
        return results.AsReadOnly();
    }
    
    public async Task<IReadOnlyList<ConversationThread>> GetByAgentAsync(
        string agentDefinitionId, 
        string tenantId, 
        int skip = 0, 
        int take = 50, 
        CancellationToken ct = default)
    {
        var results = await _collection.Find(x => 
            x.AgentDefinitionId == agentDefinitionId && 
            x.TenantId == tenantId
        )
        .SortByDescending(x => x.LastActivityAt)
        .Skip(skip)
        .Limit(take)
        .ToListAsync(ct);
        
        return results.AsReadOnly();
    }
    
    public async Task<Result> InsertAsync(ConversationThread thread, CancellationToken ct = default)
    {
        try
        {
            await _collection.InsertOneAsync(thread, cancellationToken: ct);
            return Result.Success();
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return Result.Failure(Error.Validation("ThreadKey", "A thread with this key already exists."));
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("Thread.InsertFailed", $"Failed to insert thread: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }
    
    public async Task<Result> UpdateAsync(ConversationThread thread, CancellationToken ct = default)
    {
        try
        {
            var result = await _collection.ReplaceOneAsync(
                x => x.Id == thread.Id && x.TenantId == thread.TenantId,
                thread,
                cancellationToken: ct
            );
            
            if (result.MatchedCount == 0)
                return Result.Failure(Error.NotFound("Thread not found"));
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("Thread.UpdateFailed", $"Failed to update thread: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }
    
    public async Task<Result> DeleteAsync(string threadId, string tenantId, CancellationToken ct = default)
    {
        try
        {
            // Hard delete for GDPR compliance
            var result = await _collection.DeleteOneAsync(
                x => x.Id == threadId && x.TenantId == tenantId,
                ct
            );
            
            if (result.DeletedCount == 0)
                return Result.Failure(Error.NotFound("Thread not found"));
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("Thread.DeleteFailed", $"Failed to delete thread: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }
    
    public async Task<int> GetActiveCountAsync(string tenantId, CancellationToken ct = default)
    {
        return (int)await _collection.CountDocumentsAsync(
            x => x.TenantId == tenantId && x.Status == ThreadStatus.Active,
            cancellationToken: ct
        );
    }
}
