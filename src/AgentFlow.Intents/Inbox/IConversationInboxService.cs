using AgentFlow.Intents.Inbox.Models;

namespace AgentFlow.Intents.Inbox;

/// <summary>
/// Service for managing conversations in the inbox.
/// Provides CRUD operations, state management, and analytics for conversations awaiting classification or review.
/// </summary>
/// <remarks>
/// <para><b>Tenant Isolation:</b> All operations are scoped to a specific TenantId.</para>
/// <para><b>Persistence:</b> Backed by MongoDB for scalability and query flexibility.</para>
/// <para><b>Use Cases:</b></para>
/// <list type="bullet">
///   <item><description>Store conversations with low confidence for human review</description></item>
///   <item><description>Track conversations with no intent match</description></item>
///   <item><description>Monitor workflow execution status</description></item>
///   <item><description>Provide inbox UI data and statistics</description></item>
/// </list>
/// <para><b>Integration Points:</b></para>
/// <list type="bullet">
///   <item><description><b>Routing Orchestrator:</b> Calls CreateOrUpdateAsync when confidence is Low or NoMatch</description></item>
///   <item><description><b>Frontend Inbox:</b> Calls GetPendingAsync to display conversations</description></item>
///   <item><description><b>Workflow Engine:</b> Calls UpdateStateAsync when workflow starts/completes</description></item>
///   <item><description><b>Human Agents:</b> Calls UpdateStateAsync to mark reviewed/resolved</description></item>
/// </list>
/// </remarks>
public interface IConversationInboxService
{
    /// <summary>
    /// Creates a new conversation or updates an existing one in the inbox.
    /// Uses upsert semantics: creates if not exists, updates if exists.
    /// </summary>
    /// <param name="conversation">The conversation to create or update.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created or updated conversation.</returns>
    /// <remarks>
    /// <para><b>Idempotency:</b> Safe to call multiple times with same ID.</para>
    /// <para><b>Auto-Updates:</b> UpdatedAt timestamp is automatically set to current time.</para>
    /// <para><b>Common Scenarios:</b></para>
    /// <list type="bullet">
    ///   <item><description>Routing Orchestrator returns Queue → Store with State=AwaitingClassification</description></item>
    ///   <item><description>Classification returns Low confidence → Store with State=LowConfidence, RequiresReview=true</description></item>
    ///   <item><description>No intent match → Store with State=NoMatch, RequiresReview=true</description></item>
    /// </list>
    /// </remarks>
    Task<InboxConversation> CreateOrUpdateAsync(
        InboxConversation conversation,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves paginated list of conversations matching the filter criteria.
    /// Results are sorted by UpdatedAt descending (most recent first).
    /// </summary>
    /// <param name="tenantId">Tenant identifier for isolation.</param>
    /// <param name="filter">Filter criteria including state, confidence, channel, and pagination.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paginated result with conversations and pagination metadata.</returns>
    /// <remarks>
    /// <para><b>Default Sorting:</b> UpdatedAt descending (newest first).</para>
    /// <para><b>Performance:</b> Uses MongoDB compound index on (TenantId, State, UpdatedAt).</para>
    /// <para><b>Common Filters:</b></para>
    /// <list type="bullet">
    ///   <item><description>State=PendingHumanReview → Show conversations waiting for review</description></item>
    ///   <item><description>Confidence=Low → Show low confidence classifications</description></item>
    ///   <item><description>RequiresReview=true → Show all conversations needing attention</description></item>
    ///   <item><description>Channel="whatsapp" → Show WhatsApp conversations only</description></item>
    /// </list>
    /// </remarks>
    Task<PagedResult<InboxConversation>> GetPendingAsync(
        string tenantId,
        InboxFilter filter,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific conversation by ID.
    /// Returns null if conversation not found or belongs to different tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier for isolation.</param>
    /// <param name="conversationId">Unique conversation identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The conversation if found, null otherwise.</returns>
    /// <remarks>
    /// <para><b>Security:</b> Always filters by TenantId to prevent cross-tenant access.</para>
    /// <para><b>Use Case:</b> Display conversation details in inbox UI.</para>
    /// </remarks>
    Task<InboxConversation?> GetByIdAsync(
        string tenantId,
        string conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the state of a conversation.
    /// Optionally adds review notes and sets resolution metadata.
    /// </summary>
    /// <param name="tenantId">Tenant identifier for isolation.</param>
    /// <param name="conversationId">Unique conversation identifier.</param>
    /// <param name="newState">New state to transition to.</param>
    /// <param name="notes">Optional review notes or comments.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if conversation was found and updated, false if not found.</returns>
    /// <remarks>
    /// <para><b>Auto-Updates:</b></para>
    /// <list type="bullet">
    ///   <item><description>UpdatedAt is always set to current time</description></item>
    ///   <item><description>ResolvedAt is set when transitioning to Resolved state</description></item>
    ///   <item><description>ReviewNotes is updated if notes parameter is provided</description></item>
    /// </list>
    /// <para><b>State Transition Examples:</b></para>
    /// <list type="bullet">
    ///   <item><description>AwaitingClassification → Classified (after intent detected)</description></item>
    ///   <item><description>LowConfidence → InProgress (human approved, workflow started)</description></item>
    ///   <item><description>PendingHumanReview → Resolved (human responded directly)</description></item>
    ///   <item><description>NoMatch → Escalated (escalated to supervisor)</description></item>
    /// </list>
    /// </remarks>
    Task<bool> UpdateStateAsync(
        string tenantId,
        string conversationId,
        ConversationState newState,
        string? notes = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves aggregated statistics for the inbox.
    /// Includes counts by state, confidence level, and daily resolution metrics.
    /// </summary>
    /// <param name="tenantId">Tenant identifier for isolation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Inbox statistics including totals, breakdowns, and daily metrics.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Uses MongoDB aggregation pipelines for efficient computation.</para>
    /// <para><b>Caching Recommendation:</b> Consider caching stats with short TTL (~30s) for high-traffic tenants.</para>
    /// <para><b>Use Cases:</b></para>
    /// <list type="bullet">
    ///   <item><description>Dashboard widgets showing inbox health</description></item>
    ///   <item><description>Team workload monitoring</description></item>
    ///   <item><description>SLA compliance tracking (RequiresReview count)</description></item>
    ///   <item><description>Intent catalog quality metrics (NoMatch count)</description></item>
    /// </list>
    /// </remarks>
    Task<InboxStats> GetStatsAsync(
        string tenantId,
        CancellationToken ct = default);
}
