using AgentFlow.Security;

namespace AgentFlow.Intents.Classification.Models;

/// <summary>
/// Represents a matched intent candidate with similarity scoring and metadata.
/// Used by the semantic matcher to return ranked intent candidates.
/// </summary>
public sealed record IntentMatch
{
    /// <summary>
    /// The unique key of the matched intent (e.g., "loan_application", "account_support").
    /// </summary>
    public required string IntentKey { get; init; }

    /// <summary>
    /// Semantic similarity score between the input message and this intent.
    /// Range: 0.0 (no match) to 1.0 (perfect match).
    /// Typical threshold for production: 0.75+
    /// </summary>
    public required float SimilarityScore { get; init; }

    /// <summary>
    /// The matching method used to identify this intent.
    /// Values: "semantic" (vector similarity), "keyword" (exact match), "rule" (pattern-based).
    /// </summary>
    public required string MatchedVia { get; init; }

    /// <summary>
    /// The full intent routing rule that was matched.
    /// Contains target agent, workflow, and routing configuration.
    /// </summary>
    public required IntentRoutingRule Rule { get; init; }
}
