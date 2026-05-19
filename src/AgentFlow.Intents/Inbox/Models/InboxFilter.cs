using AgentFlow.Intents.Classification.Models;

namespace AgentFlow.Intents.Inbox.Models;

/// <summary>
/// Filter criteria for querying conversations in the inbox.
/// Supports pagination and multi-criteria filtering.
/// </summary>
/// <remarks>
/// <para><b>Default Pagination:</b> Page 1, PageSize 20</para>
/// <para><b>Filtering Logic:</b> All non-null filters are combined with AND logic.</para>
/// <para><b>Performance:</b> Ensure MongoDB indexes cover filter combinations.</para>
/// </remarks>
public sealed record InboxFilter
{
    /// <summary>
    /// Filter by conversation state.
    /// Null returns all states.
    /// </summary>
    public ConversationState? State { get; init; }

    /// <summary>
    /// Filter by confidence level.
    /// Commonly used to find Low confidence conversations requiring review.
    /// </summary>
    public ConfidenceLevel? Confidence { get; init; }

    /// <summary>
    /// Filter by channel (e.g., "whatsapp", "sms").
    /// Null returns all channels.
    /// </summary>
    public string? Channel { get; init; }

    /// <summary>
    /// Filter by RequiresHumanReview flag.
    /// True returns only conversations flagged for review.
    /// </summary>
    public bool? RequiresReview { get; init; }

    /// <summary>
    /// Current page number (1-indexed).
    /// Default: 1
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Number of results per page.
    /// Default: 20
    /// </summary>
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// Calculated skip count for MongoDB .Skip() operation.
    /// Formula: (Page - 1) * PageSize
    /// </summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>
    /// Calculated take count for MongoDB .Limit() operation.
    /// Alias for PageSize for clarity.
    /// </summary>
    public int Take => PageSize;
}
