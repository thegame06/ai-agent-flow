using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Persistence;

/// <summary>
/// MongoDB implementation with TTL index on ExpiresAt.
/// Auto-cleanup of expired threads.
/// Enterprise-ready with tenant isolation and performance indexes.
/// </summary>
public sealed class MongoConversationThreadRepository : IConversationThreadRepository
{
    private readonly IMongoCollection<ConversationThread> _collection;
    private readonly ILogger<MongoConversationThreadRepository> _logger;
    
    public MongoConversationThreadRepository(IMongoDatabase database, ILoggerFactory loggerFactory)
    {
        _collection = database.GetCollection<ConversationThread>("conversation_threads");
        _logger = loggerFactory.CreateLogger<MongoConversationThreadRepository>();
        
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
            // 🔍 DEBUG: Log what we're about to persist
            _logger.LogDebug("Updating thread {ThreadId}: TurnCount={TurnCount}, ExecutionIds.Count={Count}",
                thread.Id, thread.TurnCount, thread.ExecutionIds.Count);

            var result = await _collection.ReplaceOneAsync(
                x => x.Id == thread.Id && x.TenantId == thread.TenantId,
                thread,
                cancellationToken: ct
            );
            
            if (result.MatchedCount == 0)
                return Result.Failure(Error.NotFound("Thread not found"));
            
            // 🔍 DEBUG: Verify what was saved by reading it back
            var reloaded = await _collection.Find(x => x.Id == thread.Id).FirstOrDefaultAsync(ct);
            if (reloaded != null)
            {
                _logger.LogDebug("After update, reloaded thread has TurnCount={TurnCount}, ExecutionIds.Count={Count}",
                    reloaded.TurnCount, reloaded.ExecutionIds.Count);
            }
            
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
