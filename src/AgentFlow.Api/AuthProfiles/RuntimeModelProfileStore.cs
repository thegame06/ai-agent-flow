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
