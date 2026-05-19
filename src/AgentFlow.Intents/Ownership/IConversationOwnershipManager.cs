using AgentFlow.Intents.Ownership.Models;

namespace AgentFlow.Intents.Ownership;

/// <summary>
/// Manages conversation ownership using distributed locks.
/// Enforces the golden rule: **only 1 agent AI active per conversation**.
/// </summary>
/// <remarks>
/// <para>
/// This is the most critical security component in the Intent Routing system.
/// It prevents:
/// </para>
/// <list type="bullet">
/// <item>Multiple agents competing for a conversation</item>
/// <item>Duplicate or contradictory responses</item>
/// <item>Race conditions in routing</item>
/// <item>Context loss during agent transitions</item>
/// </list>
/// <para>
/// <strong>Implementation Details:</strong>
/// </para>
/// <list type="bullet">
/// <item>Uses Redis distributed locks with TTL for automatic cleanup</item>
/// <item>Multi-tenant safe (tenantId scoping)</item>
/// <item>Idempotent operations for resilience</item>
/// <item>Full audit trail via logging and metadata persistence</item>
/// </list>
/// </remarks>
public interface IConversationOwnershipManager
{
    /// <summary>
    /// Attempts to acquire exclusive ownership of a conversation.
    /// </summary>
    /// <param name="tenantId">The tenant identifier (for multi-tenant isolation).</param>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="agentId">The agent requesting ownership.</param>
    /// <param name="ttl">Time-to-live for the lock. After this duration, lock expires automatically.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="OwnershipLock"/> if lock acquired successfully; otherwise <c>null</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Atomicity:</strong> This operation is atomic via Redis SET NX.
    /// Only one agent can acquire the lock at a time.
    /// </para>
    /// <para>
    /// <strong>TTL Behavior:</strong> If the agent crashes or fails to release,
    /// the lock expires automatically after TTL, preventing orphaned locks.
    /// </para>
    /// <para>
    /// <strong>Usage Pattern:</strong>
    /// </para>
    /// <code>
    /// var lock = await manager.TryAcquireLockAsync("tenant-123", "conv-456", "agent-a", TimeSpan.FromMinutes(5));
    /// if (lock != null)
    /// {
    ///     try
    ///     {
    ///         // Execute workflow
    ///     }
    ///     finally
    ///     {
    ///         await manager.ReleaseLockAsync(lock.LockId);
    ///     }
    /// }
    /// </code>
    /// </remarks>
    Task<OwnershipLock?> TryAcquireLockAsync(
        string tenantId,
        string conversationId,
        string agentId,
        TimeSpan ttl,
        CancellationToken ct = default);

    /// <summary>
    /// Renews an existing lock by extending its TTL.
    /// </summary>
    /// <param name="lockId">The lock identifier returned by <see cref="TryAcquireLockAsync"/>.</param>
    /// <param name="additionalTtl">Additional time to add to the lock expiration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if lock renewed successfully; <c>false</c> if lock not found or expired.</returns>
    /// <remarks>
    /// <para>
    /// Use this for long-running operations that exceed the initial TTL.
    /// </para>
    /// <para>
    /// <strong>Safety:</strong> Only the lock holder can renew. Lock validation occurs before renewal.
    /// </para>
    /// </remarks>
    Task<bool> RenewLockAsync(
        string lockId,
        TimeSpan additionalTtl,
        CancellationToken ct = default);

    /// <summary>
    /// Releases a conversation ownership lock.
    /// </summary>
    /// <param name="lockId">The lock identifier to release.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// <para>
    /// <strong>Idempotent:</strong> Safe to call multiple times. No error if lock already released.
    /// </para>
    /// <para>
    /// <strong>Best Practice:</strong> Always call in a <c>finally</c> block to ensure cleanup.
    /// </para>
    /// </remarks>
    Task ReleaseLockAsync(
        string lockId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current ownership state of a conversation.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current ownership state.</returns>
    /// <remarks>
    /// <para>
    /// Use this for:
    /// </para>
    /// <list type="bullet">
    /// <item>Checking if a conversation is locked before attempting acquisition</item>
    /// <item>Debugging ownership conflicts</item>
    /// <item>Auditing agent activity</item>
    /// </list>
    /// </remarks>
    Task<ConversationOwnershipState> GetStateAsync(
        string tenantId,
        string conversationId,
        CancellationToken ct = default);
}
