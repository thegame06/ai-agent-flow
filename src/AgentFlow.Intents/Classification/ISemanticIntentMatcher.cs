using AgentFlow.Intents.Classification.Models;

namespace AgentFlow.Intents.Classification;

/// <summary>
/// Provides semantic intent matching using vector embeddings and similarity search.
/// This is the core component for AI-powered intent routing in regulated environments.
/// </summary>
public interface ISemanticIntentMatcher
{
    /// <summary>
    /// Finds candidate intents that semantically match the given message.
    /// Uses vector similarity search against indexed intent definitions.
    /// </summary>
    /// <param name="message">The user message to classify.</param>
    /// <param name="tenantId">The tenant ID for multi-tenancy filtering.</param>
    /// <param name="channel">Optional channel filter (e.g., "whatsapp", "web"). Null means all channels.</param>
    /// <param name="topK">Maximum number of candidates to return, ordered by similarity score.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of intent matches ranked by similarity score (highest first).</returns>
    Task<IReadOnlyList<IntentMatch>> FindCandidatesAsync(
        string message,
        string tenantId,
        string? channel = null,
        int topK = 5,
        CancellationToken ct = default);
}
