namespace AgentFlow.Intents.Classification.Models;

/// <summary>
/// Confidence level of an intent classification decision.
/// Determines whether the classification requires human review or can be auto-routed.
/// </summary>
/// <remarks>
/// <para><b>Thresholds:</b></para>
/// <list type="bullet">
///   <item><description><b>High</b>: Score ≥ 0.90 — Auto-route with confidence</description></item>
///   <item><description><b>Medium</b>: Score 0.75-0.89 — Auto-route, log for monitoring</description></item>
///   <item><description><b>Low</b>: Score 0.50-0.74 — Requires human review before routing</description></item>
///   <item><description><b>NoMatch</b>: Score &lt; 0.50 — Fallback to default handler</description></item>
/// </list>
/// <para><b>Audit Requirement:</b> All Low confidence decisions must be logged for compliance.</para>
/// </remarks>
public enum ConfidenceLevel
{
    /// <summary>
    /// No viable intent match found. Score &lt; 0.50.
    /// Action: Route to fallback handler or human agent.
    /// </summary>
    NoMatch = 0,

    /// <summary>
    /// Low confidence classification. Score 0.50 - 0.74.
    /// Action: Requires human review before routing.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Medium confidence classification. Score 0.75 - 0.89.
    /// Action: Auto-route but log for quality monitoring.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// High confidence classification. Score ≥ 0.90.
    /// Action: Auto-route with full confidence.
    /// </summary>
    High = 3
}
