using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace AgentFlow.Api.AuthProfiles;

public sealed record ProviderAuthProfile
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string Provider { get; init; }
    public required string ProfileId { get; init; }
    public required string AuthType { get; init; }
    public string? SecretMasked { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record UpsertProviderAuthProfileRequest
{
    public required string Provider { get; init; }
    public required string ProfileId { get; init; }
    public string AuthType { get; init; } = "api_key";
    public string? Secret { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

public interface IAuthProfilesStore
{
    ProviderAuthProfile Upsert(string tenantId, UpsertProviderAuthProfileRequest request);
    IReadOnlyList<ProviderAuthProfile> List(string tenantId, string? provider = null);
    ProviderAuthProfile? Get(string tenantId, string profileId);
    bool Delete(string tenantId, string profileId);
    bool LinkModelProfile(string tenantId, string modelId, string profileId);
    string? GetModelProfileId(string tenantId, string modelId);
}

internal sealed record StoredProfile
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string Provider { get; init; }
    public required string ProfileId { get; init; }
    public required string AuthType { get; init; }
    public string? SecretCipher { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed class InMemoryAuthProfilesStore : IAuthProfilesStore
{
    private readonly ConcurrentDictionary<string, StoredProfile> _profiles = new();
    private readonly ConcurrentDictionary<string, string> _modelBindings = new();

    public ProviderAuthProfile Upsert(string tenantId, UpsertProviderAuthProfileRequest request)
    {
        var key = ComposeProfileKey(tenantId, request.ProfileId);
        var created = DateTimeOffset.UtcNow;

        var profile = _profiles.AddOrUpdate(
            key,
            _ => new StoredProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                Provider = request.Provider,
                ProfileId = request.ProfileId,
                AuthType = request.AuthType,
                SecretCipher = Encrypt(request.Secret),
                CreatedAt = created,
                ExpiresAt = request.ExpiresAt,
                Metadata = request.Metadata
            },
            (_, existing) => existing with
            {
                Provider = request.Provider,
                AuthType = request.AuthType,
                SecretCipher = string.IsNullOrWhiteSpace(request.Secret) ? existing.SecretCipher : Encrypt(request.Secret),
                ExpiresAt = request.ExpiresAt,
                Metadata = request.Metadata
            });

        return ToPublic(profile);
    }

    public IReadOnlyList<ProviderAuthProfile> List(string tenantId, string? provider = null)
    {
        var query = _profiles.Values.Where(p => p.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(provider))
            query = query.Where(p => string.Equals(p.Provider, provider, StringComparison.OrdinalIgnoreCase));

        return query
            .OrderByDescending(x => x.CreatedAt)
            .Select(ToPublic)
            .ToList();
    }

    public ProviderAuthProfile? Get(string tenantId, string profileId)
    {
        return _profiles.TryGetValue(ComposeProfileKey(tenantId, profileId), out var profile)
            ? ToPublic(profile)
            : null;
    }

    public bool Delete(string tenantId, string profileId)
    {
        var removed = _profiles.TryRemove(ComposeProfileKey(tenantId, profileId), out _);
        if (!removed) return false;

        var prefix = $"{tenantId}:";
        foreach (var kv in _modelBindings.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal) && x.Value == profileId).ToList())
        {
            _modelBindings.TryRemove(kv.Key, out _);
        }

        return true;
    }

    public bool LinkModelProfile(string tenantId, string modelId, string profileId)
    {
        if (!_profiles.ContainsKey(ComposeProfileKey(tenantId, profileId)))
            return false;

        _modelBindings[ComposeModelKey(tenantId, modelId)] = profileId;
        return true;
    }

    public string? GetModelProfileId(string tenantId, string modelId)
    {
        return _modelBindings.TryGetValue(ComposeModelKey(tenantId, modelId), out var profileId)
            ? profileId
            : null;
    }

    private static string ComposeProfileKey(string tenantId, string profileId) => $"{tenantId}:{profileId}";
    private static string ComposeModelKey(string tenantId, string modelId) => $"{tenantId}:{modelId}";

    private static ProviderAuthProfile ToPublic(StoredProfile p) => new()
    {
        Id = p.Id,
        TenantId = p.TenantId,
        Provider = p.Provider,
        ProfileId = p.ProfileId,
        AuthType = p.AuthType,
        SecretMasked = Mask(Decrypt(p.SecretCipher)),
        CreatedAt = p.CreatedAt,
        ExpiresAt = p.ExpiresAt,
        Metadata = p.Metadata
    };

    // Demo-level reversible encryption for local dev; replace with KMS/libsecret in prod.
    private static string? Encrypt(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return null;

        using var aes = Aes.Create();
        aes.Key = GetKey();
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var packed = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, packed, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, packed, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(packed);
    }

    private static string? Decrypt(string? cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher)) return null;

        var packed = Convert.FromBase64String(cipher);
        using var aes = Aes.Create();
        aes.Key = GetKey();

        var iv = packed[..16];
        var cipherBytes = packed[16..];

        using var decryptor = aes.CreateDecryptor(aes.Key, iv);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
+
+    private static byte[] GetKey()
+    {
+        var keyMaterial = Environment.GetEnvironmentVariable("AGENTFLOW_AUTH_KEY")
+            ?? "agentflow-dev-key-change-me";
+        return SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
+    }

    private static string? Mask(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return null;
        if (secret.Length <= 6) return new string('*', secret.Length);
        return $"{secret[..3]}***{secret[^3..]}";
    }
}
