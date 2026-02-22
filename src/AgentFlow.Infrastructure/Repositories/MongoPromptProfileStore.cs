using AgentFlow.Prompting;
using MongoDB.Driver;

namespace AgentFlow.Infrastructure.Repositories;

public sealed class MongoPromptProfileStore : IPromptProfileStore
{
    private readonly IMongoCollection<PromptProfile> _collection;

    public MongoPromptProfileStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<PromptProfile>("prompt_profiles");
        
        // Indices
        var indexKeys = Builders<PromptProfile>.IndexKeys
            .Ascending(x => x.TenantId)
            .Ascending(x => x.ProfileId)
            .Descending(x => x.Version);
            
        _collection.Indexes.CreateOne(new CreateIndexModel<PromptProfile>(indexKeys));
    }

    public async Task<PromptProfile?> GetAsync(string profileId, string tenantId, string? version = null, CancellationToken ct = default)
    {
        if (version != null)
        {
            return await _collection.Find(x => x.TenantId == tenantId && x.ProfileId == profileId && x.Version == version)
                .FirstOrDefaultAsync(ct);
        }

        return await _collection.Find(x => x.TenantId == tenantId && x.ProfileId == profileId && x.IsPublished)
            .SortByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SaveAsync(PromptProfile profile, CancellationToken ct = default)
    {
        // Upsert by Tenant+Profile+Version
        await _collection.ReplaceOneAsync(
            x => x.TenantId == profile.TenantId && x.ProfileId == profile.ProfileId && x.Version == profile.Version,
            profile,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    public async Task<IReadOnlyList<PromptProfile>> ListPublishedAsync(string tenantId, CancellationToken ct = default)
    {
        return await _collection.Find(x => x.TenantId == tenantId && x.IsPublished)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }
}
