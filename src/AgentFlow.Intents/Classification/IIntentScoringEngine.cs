using AgentFlow.Intents.Classification.Models;

namespace AgentFlow.Intents.Classification;

/// <summary>
/// The Hybrid Scoring Engine that combines semantic matching, keyword matching, and priority scoring
/// to produce the final intent classification with confidence levels.
/// This is the core component for enterprise-grade intent routing in regulated environments.
/// </summary>
/// <remarks>
/// <para><b>Hybrid Scoring Formula:</b></para>
/// <code>
/// FinalScore = (0.7 × SemanticScore) + (0.2 × KeywordScore) + (0.1 × PriorityScore)
/// </code>
/// <para><b>Execution Flow:</b></para>
/// <list type="number">
///   <item><description>Get top 10 candidates from Semantic Matcher (vector similarity)</description></item>
///   <item><description>Get candidates from Keyword Matcher (exact/n-gram)</description></item>
///   <item><description>Merge and combine scores by IntentKey (one intent may appear in both)</description></item>
///   <item><description>Normalize priority (1000 → 1.0, 500 → 0.5)</description></item>
///   <item><description>Calculate final hybrid score</description></item>
///   <item><description>Determine confidence level (High/Medium/Low/NoMatch)</description></item>
///   <item><description>Generate audit-ready explanation JSON</description></item>
/// </list>
/// <para><b>Performance Target:</b> &lt; 500ms end-to-end</para>
/// <para><b>Audit Requirement:</b> All decisions must be traceable via ExplanationJson.</para>
/// </remarks>
public interface IIntentScoringEngine
{
    /// <summary>
    /// Classifies a user message by combining semantic, keyword, and priority scoring.
    /// Returns the best match, all candidates, confidence level, and full audit trail.
    /// </summary>
    /// <param name="message">The user message to classify (e.g., "Quiero solicitar un préstamo").</param>
    /// <param name="tenantId">The tenant context for multi-tenancy isolation.</param>
    /// <param name="channel">Optional channel filter (e.g., "whatsapp", "web"). Null = all channels.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A classification result containing:
    /// - Best matching intent (or null if NoMatch)
    /// - All candidate intents with scores
    /// - Confidence level (High/Medium/Low/NoMatch)
    /// - Human review flag
    /// - Full JSON explanation for audit
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when message is null or empty.</exception>
    Task<IntentClassificationResult> ClassifyAsync(
        string message,
        string tenantId,
        string? channel = null,
        CancellationToken ct = default);
}
