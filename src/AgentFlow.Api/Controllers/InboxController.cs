using AgentFlow.Intents.Classification.Models;
using AgentFlow.Intents.Inbox;
using AgentFlow.Intents.Inbox.Models;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

/// <summary>
/// API controller for managing conversations in the inbox.
/// Provides endpoints for retrieving, filtering, and updating conversations that require human review.
/// </summary>
/// <remarks>
/// <para><b>Security:</b> All endpoints require authentication and validate tenant access.</para>
/// <para><b>Base Route:</b> /api/v1/tenants/{tenantId}/inbox</para>
/// <para><b>Use Cases:</b></para>
/// <list type="bullet">
///   <item><description>Frontend inbox UI displays pending conversations</description></item>
///   <item><description>Human agents review and update conversation states</description></item>
///   <item><description>Dashboard widgets show inbox statistics</description></item>
///   <item><description>Operational monitoring tracks conversation flow</description></item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/v1/tenants/{tenantId}/inbox")]
[Authorize]
public sealed class InboxController : ControllerBase
{
    private readonly IConversationInboxService _inboxService;
    private readonly ITenantContextAccessor _tenantContext;

    public InboxController(IConversationInboxService inboxService, ITenantContextAccessor tenantContext)
    {
        _inboxService = inboxService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Retrieves paginated list of conversations matching filter criteria.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (from route).</param>
    /// <param name="state">Filter by conversation state (e.g., "PendingHumanReview", "LowConfidence").</param>
    /// <param name="confidence">Filter by confidence level (e.g., "Low", "Medium", "High").</param>
    /// <param name="channel">Filter by channel (e.g., "whatsapp", "sms").</param>
    /// <param name="requiresReview">Filter by RequiresHumanReview flag.</param>
    /// <param name="page">Page number (1-indexed, default: 1).</param>
    /// <param name="pageSize">Results per page (default: 20, max: 100).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paginated result with conversations and metadata.</returns>
    /// <response code="200">Returns paginated conversations.</response>
    /// <response code="403">User does not have access to this tenant.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InboxConversation>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetConversations(
        [FromRoute] string tenantId,
        [FromQuery] string? state = null,
        [FromQuery] string? confidence = null,
        [FromQuery] string? channel = null,
        [FromQuery] bool? requiresReview = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin)
        {
            return Forbid();
        }

        // Validate and cap page size
        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 100) pageSize = 100;
        if (page < 1) page = 1;

        var filter = new InboxFilter
        {
            State = string.IsNullOrEmpty(state) ? null : Enum.Parse<ConversationState>(state, ignoreCase: true),
            Confidence = string.IsNullOrEmpty(confidence) ? null : Enum.Parse<ConfidenceLevel>(confidence, ignoreCase: true),
            Channel = channel,
            RequiresReview = requiresReview,
            Page = page,
            PageSize = pageSize
        };

        var result = await _inboxService.GetPendingAsync(tenantId, filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves a specific conversation by ID.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (from route).</param>
    /// <param name="conversationId">Unique conversation identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The conversation if found.</returns>
    /// <response code="200">Returns the conversation.</response>
    /// <response code="404">Conversation not found or belongs to different tenant.</response>
    /// <response code="403">User does not have access to this tenant.</response>
    [HttpGet("{conversationId}")]
    [ProducesResponseType(typeof(InboxConversation), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetConversation(
        [FromRoute] string tenantId,
        [FromRoute] string conversationId,
        CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin)
        {
            return Forbid();
        }

        var conversation = await _inboxService.GetByIdAsync(tenantId, conversationId, ct);
        return conversation == null ? NotFound() : Ok(conversation);
    }

    /// <summary>
    /// Updates the state of a conversation.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (from route).</param>
    /// <param name="conversationId">Unique conversation identifier.</param>
    /// <param name="body">Update request containing new state and optional notes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">State updated successfully.</response>
    /// <response code="404">Conversation not found or belongs to different tenant.</response>
    /// <response code="403">User does not have access to this tenant.</response>
    [HttpPut("{conversationId}/state")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateState(
        [FromRoute] string tenantId,
        [FromRoute] string conversationId,
        [FromBody] UpdateStateRequest body,
        CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin)
        {
            return Forbid();
        }

        var success = await _inboxService.UpdateStateAsync(
            tenantId,
            conversationId,
            body.State,
            body.Notes,
            ct);

        return success ? NoContent() : NotFound();
    }

    /// <summary>
    /// Retrieves inbox statistics for the tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (from route).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Inbox statistics including counts and breakdowns.</returns>
    /// <response code="200">Returns inbox statistics.</response>
    /// <response code="403">User does not have access to this tenant.</response>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(InboxStats), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStats([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin)
        {
            return Forbid();
        }

        var stats = await _inboxService.GetStatsAsync(tenantId, ct);
        return Ok(stats);
    }
}

/// <summary>
/// Request body for updating conversation state.
/// </summary>
public sealed record UpdateStateRequest
{
    /// <summary>
    /// New state to transition to.
    /// </summary>
    public required ConversationState State { get; init; }

    /// <summary>
    /// Optional review notes or comments.
    /// Added to the conversation's ReviewNotes field.
    /// </summary>
    public string? Notes { get; init; }
}
