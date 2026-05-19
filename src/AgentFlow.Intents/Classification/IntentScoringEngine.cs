using System.Text.Json;
using AgentFlow.Intents.Classification.Models;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Intents.Classification;

/// <summary>
/// Enterprise-grade hybrid scoring engine for intent classification.
/// Combines semantic matching (vector similarity), keyword matching (deterministic rules),
/// and priority scoring to produce final intent classification with confidence levels.
/// </summary>
/// <remarks>
/// <para><b>Scoring Weights:</b></para>
/// <list type="bullet">
///   <item><description>Semantic: 70% (AI-powered similarity)</description></item>
///   <item><description>Keyword: 20% (deterministic rules)</description></item>
///   <item><description>Priority: 10% (business priority)</description></item>
/// </list>
/// <para><b>Design Principles:</b></para>
/// <list type="bullet">
///   <item><description><b>Determinism</b>: Same input always produces same output</description></item>
///   <item><description><b>Auditability</b>: Full decision traceability via ExplanationJson</description></item>
///   <item><description><b>Safety</b>: Low confidence requires human review</description></item>
///   <item><description><b>Performance</b>: Target &lt; 500ms end-to-end</description></item>
/// </list>
/// </remarks>
public sealed class IntentScoringEngine : IIntentScoringEngine
{
    private readonly ISemanticIntentMatcher _semanticMatcher;
    private readonly IKeywordIntentMatcher _keywordMatcher;
    private readonly ILogger<IntentScoringEngine> _logger;

    // Scoring weights (must sum to 1.0)
    private const float SemanticWeight = 0.7f;
    private const float KeywordWeight = 0.2f;
    private const float PriorityWeight = 0.1f;

