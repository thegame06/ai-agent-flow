using AgentFlow.Intents.Classification.Models;

namespace AgentFlow.Intents.Classification;

/// <summary>
/// Keyword-based intent matcher that uses deterministic rules (exact match, n-gram overlap, regex)
/// for intent classification. Complements semantic matching with fast, rule-based scoring.
/// </summary>
/// <remarks>
/// <para><b>Matching Strategy:</b></para>
/// <list type="bullet">
///   <item><description>Exact Match: Full phrase appears in message (weight: 0.3)</description></item>
///   <item><description>N-gram Overlap: Token intersection ratio (weight: 0.5)</description></item>
///   <item><description>Synonym Match: Known synonyms detected (weight: 0.2)</description></item>
/// </list>
/// <para><b>Performance Target:</b> &lt; 100ms for 100 rules.</para>
/// <para><b>Use Cases:</b> Deterministic routing, high-precision keywords, compliance-required paths.</para>
/// </remarks>
public interface IKeywordIntentMatcher
{
    /// <summary>
    /// Finds intent candidates by matching keywords and phrases deterministically.
    /// </summary>
    /// <param name="message">The user message to classify (e.g., "Quiero solicitar un préstamo personal").</param>
    /// <param name="tenantId">The tenant context for rule isolation.</param>
    /// <param name="channel">Optional channel filter (e.g., "whatsapp", "web"). Null = all channels.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A list of matched intents ordered by score descending. Only returns candidates with score &gt; 0.
    /// Each match includes the intent key, keyword score, and the matched rule.
    /// </returns>
    Task<IReadOnlyList<IntentMatch>> FindCandidatesAsync(
        string message,
        string tenantId,
        string? channel = null,
        CancellationToken ct = default);
}
