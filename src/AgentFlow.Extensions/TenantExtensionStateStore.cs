using System.Collections.Concurrent;

namespace AgentFlow.Extensions;

/// <summary>
/// Persistence for per-tenant extension entitlements (which apps/modules a tenant has
/// installed and enabled), tenant allowlists and global quarantine.
/// The registry delegates all tenant state here so it survives restarts and is shared
/// across API instances when a durable implementation (e.g. Mongo) is registered.
/// </summary>
public interface ITenantExtensionStateStore
{
    /// <summary>Installed extensions for the tenant, mapped to their enabled flag.</summary>
    Task<IReadOnlyDictionary<string, bool>> GetStatesAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Installs (or updates) an extension for the tenant with the given enabled flag.</summary>
    Task SetStateAsync(string tenantId, string extensionId, bool enabled, CancellationToken ct = default);

    /// <summary>Uninstalls an extension for the tenant. No-op when it is not installed.</summary>
    Task RemoveStateAsync(string tenantId, string extensionId, CancellationToken ct = default);

    /// <summary>Extensions the tenant may install. Empty means no restriction.</summary>
    Task<IReadOnlySet<string>> GetAllowlistAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Replaces the tenant allowlist.</summary>
    Task SetAllowlistAsync(string tenantId, IReadOnlyCollection<string> extensionIds, CancellationToken ct = default);

    /// <summary>Quarantined extensions mapped to the quarantine reason.</summary>
    Task<IReadOnlyDictionary<string, string>> GetQuarantinedAsync(CancellationToken ct = default);

    /// <summary>Quarantines an extension and disables it for every tenant that has it installed.</summary>
    Task QuarantineAsync(string extensionId, string reason, CancellationToken ct = default);
}

/// <summary>
/// Process-local store. Default when no durable store is registered (unit tests, tools);
/// state is lost on restart, so hosts that sell apps per tenant must register a durable store.
/// </summary>
public sealed class InMemoryTenantExtensionStateStore : ITenantExtensionStateStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _states = new();
    private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _allowlists = new();
    private readonly ConcurrentDictionary<string, string> _quarantine = new();

    public Task<IReadOnlyDictionary<string, bool>> GetStatesAsync(string tenantId, CancellationToken ct = default)
    {
        IReadOnlyDictionary<string, bool> snapshot = _states.TryGetValue(tenantId, out var state)
            ? state.ToDictionary(x => x.Key, x => x.Value)
            : new Dictionary<string, bool>();
        return Task.FromResult(snapshot);
    }

    public Task SetStateAsync(string tenantId, string extensionId, bool enabled, CancellationToken ct = default)
    {
        _states.GetOrAdd(tenantId, _ => new ConcurrentDictionary<string, bool>())[extensionId] = enabled;
        return Task.CompletedTask;
    }

    public Task RemoveStateAsync(string tenantId, string extensionId, CancellationToken ct = default)
    {
        if (_states.TryGetValue(tenantId, out var state))
        {
            state.TryRemove(extensionId, out _);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlySet<string>> GetAllowlistAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult(_allowlists.TryGetValue(tenantId, out var allowlist)
            ? allowlist
            : (IReadOnlySet<string>)new HashSet<string>());

    public Task SetAllowlistAsync(string tenantId, IReadOnlyCollection<string> extensionIds, CancellationToken ct = default)
    {
        _allowlists[tenantId] = extensionIds.ToHashSet();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetQuarantinedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(_quarantine.ToDictionary(x => x.Key, x => x.Value));

    public Task QuarantineAsync(string extensionId, string reason, CancellationToken ct = default)
    {
        _quarantine[extensionId] = reason;
        foreach (var tenant in _states.Values)
        {
            if (tenant.ContainsKey(extensionId)) tenant[extensionId] = false;
        }

        return Task.CompletedTask;
    }
}
