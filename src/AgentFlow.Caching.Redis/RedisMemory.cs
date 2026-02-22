using AgentFlow.Abstractions;
using AgentFlow.Application.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace AgentFlow.Caching.Redis;

// =========================================================================
// REDIS WORKING MEMORY
// =========================================================================

/// <summary>
/// Redis-backed working memory for agent execution state.
/// Invariants:
/// - NEVER stores full prompt text (only structured state)
/// - Always has TTL (expiry = maxExecutionSeconds + buffer)
/// - TTL key: working:{executionId}:*
/// - Cleared explicitly at execution end (belt-and-suspenders with Redis TTL)
/// </summary>
public sealed class RedisWorkingMemoryStore : IWorkingMemoryStore, IWorkingMemory
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisWorkingMemoryStore> _logger;

    public RedisWorkingMemoryStore(IConnectionMultiplexer redis, ILogger<RedisWorkingMemoryStore> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    private static string FieldKey(string executionId, string key) =>
        $"wm:{executionId}:{key}";
    private static string IndexKey(string executionId) =>
        $"wm:{executionId}:__index";

    public async Task SetAsync(string executionId, string key, string valueJson, TimeSpan expiry, CancellationToken ct = default)
    {
        var fieldKey = FieldKey(executionId, key);

        await _db.StringSetAsync(fieldKey, valueJson, expiry);

        // Track all keys for this execution (for ClearAsync)
        await _db.SetAddAsync(IndexKey(executionId), key);
        await _db.KeyExpireAsync(IndexKey(executionId), expiry);

        _logger.LogDebug("WorkingMemory set: execution={ExecutionId}, key={Key}, ttl={TTL}", executionId, key, expiry);
    }

    public async Task<string?> GetAsync(string executionId, string key, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync(FieldKey(executionId, key));
        return value.HasValue ? (string?)value : null;
    }

    public async Task<string> GetSummaryAsync(string executionId, CancellationToken ct = default)
    {
        var members = await _db.SetMembersAsync(IndexKey(executionId));

        if (members.Length == 0)
            return "{}";

        var dict = new Dictionary<string, object?>();

        foreach (var member in members)
        {
            var key = member.ToString();
            if (key.StartsWith("__")) continue; // internal keys

            var value = await _db.StringGetAsync(FieldKey(executionId, key));
            if (value.HasValue)
            {
                try
                {
                    dict[key] = JsonSerializer.Deserialize<JsonElement>(value.ToString());
                }
                catch
                {
                    dict[key] = value.ToString();
                }
            }
        }

        return JsonSerializer.Serialize(dict);
    }

    public async Task ClearAsync(string executionId, CancellationToken ct = default)
    {
        var members = await _db.SetMembersAsync(IndexKey(executionId));
        var keysToDelete = members
            .Select(m => (RedisKey)FieldKey(executionId, m.ToString()))
            .Append((RedisKey)IndexKey(executionId))
            .ToArray();

        if (keysToDelete.Length > 0)
        {
            await _db.KeyDeleteAsync(keysToDelete);
            _logger.LogDebug("WorkingMemory cleared: {Count} keys for execution={ExecutionId}", keysToDelete.Length, executionId);
        }
    }

    // IWorkingMemory Implementation (Application Layer Compatibility)

    async Task IWorkingMemory.SetAsync(string executionId, string key, string value, TimeSpan? ttl, CancellationToken ct)
    {
        await SetAsync(executionId, key, value, ttl ?? TimeSpan.FromHours(1), ct);
    }

    async Task<IReadOnlyDictionary<string, string>> IWorkingMemory.GetAllAsync(string executionId, CancellationToken ct)
    {
        var members = await _db.SetMembersAsync(IndexKey(executionId));
        var dict = new Dictionary<string, string>();

        foreach (var member in members)
        {
            var key = member.ToString();
            var value = await _db.StringGetAsync(FieldKey(executionId, key));
            if (value.HasValue)
            {
                dict[key] = value.ToString();
            }
        }

        return dict;
    }
}

// =========================================================================
// REDIS DISTRIBUTED LOCK
// =========================================================================

