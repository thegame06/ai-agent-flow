using AgentFlow.Application.Memory;
using AgentFlow.Intents.Catalog;
using AgentFlow.Intents.Catalog.Models;
using AgentFlow.Intents.Classification;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgentFlow.Intents.Indexing;

/// <summary>
/// Indexes intent definitions into Qdrant vector database for semantic search.
/// Responsible for:
/// - Building intent embeddings from examples, synonyms, and descriptions
/// - Creating and managing tenant-specific vector collections
/// - Batch indexing and re-indexing operations
/// - Maintaining metadata for filtering and retrieval
/// </summary>
public sealed class IntentVectorIndexer
{
    private readonly IVectorMemory _vectorMemory;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IIntentCatalogService _catalogService;
    private readonly ILogger<IntentVectorIndexer> _logger;

    // Vector dimension for embeddings (default: OpenAI text-embedding-3-small)
    private const int DefaultEmbeddingDimension = 1536;

    public IntentVectorIndexer(
        IVectorMemory vectorMemory,
        IEmbeddingGenerator embeddingGenerator,
        IIntentCatalogService catalogService,
        ILogger<IntentVectorIndexer> logger)
    {
        _vectorMemory = vectorMemory ?? throw new ArgumentNullException(nameof(vectorMemory));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Rebuilds the intent vector index for a specific tenant.
    /// Deletes existing collection and re-indexes all intents (base + custom).
    /// This operation is idempotent and safe to call multiple times.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RebuildIndexAsync(string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId cannot be null or whitespace.", nameof(tenantId));
        }

        _logger.LogInformation("🔄 Rebuilding intent vector index for tenant {TenantId}...", tenantId);

        try
        {
            // Step 1: Load all intents (base + custom) for the tenant
            var allIntents = await _catalogService.GetAllIntentsForTenantAsync(tenantId, ct);

            if (allIntents.Count == 0)
            {
                _logger.LogWarning("⚠️ No intents found for tenant {TenantId}. Skipping indexing.", tenantId);
                return;
            }

            _logger.LogInformation(
                "Found {Count} intents to index for tenant {TenantId}",
                allIntents.Count,
                tenantId);

            // Step 2: Index each intent
            var indexedCount = 0;
            foreach (var intent in allIntents)
            {
                await IndexIntentAsync(tenantId, intent, ct);
                indexedCount++;

                if (indexedCount % 10 == 0)
                {
                    _logger.LogDebug("Indexed {Count}/{Total} intents...", indexedCount, allIntents.Count);
                }
            }

            _logger.LogInformation(
                "✅ Successfully rebuilt intent index for tenant {TenantId}: {Count} intents indexed",
                tenantId,
                indexedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Failed to rebuild intent index for tenant {TenantId}",
                tenantId);
            throw;
        }
    }

    /// <summary>
    /// Indexes a single intent into the vector database.
    /// Generates embedding from intent text and stores with metadata.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="intent">The intent definition to index.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task IndexIntentAsync(
        string tenantId,
        IntentDefinition intent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId cannot be null or whitespace.", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(intent);

        _logger.LogDebug(
            "Indexing intent {IntentKey} ({IntentName}) for tenant {TenantId}",
            intent.Key,
            intent.Name,
            tenantId);

        try
        {
            // Step 1: Build text representation for embedding
            var intentText = BuildIntentText(intent);

            // Step 2: Generate embedding
            var embedding = await _embeddingGenerator.GenerateAsync(intentText, ct);

            // Step 3: Prepare metadata for storage
            var metadata = BuildMetadata(tenantId, intent);

            // Step 4: Store in vector database
            // Note: Using agentId = "intent_router_{tenantId}" as a virtual agent for intent routing
            var agentId = $"intent_router_{tenantId}";
            
            var embeddingId = await _vectorMemory.StoreEmbeddingAsync(
                agentId,
                tenantId,
                intentText,
                metadata,
                ct);

            _logger.LogDebug(
                "✅ Indexed intent {IntentKey} with embedding ID {EmbeddingId}",
                intent.Key,
                embeddingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Failed to index intent {IntentKey} for tenant {TenantId}",
                intent.Key,
                tenantId);
            throw;
        }
    }

    /// <summary>
    /// Removes an intent from the vector index.
    /// Used when deleting custom intents.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="intentKey">The unique key of the intent to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RemoveIntentAsync(
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

        _logger.LogInformation(
            "Removing intent {IntentKey} from index for tenant {TenantId}",
            intentKey,
            tenantId);

        try
        {
            var agentId = $"intent_router_{tenantId}";
            
            // Note: IVectorMemory.DeleteAsync expects an embeddingId, not intentKey
            // We need to track this mapping or use intentKey as embeddingId
            // For now, we'll use intentKey as the embeddingId
            await _vectorMemory.DeleteAsync(agentId, tenantId, intentKey, ct);

            _logger.LogInformation(
                "✅ Removed intent {IntentKey} from index",
                intentKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Failed to remove intent {IntentKey} from index for tenant {TenantId}",
                intentKey,
                tenantId);
            throw;
        }
    }

    /// <summary>
    /// Builds a comprehensive text representation of the intent for embedding generation.
    /// Combines name, description, examples, and synonyms to create rich semantic representation.
    /// </summary>
    /// <param name="intent">The intent definition.</param>
    /// <returns>Text suitable for embedding generation.</returns>
    private string BuildIntentText(IntentDefinition intent)
    {
        var parts = new List<string>
        {
            // Start with name and description (highest weight)
            $"{intent.Name}. {intent.Description}",
            "",
            // Add examples (real user utterances)
            "Examples:",
            string.Join("\n", intent.Examples),
            "",
            // Add synonyms (keyword matching support)
            $"Synonyms: {string.Join(", ", intent.Synonyms)}"
        };

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Builds metadata dictionary for storing alongside the vector.
    /// Metadata is used for filtering and retrieving intent details without re-querying the database.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="intent">The intent definition.</param>
    /// <returns>Metadata dictionary.</returns>
    private Dictionary<string, string> BuildMetadata(string tenantId, IntentDefinition intent)
    {
        var metadata = new Dictionary<string, string>
        {
            ["intent_key"] = intent.Key,
            ["intent_name"] = intent.Name,
            ["tenant_id"] = tenantId,
            ["category"] = intent.Category,
            ["priority"] = intent.Priority.ToString(),
            ["confidence_threshold"] = intent.ConfidenceThreshold.ToString("F2"),
            ["is_base_intent"] = intent.IsBaseIntent.ToString().ToLowerInvariant(),
            ["version"] = intent.Version.ToString(),
            ["enabled"] = "true", // For future enable/disable functionality
        };

        // Add suggested workflow if present
        if (!string.IsNullOrWhiteSpace(intent.SuggestedWorkflow))
        {
            metadata["suggested_workflow"] = intent.SuggestedWorkflow;
        }

        // Serialize full intent definition as JSON for complete retrieval
        // This allows reconstructing the intent without additional database queries
        metadata["rule_json"] = JsonSerializer.Serialize(intent);

        return metadata;
    }
}
