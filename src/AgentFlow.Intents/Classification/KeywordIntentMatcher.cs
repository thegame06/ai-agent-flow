using System.Text.RegularExpressions;
using AgentFlow.Intents.Classification.Models;
using AgentFlow.Security;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Intents.Classification;

/// <summary>
/// Keyword-based intent matcher using deterministic scoring algorithms.
/// Provides fast, rule-based classification complementary to semantic matching.
/// </summary>
public sealed class KeywordIntentMatcher : IKeywordIntentMatcher
{
    private readonly IIntentRoutingStore _routingStore;
    private readonly ILogger<KeywordIntentMatcher> _logger;

    // Common stopwords to exclude from tokenization (Spanish + English)
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Spanish
        "el", "la", "de", "que", "en", "un", "una", "por", "para", "con", "los", "las",
        "del", "al", "es", "su", "me", "te", "se", "le", "lo", "como", "más",
        // English
        "the", "is", "at", "which", "on", "a", "an", "and", "or", "to", "for", "of", "in"
    };

    public KeywordIntentMatcher(
        IIntentRoutingStore routingStore,
        ILogger<KeywordIntentMatcher> logger)
    {
        _routingStore = routingStore ?? throw new ArgumentNullException(nameof(routingStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IntentMatch>> FindCandidatesAsync(
        string message,
        string tenantId,
        string? channel = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("KeywordIntentMatcher received empty message. Returning no candidates.");
            return Array.Empty<IntentMatch>();
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId cannot be null or whitespace.", nameof(tenantId));
        }

        _logger.LogDebug(
            "Finding keyword-based intent candidates. TenantId={TenantId}, Channel={Channel}, MessageLength={Length}",
            tenantId, channel ?? "all", message.Length);

        // Fetch all enabled rules for this tenant/channel
        var rules = await _routingStore.GetRulesByChannelAsync(tenantId, channel ?? string.Empty, ct);

        if (rules.Count == 0)
        {
            _logger.LogWarning(
                "No intent routing rules found for TenantId={TenantId}, Channel={Channel}",
                tenantId, channel);
            return Array.Empty<IntentMatch>();
        }

        _logger.LogDebug("Processing {RuleCount} intent rules for keyword matching", rules.Count);

        // Calculate scores for each rule
        var candidates = new List<IntentMatch>(capacity: rules.Count);

        foreach (var rule in rules)
        {
            var score = CalculateKeywordScore(message, rule);

            if (score > 0)
            {
                candidates.Add(new IntentMatch
                {
                    IntentKey = rule.IntentKey,
                    SimilarityScore = score,
                    MatchedVia = "keyword",
                    Rule = rule
                });

                _logger.LogDebug(
                    "Keyword match found: Intent={IntentKey}, Score={Score:F3}",
                    rule.IntentKey, score);
            }
        }

        // Sort by score descending
        var sortedCandidates = candidates
            .OrderByDescending(c => c.SimilarityScore)
            .ToList();

        _logger.LogInformation(
            "Keyword matching complete. Found {CandidateCount} candidates from {RuleCount} rules. TopIntent={TopIntent}, TopScore={TopScore:F3}",
            sortedCandidates.Count,
            rules.Count,
            sortedCandidates.FirstOrDefault()?.IntentKey ?? "none",
            sortedCandidates.FirstOrDefault()?.SimilarityScore ?? 0f);

        return sortedCandidates;
    }

    /// <summary>
    /// Calculates a keyword-based score for an intent rule against a message.
    /// </summary>
    /// <param name="message">The user message to score.</param>
    /// <param name="rule">The intent rule with example phrases and metadata.</param>
    /// <returns>
    /// A score between 0.0 and 1.0:
    /// <list type="bullet">
    ///   <item><description>0.0 = no keyword match</description></item>
    ///   <item><description>0.3 = exact phrase match</description></item>
    ///   <item><description>0.5 = strong n-gram overlap</description></item>
    ///   <item><description>1.0 = perfect keyword match (exact + overlap + synonyms)</description></item>
    /// </list>
    /// </returns>
    private float CalculateKeywordScore(string message, IntentRoutingRule rule)
    {
        if (rule.ExamplePhrases == null || rule.ExamplePhrases.Count == 0)
        {
            // No example phrases to match against
            return 0f;
        }

        float score = 0f;

        // Normalize message for case-insensitive matching
        var normalizedMessage = message.ToLowerInvariant();

        // 1. EXACT MATCH (weight: 0.3)
        // Check if message contains any complete example phrase
        bool hasExactMatch = rule.ExamplePhrases.Any(phrase =>
            !string.IsNullOrWhiteSpace(phrase) &&
            normalizedMessage.Contains(phrase.ToLowerInvariant()));

        if (hasExactMatch)
        {
            score += 0.3f;
        }

        // 2. N-GRAM OVERLAP (weight: 0.5)
        // Tokenize message and all example phrases
        var messageTokens = Tokenize(normalizedMessage);

        if (messageTokens.Count > 0)
        {
            var allExampleTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var phrase in rule.ExamplePhrases)
            {
                if (!string.IsNullOrWhiteSpace(phrase))
                {
                    var phraseTokens = Tokenize(phrase.ToLowerInvariant());
                    foreach (var token in phraseTokens)
                    {
                        allExampleTokens.Add(token);
                    }
                }
            }

            if (allExampleTokens.Count > 0)
            {
                // Count overlapping tokens
                int overlapCount = messageTokens.Count(token => allExampleTokens.Contains(token));

                // Calculate overlap ratio
                int maxTokens = Math.Max(messageTokens.Count, allExampleTokens.Count);
                float overlapRatio = (float)overlapCount / maxTokens;

                // Apply weight
                score += overlapRatio * 0.5f;
            }
        }

        // 3. SYNONYM MATCH (weight: 0.2)
        // Note: For now, we check if the IntentDescription contains synonyms.
        // In production, you'd maintain a separate synonym dictionary.
        // This is a simplified implementation that checks if the intent description
        // has overlapping significant tokens with the message.
        if (!string.IsNullOrWhiteSpace(rule.IntentDescription))
        {
            var descriptionTokens = Tokenize(rule.IntentDescription.ToLowerInvariant());
            var messageTokensSet = new HashSet<string>(messageTokens, StringComparer.OrdinalIgnoreCase);

            bool hasSynonymMatch = descriptionTokens.Any(token => messageTokensSet.Contains(token));

            if (hasSynonymMatch)
            {
                score += 0.2f;
            }
        }

        // Ensure score doesn't exceed 1.0
        return Math.Min(score, 1.0f);
    }

    /// <summary>
    /// Tokenizes a text string into normalized, meaningful tokens.
    /// </summary>
    /// <param name="text">The text to tokenize.</param>
    /// <returns>A list of tokens (lowercase, no punctuation, no stopwords, length >= 3).</returns>
    private static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        // Split by whitespace and punctuation
        // Keep only alphanumeric characters
        var tokens = Regex.Split(text, @"[^\w]+")
            .Where(token =>
                !string.IsNullOrWhiteSpace(token) &&
                token.Length >= 3 &&                      // Ignore very short tokens
                !Stopwords.Contains(token))               // Remove stopwords
            .Select(token => token.ToLowerInvariant())
            .ToList();

        return tokens;
    }
}
