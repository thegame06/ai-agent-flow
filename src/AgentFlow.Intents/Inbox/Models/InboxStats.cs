using AgentFlow.Intents.Classification.Models;

namespace AgentFlow.Intents.Inbox.Models;

/// <summary>
/// Aggregated statistics for the conversation inbox.
/// Used for dashboard metrics and operational monitoring.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Stats are computed via MongoDB aggregation pipelines.</para>
/// <para><b>Caching:</b> Consider caching stats for high-traffic tenants (TTL ~30s).</para>
/// <para><b>Real-time Updates:</b> Stats are eventually consistent. May lag by a few seconds.</para>
/// </remarks>
public sealed record InboxStats
{
    /// <summary>
    /// Total number of conversations in the inbox (all states).
    /// Used for capacity planning and load monitoring.
    /// </summary>
    public required int TotalConversations { get; init; }

    /// <summary>
    /// Count of conversations in AwaitingClassification state.
    /// High count indicates classification bottleneck.
    /// </summary>
    public required int AwaitingClassification { get; init; }

    /// <summary>
    /// Count of conversations requiring human review.
    /// Includes LowConfidence and PendingHumanReview states.
    /// Critical SLA metric.
    /// </summary>
    public required int RequiresReview { get; init; }

    /// <summary>
    /// Count of conversations resolved today (UTC).
    /// Used for daily resolution metrics and team performance tracking.
    /// </summary>
    public required int ResolvedToday { get; init; }

    /// <summary>
    /// Count of conversations currently in progress.
    /// Indicates active agent workload.
    /// </summary>
    public required int InProgress { get; init; }

    /// <summary>
    /// Count of conversations with no intent match.
    /// High count indicates missing intents in catalog.
    /// </summary>
    public required int NoMatch { get; init; }

    /// <summary>
    /// Breakdown of conversations by state.
    /// Key: ConversationState enum value.
    /// Value: Count of conversations in that state.
    /// </summary>
    public required Dictionary<ConversationState, int> ByState { get; init; }

    /// <summary>
    /// Breakdown of conversations by confidence level.
    /// Key: ConfidenceLevel enum value.
    /// Value: Count of conversations at that confidence level.
    /// Used to monitor classification quality.
    /// </summary>
    public required Dictionary<ConfidenceLevel, int> ByConfidence { get; init; }
}
