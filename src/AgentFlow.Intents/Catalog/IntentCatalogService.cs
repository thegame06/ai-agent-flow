using AgentFlow.Intents.Catalog.Models;
using Microsoft.Extensions.Logging;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentFlow.Intents.Catalog;

/// <summary>
/// Implementation of IIntentCatalogService.
/// Loads base intents from embedded YAML resource and manages tenant-specific custom intents in MongoDB.
/// </summary>
public sealed class IntentCatalogService : IIntentCatalogService
{
    private readonly ILogger<IntentCatalogService> _logger;
    
    // Lazy-loaded cache of base intents (immutable after load)
    private readonly Lazy<Task<IReadOnlyList<IntentDefinition>>> _baseIntentsCache;

    // Embedded resource name for base-intents.yaml
    private const string BaseIntentsResourceName = "AgentFlow.Intents.Catalog.base-intents.yaml";

    public IntentCatalogService(
        ILogger<IntentCatalogService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize lazy cache for base intents
        _baseIntentsCache = new Lazy<Task<IReadOnlyList<IntentDefinition>>>(LoadBaseIntentsFromYamlAsync);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IntentDefinition>> GetBaseIntentsAsync(CancellationToken ct = default)
    {
        // Return cached base intents (loaded once on first access)
        return _baseIntentsCache.Value;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IntentDefinition>> GetTenantIntentsAsync(
        string tenantId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId cannot be null or whitespace.", nameof(tenantId));
        }

        _logger.LogDebug("Loading custom intents for tenant {TenantId}", tenantId);

        // Load custom intents from database
        // Note: IIntentRoutingStore doesn't have this method yet, we'll add a placeholder
        // In a real implementation, this would query MongoDB for tenant-specific intents
        
        // TODO: Implement GetCustomIntentsAsync in IIntentRoutingStore
        // For now, return empty list
        _logger.LogWarning("Tenant custom intents not yet implemented. Returning empty list.");
        return Array.Empty<IntentDefinition>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IntentDefinition>> GetAllIntentsForTenantAsync(
        string tenantId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId cannot be null or whitespace.", nameof(tenantId));
        }

        _logger.LogDebug("Loading all intents (base + custom) for tenant {TenantId}", tenantId);

        // Load both base and tenant-specific intents
        var baseIntents = await GetBaseIntentsAsync(ct);
        var tenantIntents = await GetTenantIntentsAsync(tenantId, ct);

        // Combine them (tenant intents can override base intents if keys match)
        var allIntents = new List<IntentDefinition>(baseIntents);
        allIntents.AddRange(tenantIntents);

        _logger.LogInformation(
            "Loaded {TotalCount} intents for tenant {TenantId} ({BaseCount} base + {CustomCount} custom)",
            allIntents.Count, tenantId, baseIntents.Count, tenantIntents.Count);

        return allIntents;
    }

    /// <inheritdoc />
    public Task<IntentDefinition> CreateCustomIntentAsync(
        string tenantId,
        IntentDefinition intent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId cannot be null or whitespace.", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(intent);

        if (intent.IsBaseIntent)
        {
            throw new InvalidOperationException("Cannot create a custom intent with IsBaseIntent=true. Use FromYaml with isBaseIntent=false.");
        }

        _logger.LogInformation("Creating custom intent {IntentKey} for tenant {TenantId}", intent.Key, tenantId);

        // TODO: Implement persistence logic in IIntentRoutingStore
        throw new NotImplementedException("Custom intent persistence not yet implemented. Will be added in future phase.");
    }

    /// <inheritdoc />
    public Task<IntentDefinition> UpdateCustomIntentAsync(
        string tenantId,
        string intentKey,
        IntentDefinition intent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId cannot be null or whitespace.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(intentKey))
        {
            throw new ArgumentException("IntentKey cannot be null or whitespace.", nameof(intentKey));
        }

        ArgumentNullException.ThrowIfNull(intent);

        if (intent.IsBaseIntent)
        {
            throw new InvalidOperationException("Cannot update a base intent. Base intents are immutable.");
        }

        _logger.LogInformation("Updating custom intent {IntentKey} for tenant {TenantId}", intentKey, tenantId);

        // TODO: Implement update logic in IIntentRoutingStore
        throw new NotImplementedException("Custom intent update not yet implemented. Will be added in future phase.");
    }