    // Confidence thresholds
    private const float HighConfidenceThreshold = 0.90f;
    private const float MediumConfidenceThreshold = 0.75f;
    private const float LowConfidenceThreshold = 0.50f;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntentScoringEngine"/> class.
    /// </summary>
    /// <param name="semanticMatcher">The semantic intent matcher for vector similarity search.</param>
    /// <param name="keywordMatcher">The keyword intent matcher for deterministic rule matching.</param>
    /// <param name="logger">The logger for audit and debugging.</param>
    public IntentScoringEngine(
        ISemanticIntentMatcher semanticMatcher,
        IKeywordIntentMatcher keywordMatcher,
        ILogger<IntentScoringEngine> logger)
    {
        _semanticMatcher = semanticMatcher ?? throw new ArgumentNullException(nameof(semanticMatcher));
        _keywordMatcher = keywordMatcher ?? throw new ArgumentNullException(nameof(keywordMatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IntentClassificationResult> ClassifyAsync(
        string message,
        string tenantId,
        string? channel = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message cannot be null or empty.", nameof(message));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId cannot be null or empty.", nameof(tenantId));
        }

        _logger.LogInformation(
            "Starting intent classification for message: '{Message}' (tenant: {TenantId}, channel: {Channel})",
            message, tenantId, channel ?? "all");

        // 1. Get candidates from both matchers in parallel
        var semanticTask = _semanticMatcher.FindCandidatesAsync(message, tenantId, channel, topK: 10, ct);
        var keywordTask = _keywordMatcher.FindCandidatesAsync(message, tenantId, channel, ct);

        await Task.WhenAll(semanticTask, keywordTask);

        var semanticCandidates = await semanticTask;
        var keywordCandidates = await keywordTask;

        _logger.LogDebug(
            "Retrieved {SemanticCount} semantic candidates and {KeywordCount} keyword candidates",
            semanticCandidates.Count, keywordCandidates.Count);

        // 2. Combine scores by intent key
        var combinedScores = CombineScores(semanticCandidates, keywordCandidates);

        // 3. Handle no candidates case
        if (combinedScores.Count == 0)
        {
            _logger.LogWarning("No intent candidates found for message: '{Message}'", message);
            
            return new IntentClassificationResult
            {
                Message = message,
                BestMatch = null,
                AllCandidates = Array.Empty<IntentMatch>(),
                BestScore = 0.0f,
                Confidence = ConfidenceLevel.NoMatch,
                RequiresHumanReview = true,
                ExplanationJson = BuildExplanation(message, null, Array.Empty<IntentMatch>(), 
                    new Dictionary<string, string>())
            };
        }

        // 4. Sort by final score descending
        var sortedCandidates = combinedScores
            .OrderByDescending(x => x.FinalScore)
            .ToList();

        var bestCandidate = sortedCandidates.First();
        var bestScore = bestCandidate.FinalScore;
        var confidence = DetermineConfidence(bestScore);
        var requiresReview = confidence <= ConfidenceLevel.Low;

        _logger.LogInformation(
            "Best match: {IntentKey} with score {Score:F3} (confidence: {Confidence}, requires_review: {RequiresReview})",
            bestCandidate.Match.IntentKey, bestScore, confidence, requiresReview);

        // 5. Build audit trail
        var scoreBreakdown = new Dictionary<string, string>
        {
            ["semantic_score"] = bestCandidate.SemanticScore.ToString("F3"),
            ["keyword_score"] = bestCandidate.KeywordScore.ToString("F3"),
            ["priority_score"] = bestCandidate.PriorityScore.ToString("F3"),
            ["final_score"] = bestScore.ToString("F3"),
            ["matched_via"] = string.Join(", ", bestCandidate.MatchedVia)
        };

        var explanationJson = BuildExplanation(
            message,
            bestCandidate,
            sortedCandidates.Select(c => c.Match).ToList(),
            scoreBreakdown);

        return new IntentClassificationResult
        {
            Message = message,
            BestMatch = bestCandidate.Match,
            AllCandidates = sortedCandidates.Select(c => c.Match).ToList(),
            BestScore = bestScore,
            Confidence = confidence,
            RequiresHumanReview = requiresReview,
            ExplanationJson = explanationJson
        };
    }

    /// <summary>
    /// Combines scores from semantic and keyword matchers into a single ranked list.
    /// If an intent appears in both matchers, its scores are merged.
    /// </summary>
    private List<CombinedScore> CombineScores(
        IReadOnlyList<IntentMatch> semanticMatches,
        IReadOnlyList<IntentMatch> keywordMatches)
    {
        var scoreMap = new Dictionary<string, CombinedScore>();

        // Process semantic matches
        foreach (var match in semanticMatches)
        {
            var priorityScore = NormalizePriority(match.Rule.Priority);
            
            scoreMap[match.IntentKey] = new CombinedScore
            {
                Match = match,
                SemanticScore = match.SimilarityScore,
                KeywordScore = 0.0f,
                PriorityScore = priorityScore,
                FinalScore = (SemanticWeight * match.SimilarityScore) + (PriorityWeight * priorityScore),
                MatchedVia = new List<string> { "semantic" }
            };
        }

        // Process keyword matches and merge if already exists
        foreach (var match in keywordMatches)
        {
            var priorityScore = NormalizePriority(match.Rule.Priority);

            if (scoreMap.TryGetValue(match.IntentKey, out var existing))
            {
                // Intent appears in both matchers - merge scores
                existing.KeywordScore = match.SimilarityScore;
                existing.FinalScore = 
                    (SemanticWeight * existing.SemanticScore) +
                    (KeywordWeight * match.SimilarityScore) +
                    (PriorityWeight * priorityScore);
                existing.MatchedVia.Add("keyword");

                _logger.LogDebug(
                    "Merged scores for intent '{IntentKey}': semantic={Semantic:F3}, keyword={Keyword:F3}, final={Final:F3}",
                    match.IntentKey, existing.SemanticScore, match.SimilarityScore, existing.FinalScore);
            }
            else
            {
                // Intent only appears in keyword matcher
                scoreMap[match.IntentKey] = new CombinedScore
                {
                    Match = match,
                    SemanticScore = 0.0f,
                    KeywordScore = match.SimilarityScore,
                    PriorityScore = priorityScore,
                    FinalScore = (KeywordWeight * match.SimilarityScore) + (PriorityWeight * priorityScore),
                    MatchedVia = new List<string> { "keyword" }
                };
            }
        }

        return scoreMap.Values.ToList();
    }

    /// <summary>
    /// Normalizes priority value to 0.0-1.0 range.
    /// Priority 1000 → 1.0, Priority 500 → 0.5, Priority 100 → 0.1
    /// </summary>
    private static float NormalizePriority(int priority)
    {
        return Math.Min(priority / 1000f, 1.0f);
    }

    /// <summary>
    /// Determines confidence level based on final score.
    /// </summary>
    private static ConfidenceLevel DetermineConfidence(float score)
    {
        return score switch
        {
            >= HighConfidenceThreshold => ConfidenceLevel.High,
            >= MediumConfidenceThreshold => ConfidenceLevel.Medium,
            >= LowConfidenceThreshold => ConfidenceLevel.Low,
            _ => ConfidenceLevel.NoMatch
        };
    }

    /// <summary>
    /// Builds a JSON explanation of the classification decision for audit and debugging.
    /// </summary>
    private static string BuildExplanation(
        string message,
        CombinedScore? bestMatch,
        IReadOnlyList<IntentMatch> allCandidates,
        Dictionary<string, string> scoreBreakdown)
    {
        var explanation = new
        {
            message,
            best_match = bestMatch != null ? new
            {
                intent_key = bestMatch.Match.IntentKey,
                final_score = bestMatch.FinalScore,
                semantic_score = bestMatch.SemanticScore,
                keyword_score = bestMatch.KeywordScore,
                priority_score = bestMatch.PriorityScore,
                confidence = DetermineConfidence(bestMatch.FinalScore).ToString(),
                matched_via = bestMatch.MatchedVia
            } : null,
            all_candidates = allCandidates.Select(c => new
            {
                intent_key = c.IntentKey,
                score = c.SimilarityScore
            }).ToList(),
            score_breakdown = scoreBreakdown,
            decision = bestMatch != null && DetermineConfidence(bestMatch.FinalScore) >= ConfidenceLevel.Medium 
                ? "auto_route" 
                : "human_review",
            requires_review = bestMatch == null || DetermineConfidence(bestMatch.FinalScore) <= ConfidenceLevel.Low,
            timestamp = DateTimeOffset.UtcNow.ToString("O")
        };

        return JsonSerializer.Serialize(explanation, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Internal record for combining scores from multiple matchers.
    /// </summary>
    private sealed class CombinedScore
    {
        public required IntentMatch Match { get; init; }
        public required float SemanticScore { get; init; }
        public required float KeywordScore { get; set; }
        public required float PriorityScore { get; init; }
        public required float FinalScore { get; set; }
        public required List<string> MatchedVia { get; init; }
    }
}
