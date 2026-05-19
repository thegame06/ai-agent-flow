using AgentFlow.Intents.Catalog;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Intents.Indexing;

/// <summary>
/// Background service that bootstraps the intent routing system on application startup.
/// Responsibilities:
/// - Loads base intents from catalog YAML
/// - Validates intent definitions
/// - Optionally pre-indexes intents for default/system tenant
/// - Ensures the system is ready for intent routing
/// 
/// This service runs once during application startup and fails fast if base intents cannot be loaded.
/// Without base intents, the intent routing system cannot function properly.
/// </summary>
public sealed class IntentBootstrapService : IHostedService
{
    private readonly IIntentCatalogService _catalogService;
    private readonly IntentVectorIndexer _indexer;
    private readonly ILogger<IntentBootstrapService> _logger;

    // Optional: System tenant ID for pre-indexing base intents
    // Set this if you want to pre-index intents for a default tenant
    private const string? SystemTenantId = null; // Set to "system" or specific tenant ID if needed

    public IntentBootstrapService(
        IIntentCatalogService catalogService,
        IntentVectorIndexer indexer,
        ILogger<IntentBootstrapService> logger)
    {
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes on application startup.
    /// Loads base intents and optionally indexes them for a system tenant.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 Starting Intent Bootstrap Service...");

        var startTime = DateTimeOffset.UtcNow;

        try
        {
            // Step 1: Load base intents from catalog YAML
            _logger.LogInformation("Loading base intents from catalog...");
            var baseIntents = await _catalogService.GetBaseIntentsAsync(cancellationToken);

            if (baseIntents.Count == 0)
            {
                _logger.LogError("❌ No base intents loaded from catalog. Intent routing will not function.");
                throw new InvalidOperationException(
                    "Base intents catalog is empty. Check that base-intents.yaml is properly embedded or accessible.");
            }

            _logger.LogInformation(
                "✅ Successfully loaded {Count} base intents from catalog",
                baseIntents.Count);

            // Step 2: Validate intent definitions
            ValidateIntents(baseIntents);
            _logger.LogInformation("✅ All intent definitions validated successfully");

            // Step 3: Log intent categories summary
            LogIntentsSummary(baseIntents);

            // Step 4: Optional - Pre-index intents for system tenant
            if (!string.IsNullOrWhiteSpace(SystemTenantId))
            {
                _logger.LogInformation(
                    "Pre-indexing base intents for system tenant {TenantId}...",
                    SystemTenantId);

                await _indexer.RebuildIndexAsync(SystemTenantId, cancellationToken);

                _logger.LogInformation(
                    "✅ Pre-indexed {Count} intents for system tenant {TenantId}",
                    baseIntents.Count,
                    SystemTenantId);
            }
            else
            {
                _logger.LogInformation(
                    "ℹ️ Skipping pre-indexing (SystemTenantId not configured). " +
                    "Intents will be indexed on-demand when tenants make their first requests.");
            }

            var elapsed = DateTimeOffset.UtcNow - startTime;
            _logger.LogInformation(
                "✅ Intent Bootstrap Service completed successfully in {ElapsedMs}ms",
                elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "❌ CRITICAL: Failed to bootstrap intent routing system. Application cannot start properly.");
            
            // Fail fast: Without base intents, the system cannot route conversations
            throw new InvalidOperationException(
                "Intent Bootstrap Service failed. See inner exception for details.",
                ex);
        }
    }

    /// <summary>
    /// Executes on application shutdown. No cleanup needed for this service.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Intent Bootstrap Service stopping (no cleanup required).");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates intent definitions for common issues.
    /// Throws if validation fails.
    /// </summary>
    private void ValidateIntents(IReadOnlyList<Catalog.Models.IntentDefinition> intents)
    {
        var errors = new List<string>();

        // Check for duplicate keys
        var duplicateKeys = intents
            .GroupBy(i => i.Key)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateKeys.Any())
        {
            errors.Add($"Duplicate intent keys found: {string.Join(", ", duplicateKeys)}");
        }

        // Check for empty examples
        var intentsWithoutExamples = intents
            .Where(i => i.Examples.Count == 0)
            .Select(i => i.Key)
            .ToList();

        if (intentsWithoutExamples.Any())
        {
            _logger.LogWarning(
                "⚠️ Intents without examples (semantic matching may be poor): {Keys}",
                string.Join(", ", intentsWithoutExamples));
        }

        // Check for invalid confidence thresholds
        var invalidThresholds = intents
            .Where(i => i.ConfidenceThreshold < 0 || i.ConfidenceThreshold > 1)
            .Select(i => i.Key)
            .ToList();

        if (invalidThresholds.Any())
        {
            errors.Add($"Invalid confidence thresholds (must be 0.0-1.0): {string.Join(", ", invalidThresholds)}");
        }

        // Check for negative priorities
        var negativePriorities = intents
            .Where(i => i.Priority < 0)
            .Select(i => i.Key)
            .ToList();

        if (negativePriorities.Any())
        {
            errors.Add($"Negative priorities found: {string.Join(", ", negativePriorities)}");
        }

        if (errors.Any())
        {
            var errorMessage = $"Intent validation failed:\n{string.Join("\n", errors)}";
            _logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }
    }

    /// <summary>
    /// Logs a summary of loaded intents by category.
    /// </summary>
    private void LogIntentsSummary(IReadOnlyList<Catalog.Models.IntentDefinition> intents)
    {
        var byCategory = intents
            .GroupBy(i => i.Category)
            .OrderByDescending(g => g.Count())
            .ToList();

        _logger.LogInformation("📊 Intent Distribution by Category:");
        foreach (var group in byCategory)
        {
            _logger.LogInformation(
                "  - {Category}: {Count} intents",
                group.Key,
                group.Count());
        }

        // Log priority distribution
        var highPriority = intents.Count(i => i.Priority >= 400);
        var mediumPriority = intents.Count(i => i.Priority >= 200 && i.Priority < 400);
        var lowPriority = intents.Count(i => i.Priority < 200);

        _logger.LogInformation(
            "📊 Priority Distribution: High={High}, Medium={Medium}, Low={Low}",
            highPriority,
            mediumPriority,
            lowPriority);

        // Log confidence threshold range
        var avgThreshold = intents.Average(i => i.ConfidenceThreshold);
        var minThreshold = intents.Min(i => i.ConfidenceThreshold);
        var maxThreshold = intents.Max(i => i.ConfidenceThreshold);

        _logger.LogInformation(
            "📊 Confidence Thresholds: Avg={Avg:F2}, Min={Min:F2}, Max={Max:F2}",
            avgThreshold,
            minThreshold,
            maxThreshold);
    }
}
