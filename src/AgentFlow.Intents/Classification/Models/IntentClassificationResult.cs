namespace AgentFlow.Intents.Classification.Models;

/// <summary>
/// The final result of intent classification after hybrid scoring (semantic + keyword + priority).
/// Contains the best match, all candidates, confidence level, and full decision traceability.
/// </summary>
/// <remarks>
/// <para><b>Scoring Formula:</b></para>
/// <code>
/// FinalScore = (0.7 × SemanticScore) + (0.2 × KeywordScore) + (0.1 × PriorityScore)
/// </code>
/// <para><b>Decision Logic:</b></para>
/// <list type="bullet">
///   <item><description>High confidence (≥0.90): Auto-route immediately</description></item>
///   <item><description>Medium confidence (0.75-0.89): Auto-route with monitoring</description></item>
///   <item><description>Low confidence (0.50-0.74): Human review required</description></item>
///   <item><description>No match (&lt;0.50): Fallback to default handler</description></item>
/// </list>
/// <para><b>Audit Trail:</b> The ExplanationJson field contains full breakdown for compliance.</para>
/// </remarks>
public sealed record IntentClassificationResult
{
    /// <summary>
    /// The original user message that was classified.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The best matching intent after hybrid scoring.
    /// Null if no viable match was found (NoMatch confidence).
    /// </summary>
    public IntentMatch? BestMatch { get; init; }

    /// <summary>
    /// All candidate intents that were considered, ordered by final score descending.
    /// Includes matches from both semantic and keyword matchers.
    /// </summary>
    public required IReadOnlyList<IntentMatch> AllCandidates { get; init; }

    /// <summary>
    /// The final combined score of the best match (0.0 to 1.0).
    /// Formula: 0.7×semantic + 0.2×keyword + 0.1×priority
    /// </summary>
    public required float BestScore { get; init; }

    /// <summary>
    /// The confidence level of the classification decision.
    /// Determines whether human review is required.
    /// </summary>
    public required ConfidenceLevel Confidence { get; init; }

    /// <summary>
    /// Whether this classification requires human review before routing.
    /// True when Confidence is Low or NoMatch.
    /// </summary>
    public required bool RequiresHumanReview { get; init; }

    /// <summary>
    /// Full JSON explanation of the decision for audit and debugging.
    /// Contains score breakdown, matched methods, and decision reasoning.
    /// Must be valid JSON for parsing by observability tools.
    /// </summary>
    /// <example>
    /// <code>
    /// {
    ///   "message": "Quiero solicitar un préstamo",
    ///   "best_match": {
    ///     "intent_key": "loan_application",
    ///     "final_score": 0.92,
    ///     "semantic_score": 0.95,
    ///     "keyword_score": 0.80,
    ///     "priority_score": 0.50,
    ///     "confidence": "High"
    ///   },
    ///   "all_candidates": [
    ///     { "intent_key": "loan_application", "score": 0.92 },
    ///     { "intent_key": "product_inquiry", "score": 0.65 }
    ///   ],
    ///   "matched_via": ["semantic", "keyword"],
    ///   "decision": "auto_route",
    ///   "requires_review": false
    /// }
    /// </code>
    /// </example>
    public required string ExplanationJson { get; init; }
}
