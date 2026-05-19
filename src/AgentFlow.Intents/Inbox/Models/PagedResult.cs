namespace AgentFlow.Intents.Inbox.Models;

/// <summary>
/// Generic paginated result container.
/// Contains a page of items plus metadata for pagination UI.
/// </summary>
/// <typeparam name="T">Type of items in the result set.</typeparam>
/// <remarks>
/// <para><b>Page Numbers:</b> 1-indexed (Page 1 is the first page).</para>
/// <para><b>Frontend Integration:</b> HasNextPage/HasPreviousPage simplify UI logic.</para>
/// <para><b>Performance:</b> Total count may be expensive for large collections. Consider approximation for huge datasets.</para>
/// </remarks>
public sealed record PagedResult<T>
{
    /// <summary>
    /// The items in the current page.
    /// Empty list if no results found.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// Total number of items across all pages (before pagination).
    /// Used to calculate TotalPages and display "Showing X of Y" messages.
    /// </summary>
    public required int Total { get; init; }

    /// <summary>
    /// Current page number (1-indexed).
    /// </summary>
    public required int Page { get; init; }

    /// <summary>
    /// Number of items per page (same as requested PageSize).
    /// </summary>
    public required int PageSize { get; init; }

    /// <summary>
    /// Total number of pages available.
    /// Calculated as: Ceiling(Total / PageSize)
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)Total / PageSize);

    /// <summary>
    /// True if there is a next page available.
    /// Used to enable/disable "Next" button in UI.
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// True if there is a previous page available.
    /// Used to enable/disable "Previous" button in UI.
    /// </summary>
    public bool HasPreviousPage => Page > 1;
}
