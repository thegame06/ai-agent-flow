using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;

namespace AgentFlow.Domain.Repositories;

/// <summary>
/// Repository for ConversationThread aggregate.
/// Enterprise-ready with tenant isolation and ownership validation.
/// </summary>
public interface IConversationThreadRepository
{
    /// <summary>
    /// Get thread by ID with tenant validation.
    /// </summary>
    Task<ConversationThread?> GetByIdAsync(string threadId, string tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Get thread by human-readable key.
    /// </summary>
    Task<ConversationThread?> GetByKeyAsync(string threadKey, string tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Get all active threads for a user.
    /// </summary>
    Task<IReadOnlyList<ConversationThread>> GetActiveByUserAsync(string userId, string tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Get all threads for a specific agent.
    /// </summary>
    Task<IReadOnlyList<ConversationThread>> GetByAgentAsync(string agentDefinitionId, string tenantId, int skip = 0, int take = 50, CancellationToken ct = default);
    
    /// <summary>
    /// Insert new thread.
    /// </summary>
    Task<Result> InsertAsync(ConversationThread thread, CancellationToken ct = default);
    
    /// <summary>
    /// Update existing thread.
    /// </summary>
    Task<Result> UpdateAsync(ConversationThread thread, CancellationToken ct = default);
    
    /// <summary>
    /// Delete thread (hard delete for GDPR compliance).
    /// </summary>
    Task<Result> DeleteAsync(string threadId, string tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Get count of active threads (for monitoring).
    /// </summary>
    Task<int> GetActiveCountAsync(string tenantId, CancellationToken ct = default);
}
