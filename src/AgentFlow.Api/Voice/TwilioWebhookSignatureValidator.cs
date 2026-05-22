using System.Security.Cryptography;
using System.Text;
using AgentFlow.Api.Connect;
using Microsoft.AspNetCore.DataProtection;

namespace AgentFlow.Api.Voice;

public interface ITwilioWebhookSignatureValidator
{
    Task<bool> IsValidAsync(
        string tenantId,
        HttpRequest request,
        CancellationToken ct = default);
}

public sealed class TwilioWebhookSignatureValidator : ITwilioWebhookSignatureValidator
{
    private readonly ITenantConnectionStore _connectionStore;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<TwilioWebhookSignatureValidator> _logger;

    public TwilioWebhookSignatureValidator(
        ITenantConnectionStore connectionStore,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<TwilioWebhookSignatureValidator> logger)
    {
        _connectionStore = connectionStore;
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    public async Task<bool> IsValidAsync(
        string tenantId,
        HttpRequest request,
        CancellationToken ct = default)
    {
        var providedSignature = request.Headers["X-Twilio-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedSignature))
        {
            _logger.LogWarning("Twilio webhook missing X-Twilio-Signature header.");
            return false;
        }

        var authToken = await ResolveTwilioAuthTokenAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(authToken))
        {
            _logger.LogWarning("No Twilio auth token found for tenant {TenantId}. Rejecting signed webhook validation.", tenantId);
            return false;
        }

        var absoluteUrl = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}";
        var form = request.HasFormContentType
            ? request.Form.ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var expectedSignature = BuildTwilioSignature(absoluteUrl, form, authToken!);

        var isValid = FixedTimeEquals(expectedSignature, providedSignature!);
        if (!isValid)
        {
            _logger.LogWarning("Twilio webhook signature mismatch for tenant {TenantId}.", tenantId);
        }

        return isValid;
    }

    private async Task<string?> ResolveTwilioAuthTokenAsync(string tenantId, CancellationToken ct)
    {
        var connections = await _connectionStore.GetConnectionsAsync(tenantId, ct);
        var twilio = connections.FirstOrDefault(x =>
            string.Equals(x.ConnectorId, "twilio", StringComparison.OrdinalIgnoreCase) ||
            (x.Config.TryGetValue("provider", out var provider) &&
             string.Equals(provider, "twilio", StringComparison.OrdinalIgnoreCase)));
        if (twilio is null)
            return null;

        var secret = await _connectionStore.GetSecretAsync(tenantId, twilio.Id, ct);
        if (secret is null)
            return null;

        var protector = _dataProtectionProvider.CreateProtector("tenant-connections-secrets-v1");
        var plain = protector.Unprotect(secret.CipherText);
        if (!plain.TrimStart().StartsWith("{", StringComparison.Ordinal))
            return plain;

        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(plain)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (map.TryGetValue("authToken", out var authToken) && !string.IsNullOrWhiteSpace(authToken))
            return authToken;
        if (map.TryGetValue("token", out var token) && !string.IsNullOrWhiteSpace(token))
            return token;
        if (map.TryGetValue("secret", out var secretToken) && !string.IsNullOrWhiteSpace(secretToken))
            return secretToken;
        return null;
    }

    private static string BuildTwilioSignature(string url, IReadOnlyDictionary<string, string> form, string authToken)
    {
        var payload = new StringBuilder(url);
        foreach (var kv in form.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            payload.Append(kv.Key);
            payload.Append(kv.Value);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload.ToString()));
        return Convert.ToBase64String(hash);
    }

    private static bool FixedTimeEquals(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
