using System.Text.Json;
using AgentFlow.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace AgentFlow.Api.Connect;

public sealed class TenantProviderResolver : IProviderResolver
{
    private readonly ITenantConnectionStore _connectionStore;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IProviderRegistry _providerRegistry;

    public TenantProviderResolver(
        ITenantConnectionStore connectionStore,
        IDataProtectionProvider dataProtectionProvider,
        IProviderRegistry providerRegistry)
    {
        _connectionStore = connectionStore;
        _dataProtectionProvider = dataProtectionProvider;
        _providerRegistry = providerRegistry;
    }

    public async Task<ResolvedProviderAdapter<TAdapter>> ResolveRequiredAsync<TAdapter>(
        ProviderResolutionContext context,
        CancellationToken ct = default)
        where TAdapter : class, IProviderAdapter
    {
        ValidateContext(context);

        var candidates = _providerRegistry.GetByCapability<TAdapter>(context.Capability, context.Channel);
        if (candidates.Count == 0)
            throw new InvalidOperationException($"No provider adapter registered for capability '{context.Capability}' on channel '{context.Channel}'.");

        var connections = await _connectionStore.GetConnectionsAsync(context.TenantId, ct);
        if (!string.IsNullOrWhiteSpace(context.ConnectionId))
            connections = connections.Where(x => x.Id == context.ConnectionId).ToList();

        candidates = OrderCandidates(candidates, context);

        foreach (var adapter in candidates)
        {
            var connection = connections.FirstOrDefault(x => MatchesAdapter(adapter, x));
            if (connection is null)
                continue;

            var secret = await _connectionStore.GetSecretAsync(context.TenantId, connection.Id, ct);
            if (secret is null)
                throw new InvalidOperationException(
                    $"Connection '{connection.Id}' for provider '{adapter.ProviderId}' is missing credentials.");

            return new ResolvedProviderAdapter<TAdapter>(adapter, new ProviderConnectionProfile
            {
                ConnectionId = connection.Id,
                TenantId = connection.TenantId,
                ProviderId = adapter.ProviderId,
                ConnectorId = connection.ConnectorId,
                Config = connection.Config,
                Secret = ReadSecret(secret)
            });
        }

        throw new InvalidOperationException(
            $"No tenant connection matched capability '{context.Capability}' on channel '{context.Channel}' for tenant '{context.TenantId}'.");
    }

    private static IReadOnlyList<TAdapter> OrderCandidates<TAdapter>(
        IReadOnlyList<TAdapter> candidates,
        ProviderResolutionContext context)
        where TAdapter : class, IProviderAdapter
    {
        var preferred = BuildPreferredProviderChain(context);
        if (preferred.Count == 0)
            return candidates;

        var byProvider = candidates
            .GroupBy(x => x.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var ordered = new List<TAdapter>(candidates.Count);
        foreach (var providerId in preferred)
        {
            if (!byProvider.TryGetValue(providerId, out var list))
                continue;

            ordered.AddRange(list);
            byProvider.Remove(providerId);
        }

        // Keep deterministic order for the remaining providers.
        foreach (var providerId in byProvider.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            ordered.AddRange(byProvider[providerId]);

        return ordered;
    }

    private static IReadOnlyList<string> BuildPreferredProviderChain(ProviderResolutionContext context)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        void add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            if (set.Add(raw.Trim()))
                ordered.Add(raw.Trim());
        }

        add(context.PreferredProviderId);

        if (context.Metadata.TryGetValue("providerCandidates", out var csv))
        {
            foreach (var value in csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                add(value);
        }

        if (context.Metadata.TryGetValue("providerCandidatesCsv", out var csvLegacy))
        {
            foreach (var value in csvLegacy.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                add(value);
        }

        return ordered;
    }

    private static void ValidateContext(ProviderResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.TenantId))
            throw new ArgumentException("TenantId is required.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.Capability))
            throw new ArgumentException("Capability is required.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.Channel))
            throw new ArgumentException("Channel is required.", nameof(context));
    }

    private bool MatchesAdapter(IProviderAdapter adapter, TenantConnectionContract connection)
    {
        if (string.Equals(connection.ConnectorId, adapter.ProviderId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (connection.Config.TryGetValue("provider", out var provider) &&
            string.Equals(provider, adapter.ProviderId, StringComparison.OrdinalIgnoreCase))
            return true;

        return adapter.ProviderId switch
        {
            "meta" => string.Equals(connection.ConnectorId, "whatsapp-business", StringComparison.OrdinalIgnoreCase),
            "openai" => string.Equals(connection.ConnectorId, "openai", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(connection.ConnectorId, "rest-api", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private IReadOnlyDictionary<string, string> ReadSecret(TenantConnectionSecretContract? secret)
    {
        if (secret is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var protector = _dataProtectionProvider.CreateProtector("tenant-connections-secrets-v1");
        var plain = protector.Unprotect(secret.CipherText);
        if (plain.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["secret"] = plain
        };
    }
}
