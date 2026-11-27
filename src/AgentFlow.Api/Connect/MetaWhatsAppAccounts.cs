using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AgentFlow.Api.Connect;

/// <summary>
/// A tenant's WhatsApp Business (Meta Cloud API) account, read from its tenant connection:
/// config holds the ids, the encrypted secret holds the access token and the app secret.
/// </summary>
public sealed record MetaWhatsAppAccount
{
    public string ConnectionId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    /// <summary>WhatsApp Business Account id: templates live here.</summary>
    public string? BusinessAccountId { get; init; }
    public string? PhoneNumberId { get; init; }
    public string? AppId { get; init; }
    public string BaseUrl { get; init; } = MetaWhatsAppAccounts.DefaultBaseUrl;
    public string? AccessToken { get; init; }
    /// <summary>Signs the webhooks Meta sends (X-Hub-Signature-256).</summary>
    public string? AppSecret { get; init; }
}

public interface IMetaWhatsAppAccounts
{
    Task<IReadOnlyList<MetaWhatsAppAccount>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task<MetaWhatsAppAccount?> GetAsync(string tenantId, string connectionId, CancellationToken ct = default);
}

public sealed class MetaWhatsAppAccounts : IMetaWhatsAppAccounts
{
    public const string DefaultBaseUrl = "https://graph.facebook.com/v23.0";

    private readonly ITenantConnectionStore _connections;
    private readonly IDataProtectionProvider _dataProtection;

    public MetaWhatsAppAccounts(ITenantConnectionStore connections, IDataProtectionProvider dataProtection)
    {
        _connections = connections;
        _dataProtection = dataProtection;
    }

    public async Task<IReadOnlyList<MetaWhatsAppAccount>> GetAllAsync(string tenantId, CancellationToken ct = default)
    {
        var accounts = new List<MetaWhatsAppAccount>();
        foreach (var connection in (await _connections.GetConnectionsAsync(tenantId, ct)).Where(IsMeta))
            accounts.Add(await ToAccountAsync(tenantId, connection, ct));
        return accounts;
    }

    public async Task<MetaWhatsAppAccount?> GetAsync(string tenantId, string connectionId, CancellationToken ct = default)
    {
        var connection = await _connections.GetConnectionAsync(tenantId, connectionId, ct);
        return connection is null || !IsMeta(connection) ? null : await ToAccountAsync(tenantId, connection, ct);
    }

    /// <summary>Same matching as the provider resolver: connector "meta"/"whatsapp-business" or config provider=meta.</summary>
    public static bool IsMeta(TenantConnectionContract connection)
        => connection.ConnectorId is { } c &&
           (c.Equals("meta", StringComparison.OrdinalIgnoreCase) || c.Equals("whatsapp-business", StringComparison.OrdinalIgnoreCase)) ||
           (connection.Config.TryGetValue("provider", out var provider) && provider.Equals("meta", StringComparison.OrdinalIgnoreCase));

    private async Task<MetaWhatsAppAccount> ToAccountAsync(string tenantId, TenantConnectionContract connection, CancellationToken ct)
    {
        var secret = ReadSecret(await _connections.GetSecretAsync(tenantId, connection.Id, ct));
        string? Config(params string[] keys) => First(connection.Config, keys);

        return new MetaWhatsAppAccount
        {
            ConnectionId = connection.Id,
            Name = connection.Name,
            BusinessAccountId = Config("businessAccountId", "wabaId", "BusinessAccountId"),
            PhoneNumberId = Config("phoneNumberId", "PhoneNumberId"),
            AppId = Config("appId", "AppId"),
            BaseUrl = (Config("baseUrl") ?? DefaultBaseUrl).TrimEnd('/'),
            AccessToken = First(secret, "apiToken", "accessToken", "token", "secret"),
            AppSecret = First(secret, "appSecret", "app_secret")
        };
    }

    private IReadOnlyDictionary<string, string> ReadSecret(TenantConnectionSecretContract? secret)
    {
        if (secret is null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var plain = _dataProtection.CreateProtector("tenant-connections-secrets-v1").Unprotect(secret.CipherText);
        if (plain.TrimStart().StartsWith('{'))
            return new Dictionary<string, string>(
                JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? [], StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["secret"] = plain };
    }

    private static string? First(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            var match = values.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value)) return match.Value.Trim();
        }
        return null;
    }
}
