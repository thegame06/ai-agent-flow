using AgentFlow.Abstractions;
using MongoDB.Driver;

namespace AgentFlow.Api.AuthProfiles;

public sealed record RuntimeModelProfile
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RuntimeKind { get; init; } = "Text";
    public Dictionary<string, string> Roles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsDefault { get; init; } = false;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; init; } = string.Empty;
}

public static class RuntimeModelProfileMetadata
{
    public static string? GetRole(this RuntimeModelProfile? profile, string role)
    {
        if (profile?.Roles is null) return null;
        return profile.Roles.TryGetValue(role, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    public static string? GetMetadata(this RuntimeModelProfile? profile, params string[] keys)
    {
        if (profile?.Metadata is null) return null;
        foreach (var key in keys)
        {
            if (profile.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    public static AssistantVoiceConfig ToAssistantVoiceConfig(this RuntimeModelProfile profile, string fallbackLanguage = "es")
    {
        var model = profile.GetRole("tts") ?? "eleven_turbo_v2_5";
        return new AssistantVoiceConfig
        {
            Provider = profile.GetMetadata("voice.provider", "ttsProvider") ?? "11labs",
            VoiceId = profile.GetMetadata("voice.voiceId", "voiceId") ?? "nmvA11Y688M5reLqDsVm",
            Model = model,
            Language = profile.GetMetadata("voice.language", "language") ?? fallbackLanguage,
            Codec = profile.GetMetadata("voice.codec")
        };
    }

    public static AssistantTranscriberConfig ToAssistantTranscriberConfig(this RuntimeModelProfile profile, string fallbackLanguage = "es")
    {
        var model = profile.GetRole("stt") ?? "nova-3";
        return new AssistantTranscriberConfig
        {
            Provider = profile.GetMetadata("transcriber.provider", "sttProvider") ?? "deepgram",
            Model = model,
            Language = profile.GetMetadata("transcriber.language", "language") ?? fallbackLanguage,
            Codec = profile.GetMetadata("transcriber.codec")
        };
    }

    public static void ApplyExecutionMetadata(this RuntimeModelProfile profile, IDictionary<string, string> metadata)
    {
        var reasoningModel = profile.GetRole("brain") ?? profile.GetRole("reasoning");
        if (!string.IsNullOrWhiteSpace(reasoningModel))
        {
            metadata["reasoningModelCandidatesCsv"] = reasoningModel;
            metadata["providerCandidates.reasoning"] = reasoningModel;
        }

        var sttModel = profile.GetRole("stt");
        if (!string.IsNullOrWhiteSpace(sttModel))
            metadata["sttModelId"] = sttModel;

        var ttsModel = profile.GetRole("tts");
        if (!string.IsNullOrWhiteSpace(ttsModel))
            metadata["ttsModelId"] = ttsModel;

        CopyIfPresent(profile, metadata, "voice.provider", "ttsProvider");
        CopyIfPresent(profile, metadata, "voice.voiceId");
        CopyIfPresent(profile, metadata, "voice.language");
        CopyIfPresent(profile, metadata, "voice.codec");
        CopyIfPresent(profile, metadata, "transcriber.provider", "sttProvider");
        CopyIfPresent(profile, metadata, "transcriber.language");
        CopyIfPresent(profile, metadata, "transcriber.codec");
        CopyIfPresent(profile, metadata, "callControl.provider", "callControlProvider");
        CopyIfPresent(profile, metadata, "providerCandidates.stt", "sttProvidersCsv");
        CopyIfPresent(profile, metadata, "providerCandidates.tts", "ttsProvidersCsv");
        CopyIfPresent(profile, metadata, "providerCandidates.callControl", "callControlProvidersCsv");
    }

    private static void CopyIfPresent(RuntimeModelProfile profile, IDictionary<string, string> target, params string[] keys)
    {
        var value = profile.GetMetadata(keys);
        if (string.IsNullOrWhiteSpace(value)) return;

        foreach (var key in keys)
            target[key] = value;
    }
}

public interface IRuntimeModelProfileStore
{
    IReadOnlyList<RuntimeModelProfile> List(string tenantId, string? runtimeKind = null);
    RuntimeModelProfile? Get(string tenantId, string profileId);
    RuntimeModelProfile? GetDefault(string tenantId, string runtimeKind);
    void Upsert(RuntimeModelProfile profile);
    bool Delete(string tenantId, string profileId);
}

public sealed class MongoRuntimeModelProfileStore : IRuntimeModelProfileStore
{
    private readonly IMongoCollection<RuntimeModelProfile> _profiles;

    public MongoRuntimeModelProfileStore(IMongoDatabase database)
    {
        _profiles = database.GetCollection<RuntimeModelProfile>("runtime_model_profiles");
    }

    public IReadOnlyList<RuntimeModelProfile> List(string tenantId, string? runtimeKind = null)
    {
        var filter = Builders<RuntimeModelProfile>.Filter.Eq(x => x.TenantId, tenantId);
        if (!string.IsNullOrWhiteSpace(runtimeKind))
        {
            filter &= Builders<RuntimeModelProfile>.Filter.Eq(x => x.RuntimeKind, runtimeKind);
        }

        return _profiles
            .Find(filter)
            .SortByDescending(x => x.IsDefault).ThenBy(x => x.Name)
            .ToList();
    }

    public RuntimeModelProfile? Get(string tenantId, string profileId)
        => _profiles.Find(Filter(tenantId, profileId)).FirstOrDefault();

    public RuntimeModelProfile? GetDefault(string tenantId, string runtimeKind)
        => _profiles.Find(
                Builders<RuntimeModelProfile>.Filter.Eq(x => x.TenantId, tenantId)
                & Builders<RuntimeModelProfile>.Filter.Eq(x => x.RuntimeKind, runtimeKind)
                & Builders<RuntimeModelProfile>.Filter.Eq(x => x.IsDefault, true))
            .FirstOrDefault();

    public void Upsert(RuntimeModelProfile profile)
    {
        if (profile.IsDefault)
        {
            var unsetDefaultFilter = Builders<RuntimeModelProfile>.Filter.Eq(x => x.TenantId, profile.TenantId)
                & Builders<RuntimeModelProfile>.Filter.Eq(x => x.RuntimeKind, profile.RuntimeKind)
                & Builders<RuntimeModelProfile>.Filter.Ne(x => x.Id, profile.Id);
            var unsetDefaultUpdate = Builders<RuntimeModelProfile>.Update.Set(x => x.IsDefault, false);
            _profiles.UpdateMany(unsetDefaultFilter, unsetDefaultUpdate);
        }

        _profiles.ReplaceOne(Filter(profile.TenantId, profile.Id), profile, new ReplaceOptions { IsUpsert = true });
    }

    public bool Delete(string tenantId, string profileId)
    {
        var result = _profiles.DeleteOne(Filter(tenantId, profileId));
        return result.DeletedCount > 0;
    }

    private static FilterDefinition<RuntimeModelProfile> Filter(string tenantId, string profileId) =>
        Builders<RuntimeModelProfile>.Filter.Eq(x => x.TenantId, tenantId)
        & Builders<RuntimeModelProfile>.Filter.Eq(x => x.Id, profileId);
}
