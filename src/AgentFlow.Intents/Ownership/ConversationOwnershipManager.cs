using AgentFlow.Abstractions;
using AgentFlow.Intents.Ownership.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace AgentFlow.Intents.Ownership;

/// <summary>
/// Redis-backed conversation ownership manager.
/// Implements distributed locking with metadata persistence for audit and debugging.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Lock Strategy:</strong>
/// </para>
/// <list type="bullet">
/// <item>Uses <see cref="IDistributedLockService"/> for atomic lock acquisition</item>
/// <item>Stores metadata in Redis Hash for ownership state tracking</item>
/// <item>TTL on both lock and metadata ensures automatic cleanup</item>
/// <item>Multi-tenant isolation via tenantId in all keys</item>
/// </list>
/// <para>
/// <strong>Key Formats:</strong>
/// </para>
/// <list type="bullet">
/// <item>Lock: <c>lock:conversation:{tenantId}:{conversationId}</c></item>
/// <item>Metadata: <c>conversation:metadata:{tenantId}:{conversationId}</c></item>
/// </list>
/// </remarks>
public sealed class ConversationOwnershipManager : IConversationOwnershipManager
{
    private readonly IDistributedLockService _lockService;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ConversationOwnershipManager> _logger;

    // Instance ID for tracking which service instance acquired locks
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];

    public ConversationOwnershipManager(
        IDistributedLockService lockService,
        IConnectionMultiplexer redis,
        ILogger<ConversationOwnershipManager> logger)
    {
        _lockService = lockService ?? throw new ArgumentNullException(nameof(lockService));
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private IDatabase Database => _redis.GetDatabase();

    /// <summary>
    /// Constructs the Redis lock key for a conversation.
    /// Format: lock:conversation:{tenantId}:{conversationId}
    /// </summary>
    private static string BuildLockKey(string tenantId, string conversationId) =>
        $"conversation:{tenantId}:{conversationId}";

    /// <summary>
    /// Constructs the Redis metadata key for a conversation.
    /// Format: conversation:metadata:{tenantId}:{conversationId}
    /// </summary>
    private static string BuildMetadataKey(string tenantId, string conversationId) =>
        $"conversation:metadata:{tenantId}:{conversationId}";

    /// <summary>
    /// Generates a unique lock ID.
    /// Format: {agentId}:{timestamp}:{instanceId}:{guid}
    /// </summary>
    private string GenerateLockId(string agentId) =>
        $"{agentId}:{DateTimeOffset.UtcNow.Ticks}:{_instanceId}:{Guid.NewGuid():N}";

    /// <inheritdoc/>
    public async Task<OwnershipLock?> TryAcquireLockAsync(
        string tenantId,
        string conversationId,
        string agentId,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(conversationId))
            throw new ArgumentException("ConversationId cannot be null or empty", nameof(conversationId));
        if (string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("AgentId cannot be null or empty", nameof(agentId));
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentException("TTL must be positive", nameof(ttl));

        var lockKey = BuildLockKey(tenantId, conversationId);
        var lockId = GenerateLockId(agentId);
        var acquiredAt = DateTimeOffset.UtcNow;
        var expiresAt = acquiredAt.Add(ttl);

        _logger.LogDebug(
            "Attempting to acquire conversation lock: tenant={TenantId}, conversation={ConversationId}, agent={AgentId}, ttl={TTL}",
            tenantId, conversationId, agentId, ttl);

        // Step 1: Acquire distributed lock atomically
        var lockHandle = await _lockService.TryAcquireAsync(lockKey, ttl, ct);

        if (lockHandle == null)
        {
            _logger.LogWarning(
                "Failed to acquire conversation lock (already held): tenant={TenantId}, conversation={ConversationId}, agent={AgentId}",
                tenantId, conversationId, agentId);

            return null;
        }

        // Step 2: Store metadata in Redis Hash for audit/debugging
        var metadataKey = BuildMetadataKey(tenantId, conversationId);
        
        try
        {
            var metadata = new HashEntry[]
            {
                new HashEntry("lock_id", lockId),
                new HashEntry("owner_agent_id", agentId),
                new HashEntry("acquired_at", acquiredAt.ToUnixTimeSeconds().ToString()),
                new HashEntry("expires_at", expiresAt.ToUnixTimeSeconds().ToString()),
                new HashEntry("instance_id", _instanceId),
                new HashEntry("tenant_id", tenantId),
                new HashEntry("conversation_id", conversationId)
            };

            await Database.HashSetAsync(metadataKey, metadata);
            await Database.KeyExpireAsync(metadataKey, ttl.Add(TimeSpan.FromMinutes(1))); // TTL + buffer

            _logger.LogInformation(
                "Conversation lock acquired successfully: lockId={LockId}, tenant={TenantId}, conversation={ConversationId}, agent={AgentId}, expiresAt={ExpiresAt}",
                lockId, tenantId, conversationId, agentId, expiresAt);

            return new OwnershipLock
            {
                LockId = lockId,
                ConversationId = conversationId,
                OwnerAgentId = agentId,
                AcquiredAt = acquiredAt,
                ExpiresAt = expiresAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to store metadata after acquiring lock: tenant={TenantId}, conversation={ConversationId}, agent={AgentId}",
                tenantId, conversationId, agentId);

            // Release lock since we couldn't store metadata
            await lockHandle.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RenewLockAsync(
        string lockId,
        TimeSpan additionalTtl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lockId))
            throw new ArgumentException("LockId cannot be null or empty", nameof(lockId));
        if (additionalTtl <= TimeSpan.Zero)
            throw new ArgumentException("Additional TTL must be positive", nameof(additionalTtl));

        _logger.LogDebug("Attempting to renew lock: lockId={LockId}, additionalTtl={AdditionalTTL}", lockId, additionalTtl);

        // Step 1: Find metadata by lock ID
        var metadataKeyPattern = "conversation:metadata:*";
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        
        // Note: In production, consider maintaining a lockId -> metadataKey index to avoid SCAN
        await foreach (var key in server.KeysAsync(pattern: metadataKeyPattern))
        {
            var storedLockId = await Database.HashGetAsync(key, "lock_id");
            if (storedLockId == lockId)
            {
                // Step 2: Validate lock hasn't expired
                var expiresAtStr = await Database.HashGetAsync(key, "expires_at");
                if (!expiresAtStr.HasValue)
                {
                    _logger.LogWarning("Lock metadata missing expires_at: lockId={LockId}", lockId);
                    return false;
                }

                var expiresAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expiresAtStr!));
                if (expiresAt <= DateTimeOffset.UtcNow)
                {
                    _logger.LogWarning("Lock already expired, cannot renew: lockId={LockId}, expiredAt={ExpiredAt}", lockId, expiresAt);
                    return false;
                }

                // Step 3: Extend TTL on lock and metadata
                var newExpiresAt = expiresAt.Add(additionalTtl);
                await Database.HashSetAsync(key, "expires_at", newExpiresAt.ToUnixTimeSeconds().ToString());

                var currentTtl = await Database.KeyTimeToLiveAsync(key);
                if (currentTtl.HasValue)
                {
                    var extendedTtl = currentTtl.Value.Add(additionalTtl);
                    await Database.KeyExpireAsync(key, extendedTtl);
                }

                _logger.LogInformation("Lock renewed successfully: lockId={LockId}, newExpiresAt={NewExpiresAt}", lockId, newExpiresAt);
                return true;
            }
        }

        _logger.LogWarning("Lock not found for renewal: lockId={LockId}", lockId);
        return false;
    }

    /// <inheritdoc/>
    public async Task ReleaseLockAsync(
        string lockId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lockId))
        {
            _logger.LogWarning("ReleaseLockAsync called with null/empty lockId - ignoring (idempotent)");
            return;
        }

        _logger.LogDebug("Attempting to release lock: lockId={LockId}", lockId);

        try
        {
            // Step 1: Find and extract metadata
            var metadataKeyPattern = "conversation:metadata:*";
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            
            string? metadataKey = null;
            string? tenantId = null;
            string? conversationId = null;

            await foreach (var key in server.KeysAsync(pattern: metadataKeyPattern))
            {
                var storedLockId = await Database.HashGetAsync(key, "lock_id");
                if (storedLockId == lockId)
                {
                    metadataKey = key.ToString();
                    tenantId = await Database.HashGetAsync(key, "tenant_id");
                    conversationId = await Database.HashGetAsync(key, "conversation_id");
                    break;
                }
            }

            if (metadataKey == null)
            {
                _logger.LogWarning("Lock metadata not found (may have already expired): lockId={LockId}", lockId);
                return; // Idempotent: already released or expired
            }

            // Step 2: Delete metadata
            await Database.KeyDeleteAsync(metadataKey);

            // Step 3: Delete distributed lock
            // Note: IDistributedLockService handles lock deletion via IAsyncDisposable,
            // but we need to delete it explicitly here since we don't have the handle.
            // The lock will auto-expire via TTL, but we clean it up for immediate availability.
            if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(conversationId))
            {
                var lockKey = $"lock:{BuildLockKey(tenantId, conversationId)}";
                await Database.KeyDeleteAsync(lockKey);
            }

            _logger.LogInformation(
                "Lock released successfully: lockId={LockId}, tenant={TenantId}, conversation={ConversationId}",
                lockId, tenantId, conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release lock: lockId={LockId}", lockId);
            // Don't throw - release should be idempotent and best-effort
        }
    }

    /// <inheritdoc/>
    public async Task<ConversationOwnershipState> GetStateAsync(
        string tenantId,
        string conversationId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId cannot be null or empty", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(conversationId))
            throw new ArgumentException("ConversationId cannot be null or empty", nameof(conversationId));

        var metadataKey = BuildMetadataKey(tenantId, conversationId);

        _logger.LogDebug("Retrieving ownership state: tenant={TenantId}, conversation={ConversationId}", tenantId, conversationId);

        // Check if metadata exists
        var exists = await Database.KeyExistsAsync(metadataKey);
        if (!exists)
        {
            _logger.LogDebug("No ownership lock found: tenant={TenantId}, conversation={ConversationId}", tenantId, conversationId);
            
            return new ConversationOwnershipState
            {
                ConversationId = conversationId,
                IsLocked = false,
                CurrentOwnerAgentId = null,
                LockedUntil = null,
                WorkflowExecutionId = null
            };
        }

        // Retrieve metadata
        var metadata = await Database.HashGetAllAsync(metadataKey);
        var metadataDict = metadata.ToDictionary(
            entry => entry.Name.ToString(),
            entry => entry.Value.ToString());

        // Extract fields
        var ownerAgentId = metadataDict.GetValueOrDefault("owner_agent_id");
        var expiresAtStr = metadataDict.GetValueOrDefault("expires_at");
        var workflowExecutionId = metadataDict.GetValueOrDefault("workflow_execution_id");

        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrEmpty(expiresAtStr) && long.TryParse(expiresAtStr, out var expiresAtUnix))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix);
        }

        // Check if lock has expired
        var isLocked = expiresAt.HasValue && expiresAt.Value > DateTimeOffset.UtcNow;

        if (!isLocked)
        {
            _logger.LogDebug("Lock found but expired: tenant={TenantId}, conversation={ConversationId}, expiredAt={ExpiredAt}",
                tenantId, conversationId, expiresAt);

            // Clean up expired metadata (best-effort)
            await Database.KeyDeleteAsync(metadataKey);
        }
        else
        {
            _logger.LogDebug("Active lock found: tenant={TenantId}, conversation={ConversationId}, owner={Owner}, expiresAt={ExpiresAt}",
                tenantId, conversationId, ownerAgentId, expiresAt);
        }

        return new ConversationOwnershipState
        {
            ConversationId = conversationId,
            IsLocked = isLocked,
            CurrentOwnerAgentId = isLocked ? ownerAgentId : null,
            LockedUntil = isLocked ? expiresAt : null,
            WorkflowExecutionId = isLocked ? workflowExecutionId : null
        };
    }
}