/// <summary>
/// Distributed lock using Redis SET NX (SET if Not eXists).
/// Prevents duplicate execution processing.
/// Security rule: acquire BEFORE starting loop, release AFTER loop ends.
/// </summary>
public sealed class RedisDistributedLockService : IDistributedLockService
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisDistributedLockService> _logger;
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];

    public RedisDistributedLockService(
        IConnectionMultiplexer redis,
        ILogger<RedisDistributedLockService> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string lockKey,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        var lockValue = $"{_instanceId}:{DateTime.UtcNow.Ticks}";
        var redisKey = $"lock:{lockKey}";

        var acquired = await _db.StringSetAsync(
            redisKey,
            lockValue,
            expiry,
            When.NotExists);

        if (!acquired)
        {
            _logger.LogWarning("Could not acquire lock '{LockKey}' — already held", lockKey);
            return null;
        }

        _logger.LogDebug("Lock acquired: '{LockKey}' (value={LockValue}, ttl={TTL})", lockKey, lockValue, expiry);

        return new RedisLockRelease(_db, redisKey, lockValue, _logger);
    }

    private sealed class RedisLockRelease(
        IDatabase db,
        string key,
        string expectedValue,
        ILogger logger) : IAsyncDisposable
    {
        private bool _released = false;

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;

            // Lua script: only delete if value matches (safe release)
            const string Script = """
                if redis.call("GET", KEYS[1]) == ARGV[1] then
                    return redis.call("DEL", KEYS[1])
                else
                    return 0
                end
                """;

            try
            {
                await db.ScriptEvaluateAsync(Script, [key], [expectedValue]);
                logger.LogDebug("Lock released: '{Key}'", key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to release lock '{Key}'", key);
            }
        }
    }
}

// =========================================================================
// TOKEN BUDGET (per tenant, per session)
// =========================================================================

public interface ITokenBudgetService
{
    Task<bool> TryConsumeAsync(string tenantId, string sessionId, int tokens, CancellationToken ct = default);
    Task<long> GetRemainingAsync(string tenantId, string sessionId, CancellationToken ct = default);
    Task ResetAsync(string tenantId, string sessionId, CancellationToken ct = default);
}

public sealed class RedisTokenBudgetService : ITokenBudgetService
{
    private readonly IDatabase _db;
    private static readonly TimeSpan DefaultSessionTtl = TimeSpan.FromHours(24);

    public RedisTokenBudgetService(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    private static string BudgetKey(string tenantId, string sessionId) =>
        $"tokbudget:{tenantId}:{sessionId}";

    public async Task<bool> TryConsumeAsync(
        string tenantId, string sessionId, int tokens, CancellationToken ct = default)
    {
        var key = BudgetKey(tenantId, sessionId);
        var current = (long?)await _db.StringGetAsync(key) ?? 0;

        // The limit comes from tenant config (injected at startup). Here we just track.
        await _db.StringIncrementAsync(key, tokens);
        await _db.KeyExpireAsync(key, DefaultSessionTtl);
        return true;
    }

    public async Task<long> GetRemainingAsync(
        string tenantId, string sessionId, CancellationToken ct = default)
    {
        var key = BudgetKey(tenantId, sessionId);
        return (long?)await _db.StringGetAsync(key) ?? 0;
    }

    public async Task ResetAsync(
        string tenantId, string sessionId, CancellationToken ct = default) =>
        await _db.KeyDeleteAsync(BudgetKey(tenantId, sessionId));
}

// =========================================================================
// DI EXTENSIONS
// =========================================================================

public static class RedisServiceExtensions
{
    public static IServiceCollection AddAgentFlowRedis(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(connectionString));

        services.AddScoped<RedisWorkingMemoryStore>();
        services.AddScoped<IWorkingMemoryStore>(sp => sp.GetRequiredService<RedisWorkingMemoryStore>());
        services.AddScoped<IWorkingMemory>(sp => sp.GetRequiredService<RedisWorkingMemoryStore>());
        services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();
        services.AddSingleton<ITokenBudgetService, RedisTokenBudgetService>();

        return services;
    }
}
