using AgentFlow.Abstractions;
using AgentFlow.ModelRouting;
using MongoDB.Driver;

namespace AgentFlow.Api.AuthProfiles;

public sealed record ModelCatalogEntry
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string ModelId { get; init; }
    public required string ProviderId { get; init; }
    public required string DisplayName { get; init; }
    public required string Tier { get; init; }
    public double CostPer1KTokens { get; init; }
    public int MaxContextTokens { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public interface IModelCatalogStore
{
    IReadOnlyList<ModelCatalogEntry> List(string tenantId);
    IReadOnlyList<ModelCatalogEntry> ListAll();
    ModelCatalogEntry? Get(string tenantId, string modelId);
    void Upsert(ModelCatalogEntry entry);
    bool Delete(string tenantId, string modelId);
}

public sealed class MongoModelCatalogStore : IModelCatalogStore
{
    private readonly IMongoCollection<ModelCatalogEntry> _models;

    public MongoModelCatalogStore(IMongoDatabase database)
    {
        _models = database.GetCollection<ModelCatalogEntry>("model_routing_models");
    }

    public IReadOnlyList<ModelCatalogEntry> List(string tenantId)
    {
        return _models.Find(Builders<ModelCatalogEntry>.Filter.Eq(x => x.TenantId, tenantId))
            .SortBy(x => x.ProviderId)
            .ThenBy(x => x.ModelId)
            .ToList();
    }

    public IReadOnlyList<ModelCatalogEntry> ListAll()
    {
        return _models.Find(Builders<ModelCatalogEntry>.Filter.Empty).ToList();
    }

    public ModelCatalogEntry? Get(string tenantId, string modelId)
    {
        return _models.Find(ModelFilter(tenantId, modelId)).FirstOrDefault();
    }

    public void Upsert(ModelCatalogEntry entry)
    {
        _models.ReplaceOne(ModelFilter(entry.TenantId, entry.ModelId), entry, new ReplaceOptions { IsUpsert = true });
    }

    public bool Delete(string tenantId, string modelId)
    {
        return _models.DeleteOne(ModelFilter(tenantId, modelId)).DeletedCount > 0;
    }

    private static FilterDefinition<ModelCatalogEntry> ModelFilter(string tenantId, string modelId) =>
        Builders<ModelCatalogEntry>.Filter.Eq(x => x.TenantId, tenantId) &
        Builders<ModelCatalogEntry>.Filter.Eq(x => x.ModelId, modelId);
}

public static class ConfiguredModelProviderFactory
{
    public static ConfiguredModelProvider Create(
        ModelCatalogEntry entry,
        IAuthProfilesStore authProfiles)
    {
        return new ConfiguredModelProvider(
            entry.ModelId,
            entry.ProviderId,
            new ModelMetadata
            {
                DisplayName = entry.DisplayName,
                CostPer1KTokens = entry.CostPer1KTokens,
                MaxContextTokens = entry.MaxContextTokens,
                Tier = entry.Tier
            },
            _ => Task.FromResult(HasProviderConfiguration(entry.TenantId, entry.ModelId, entry.ProviderId, authProfiles)));
    }

    public static ConfiguredModelProvider Create(
        string tenantId,
        string modelId,
        string providerId,
        ModelMetadata metadata,
        IAuthProfilesStore authProfiles)
    {
        return Create(new ModelCatalogEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            ModelId = modelId,
            ProviderId = providerId,
            DisplayName = metadata.DisplayName,
            CostPer1KTokens = metadata.CostPer1KTokens,
            MaxContextTokens = metadata.MaxContextTokens,
            Tier = metadata.Tier,
            UpdatedAt = DateTimeOffset.UtcNow
        }, authProfiles);
    }

    private static bool HasProviderConfiguration(
        string tenantId,
        string modelId,
        string providerId,
        IAuthProfilesStore authProfiles)
    {
        if (!string.Equals(providerId, "OpenAI", StringComparison.OrdinalIgnoreCase))
            return false;

        var linkedProfileId = authProfiles.GetModelProfileId(tenantId, modelId);
        if (string.IsNullOrWhiteSpace(linkedProfileId))
            return false;

        var profile = authProfiles.Get(tenantId, linkedProfileId);
        return profile is not null &&
            (profile.ExpiresAt is null || profile.ExpiresAt > DateTimeOffset.UtcNow) &&
            string.Equals(profile.Provider, providerId, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ModelRoutingBootstrapService : IHostedService
{
    private readonly IModelCatalogStore _catalog;
    private readonly IModelRegistry _registry;
    private readonly IAuthProfilesStore _authProfiles;
    private readonly ILogger<ModelRoutingBootstrapService> _logger;

    public ModelRoutingBootstrapService(
        IModelCatalogStore catalog,
        IModelRegistry registry,
        IAuthProfilesStore authProfiles,
        ILogger<ModelRoutingBootstrapService> logger)
    {
        _catalog = catalog;
        _registry = registry;
        _authProfiles = authProfiles;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in _catalog.ListAll())
        {
            _registry.Register(ConfiguredModelProviderFactory.Create(entry, _authProfiles));
        }

        _logger.LogInformation(
            "Loaded {ModelCount} configured models into the routing registry.",
            _registry.GetAvailableModelIds().Count);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
