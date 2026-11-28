using AgentFlow.Api.Connect;
using Microsoft.AspNetCore.DataProtection;

namespace AgentFlow.Api.Voice;

/// <summary>The tenant's Twilio account (SID and auth token) from its Twilio connection.</summary>
public sealed record TwilioAccount(string AccountSid, string AuthToken);

public interface ITwilioCredentials
{
    Task<TwilioAccount?> GetAsync(string tenantId, string? connectionId = null, CancellationToken ct = default);
}

/// <summary>Reads the Twilio connection (a channel's own, or the tenant's) and decrypts its auth token.</summary>
public sealed class TwilioCredentials : ITwilioCredentials
{
    private readonly ITenantConnectionStore _connections;
    private readonly IDataProtectionProvider _dataProtection;

    public TwilioCredentials(ITenantConnectionStore connections, IDataProtectionProvider dataProtection)
    {
        _connections = connections;
        _dataProtection = dataProtection;
    }

    public async Task<TwilioAccount?> GetAsync(string tenantId, string? connectionId = null, CancellationToken ct = default)
    {
        var all = await _connections.GetConnectionsAsync(tenantId, ct);
        var twilio = (string.IsNullOrWhiteSpace(connectionId) ? null : all.FirstOrDefault(x => x.Id == connectionId))
            ?? all.FirstOrDefault(x =>
                string.Equals(x.ConnectorId, "twilio", StringComparison.OrdinalIgnoreCase) ||
                (x.Config.TryGetValue("provider", out var provider) && string.Equals(provider, "twilio", StringComparison.OrdinalIgnoreCase)));
        if (twilio is null) return null;

        var accountSid = twilio.Config.GetValueOrDefault("accountSid") ?? twilio.Config.GetValueOrDefault("account");
        var token = await AuthTokenAsync(tenantId, twilio.Id, ct);
        return string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(token) ? null : new TwilioAccount(accountSid!, token!);
    }

    public async Task<string?> AuthTokenAsync(string tenantId, string connectionId, CancellationToken ct)
    {
        var secret = await _connections.GetSecretAsync(tenantId, connectionId, ct);
        if (secret is null) return null;

        var plain = _dataProtection.CreateProtector("tenant-connections-secrets-v1").Unprotect(secret.CipherText);
        if (!plain.TrimStart().StartsWith("{", StringComparison.Ordinal))
            return plain;

        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(plain)
                  ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "authToken", "token", "secret" })
            if (map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        return null;
    }
}