    /// <inheritdoc />
    public Task DeleteCustomIntentAsync(
        string tenantId,
        string intentKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId cannot be null or whitespace.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(intentKey))
        {
            throw new ArgumentException("IntentKey cannot be null or whitespace.", nameof(intentKey));
        }

        _logger.LogInformation("Deleting custom intent {IntentKey} for tenant {TenantId}", intentKey, tenantId);

        // TODO: Implement delete logic in IIntentRoutingStore
        throw new NotImplementedException("Custom intent deletion not yet implemented. Will be added in future phase.");
    }

    /// <summary>
    /// Loads base intents from the embedded YAML resource.
    /// This is called once on first access and the result is cached.
    /// </summary>
    private async Task<IReadOnlyList<IntentDefinition>> LoadBaseIntentsFromYamlAsync()
    {
        _logger.LogInformation("Loading base intents from embedded YAML resource...");

        try
        {
            // Load YAML content from embedded resource
            var yamlContent = await LoadEmbeddedYamlAsync();

            // Deserialize YAML to IntentCatalog model
            var catalog = DeserializeYaml(yamlContent);

            // Convert YAML definitions to domain models
            var baseIntents = catalog.Intents
                .Select(yaml => IntentDefinition.FromYaml(yaml, isBaseIntent: true, version: 1))
                .ToList()
                .AsReadOnly();

            _logger.LogInformation(
                "✅ Successfully loaded {Count} base intents from catalog version {Version}",
                baseIntents.Count,
                catalog.Version);

            return baseIntents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to load base intents from YAML. Intent routing will not function properly.");
            throw new InvalidOperationException("Failed to load base intents catalog. See inner exception for details.", ex);
        }
    }

    /// <summary>
    /// Loads the base-intents.yaml file from embedded resources.
    /// </summary>
    private async Task<string> LoadEmbeddedYamlAsync()
    {
        var assembly = Assembly.GetExecutingAssembly();
        
        _logger.LogDebug("Attempting to load embedded resource: {ResourceName}", BaseIntentsResourceName);

        // Try to load as embedded resource
        using var stream = assembly.GetManifestResourceStream(BaseIntentsResourceName);

        if (stream == null)
        {
            // Fallback: Try to load from file system (useful during development)
            var filePath = Path.Combine(AppContext.BaseDirectory, "Catalog", "base-intents.yaml");
            
            _logger.LogWarning(
                "Embedded resource not found. Attempting to load from file system: {FilePath}",
                filePath);

            if (File.Exists(filePath))
            {
                return await File.ReadAllTextAsync(filePath);
            }

            // List available embedded resources for debugging
            var availableResources = assembly.GetManifestResourceNames();
            _logger.LogError(
                "Could not find base-intents.yaml. Available embedded resources: {Resources}",
                string.Join(", ", availableResources));

            throw new FileNotFoundException(
                $"Could not find base-intents.yaml as embedded resource ({BaseIntentsResourceName}) or file ({filePath}).");
        }

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        _logger.LogDebug("Successfully loaded YAML content ({Length} bytes)", content.Length);

        return content;
    }

    /// <summary>
    /// Deserializes YAML content into IntentCatalog model using YamlDotNet.
    /// </summary>
    private IntentCatalog DeserializeYaml(string yamlContent)
    {
        _logger.LogDebug("Deserializing YAML content...");

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance) // Matches YAML snake_case naming
            .IgnoreUnmatchedProperties() // Ignore extra fields in YAML
            .Build();

        var catalog = deserializer.Deserialize<IntentCatalog>(yamlContent);

        if (catalog == null)
        {
            throw new InvalidOperationException("YAML deserialization returned null. Check YAML structure.");
        }

        _logger.LogDebug(
            "Successfully deserialized catalog: {Name}, Version {Version}, {IntentCount} intents",
            catalog.Metadata.Name,
            catalog.Version,
            catalog.Intents.Count);

        return catalog;
    }
}
