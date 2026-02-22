using AgentFlow.Application.Memory;
using System.Collections.Concurrent;

namespace AgentFlow.Infrastructure.Memory;

/// <summary>
/// Simple in-memory working memory for MVP.
/// In production, this should be replaced with Redis.
/// </summary>
public sealed class InMemoryWorkingMemory : IWorkingMemory
{
    // Dictionary of ExecutionId -> Dictionary of Key -> Value
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _store = new();

    public Task<string?> GetAsync(string executionId, string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(executionId, out var executionStore) && 
            executionStore.TryGetValue(key, out var value))
        {
            return Task.FromResult<string?>(value);
        }
        return Task.FromResult<string?>(null);
    }

    public Task SetAsync(string executionId, string key, string value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var executionStore = _store.GetOrAdd(executionId, _ => new ConcurrentDictionary<string, string>());
        executionStore[key] = value;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(string executionId, CancellationToken ct = default)
    {
        if (_store.TryGetValue(executionId, out var executionStore))
        {
            return Task.FromResult<IReadOnlyDictionary<string, string>>(executionStore);
        }
        return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    public Task ClearAsync(string executionId, CancellationToken ct = default)
    {
        _store.TryRemove(executionId, out _);
        return Task.CompletedTask;
    }
}
