using AgentFlow.Application.Memory;
using AgentFlow.Intents.Catalog.Models;
using AgentFlow.Intents.Classification.Models;
using AgentFlow.Security;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgentFlow.Intents.Classification;

/// <summary>
/// Semantic intent matcher implementation using Qdrant vector database.
/// Provides enterprise-grade intent classification with:
/// - Multi-tenant isolation (collection per tenant)
/// - Channel-based filtering
/// - Confidence scoring with similarity thresholds
/// - Full audit trail of matching decisions
/// </summary>
public sealed class QdrantSemanticIntentMatcher : ISemanticIntentMatcher
{
    private readonly IVectorMemory _vectorMemory;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly ILogger<QdrantSemanticIntentMatcher> _logger;

    // Minimum similarity score to consider a match valid
    // Below this threshold, the intent is considered "no match"
    private const float DefaultMinScore = 0.75f;

    public QdrantSemanticIntentMatcher(
        IVectorMemory vectorMemory,
        IEmbeddingGenerator embeddingGenerator,
        ILogger<QdrantSemanticIntentMatcher> logger)
    {
        _vectorMemory = vectorMemory ?? throw new ArgumentNullException(nameof(vectorMemory));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IntentMatch>> FindCandidatesAsync(
        string message,
        string tenantId,
        string? channel = null,
        int topK = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message cannot be null or whitespace.", nameof(message));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId cannot be null or whitespace.", nameof(tenantId));
        }

        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), "TopK must be greater than 0.");
        }

        _logger.LogInformation(
            "Finding intent candidates for message in tenant {TenantId}, channel {Channel}, topK {TopK}",
            tenantId, channel ?? "all", topK);

        try
        {
            // Step 1: Generate embedding for the input message
            var embedding = await GenerateMessageEmbeddingAsync(message, ct);

            // Step 2: Search in tenant-specific collection with filters
            var agentId = $"intent_router_{tenantId}"; // Virtual agent ID for intent routing
            var searchResults = await _vectorMemory.SearchAsync(
                agentId,
                tenantId,
                message, // The search query (will use embedding internally if supported)
                topK,
                DefaultMinScore,
                ct);

            // Step 3: Map vector search results to IntentMatch models
            var matches = MapToIntentMatches(searchResults, tenantId, channel);

            _logger.LogInformation(
                "Found {MatchCount} intent candidates above threshold {MinScore} for tenant {TenantId}",
                matches.Count, DefaultMinScore, tenantId);

            return matches;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to find intent candidates for tenant {TenantId}. Message: {Message}",
                tenantId, message.Substring(0, Math.Min(50, message.Length)));

            // Return empty list on error to avoid breaking the routing pipeline
            // The caller should handle "no match" scenarios
            return Array.Empty<IntentMatch>();
        }
    }

    /// <summary>
    /// Generates an embedding vector for the given message text.
    /// </summary>
    private async Task<IReadOnlyList<float>> GenerateMessageEmbeddingAsync(
        string message,
        CancellationToken ct)
    {
        try
        {
            var embedding = await _embeddingGenerator.GenerateAsync(message, ct);

            _logger.LogDebug(
                "Generated embedding for message with dimension {Dimension} using model {Model}",
                embedding.Count, _embeddingGenerator.ModelName);

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding for message");
            throw; // Re-throw to fail fast on embedding errors
        }
    }

    /// <summary>
    /// Maps vector memory search results to IntentMatch models.
    /// Filters by channel if specified and deserializes intent rules from metadata.
    /// </summary>
    private IReadOnlyList<IntentMatch> MapToIntentMatches(
        IReadOnlyList<VectorMemoryResult> searchResults,
        string tenantId,
        string? channel)
    {
        var matches = new List<IntentMatch>();

        foreach (var result in searchResults)
        {
            try
            {
                // Extract intent metadata from vector search result
                if (!result.Metadata.TryGetValue("intent_key", out var intentKey))
                {
                    _logger.LogWarning("Vector result missing intent_key metadata. Skipping.");
                    continue;
                }

                if (!result.Metadata.TryGetValue("rule_json", out var ruleJson))
                {
                    _logger.LogWarning(
                        "Vector result for intent {IntentKey} missing rule_json metadata. Skipping.",
                        intentKey);
                    continue;
                }

                // Deserialize the full IntentRoutingRule from metadata
                var rule = DeserializeRule(ruleJson);

                if (rule == null)
                {
                    _logger.LogWarning(
                        "Failed to deserialize rule for intent {IntentKey}. Skipping.",
                        intentKey);
                    continue;
                }

                // Apply channel filter if specified
                if (!string.IsNullOrEmpty(channel) &&
                    !string.IsNullOrEmpty(rule.Channel) &&
                    !string.Equals(rule.Channel, channel, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "Intent {IntentKey} filtered out by channel mismatch. Rule channel: {RuleChannel}, Requested: {RequestedChannel}",
                        intentKey, rule.Channel, channel);
                    continue;
                }

                // Verify rule is enabled (should already be filtered by indexing, but double-check)
                if (!rule.Enabled)
                {
                    _logger.LogWarning(
                        "Intent {IntentKey} is disabled but still in vector index. Skipping.",
                        intentKey);
                    continue;
                }

                // Verify tenant matches (critical for multi-tenancy security)
                if (!string.IsNullOrWhiteSpace(rule.TenantId) &&
                    !string.Equals(rule.TenantId, tenantId, StringComparison.Ordinal))
                {
                    _logger.LogError(
                        "SECURITY VIOLATION: Intent {IntentKey} has mismatched tenant. Expected {ExpectedTenant}, Got {ActualTenant}",
                        intentKey, tenantId, rule.TenantId);
                    continue;
                }

                // Create the match result
                var match = new IntentMatch
                {
                    IntentKey = intentKey,
                    SimilarityScore = result.Score,
                    MatchedVia = "semantic", // This implementation uses semantic search
                    Rule = rule
                };

                matches.Add(match);

                _logger.LogDebug(
                    "Mapped intent match: {IntentKey} with score {Score:F3}",
                    intentKey, result.Score);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error mapping vector result to IntentMatch. Skipping result.");
            }
        }

        // Sort by similarity score descending (highest confidence first)
        return matches.OrderByDescending(m => m.SimilarityScore).ToList();
    }

    /// <summary>
    /// Deserializes an IntentRoutingRule from JSON string.
    /// </summary>
    private IntentRoutingRule? DeserializeRule(string ruleJson)
    {
        try
        {
            var rule = JsonSerializer.Deserialize<IntentRoutingRule>(
                ruleJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            return rule;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "rule_json is not IntentRoutingRule, trying IntentDefinition fallback.");
        }

        try
        {
            var intent = JsonSerializer.Deserialize<IntentDefinition>(
                ruleJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (intent == null)
            {
                return null;
            }

            // Fallback mapping for catalog-only intents indexed as IntentDefinition.
            // These entries are classification-only until they are synced into tenant routing rules.
            return new IntentRoutingRule
            {
                Id = $"catalog-{intent.Key}",
                TenantId = string.Empty,
                IntentKey = intent.Key,
                IntentDescription = intent.Description,
                ExamplePhrases = intent.Examples,
                SourceAgentId = "router",
                TargetAgentId = string.Empty,
                WorkflowDefinitionId = intent.SuggestedWorkflow,
                WorkflowName = intent.SuggestedWorkflow,
                Priority = intent.Priority,
                Enabled = true,
                Channel = null,
                ConditionsJson = null,
                HandoffPolicyJson = null,
                Version = intent.Version,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize rule_json as IntentRoutingRule or IntentDefinition");
            return null;
        }
    }
}
