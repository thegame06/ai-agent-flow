using AgentFlow.Api.Commerce;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/channel-sessions")]
[Authorize]
public sealed class ChannelSessionsController : ControllerBase
{
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly IChannelSpamReputationRepository _spamReputationRepo;
    private readonly IChannelGateway _gateway;
    private readonly ITenantContextAccessor _tenantContext;

    public ChannelSessionsController(
        IChannelSessionRepository sessionRepo,
        IChannelSpamReputationRepository spamReputationRepo,
        IChannelGateway gateway,
        ITenantContextAccessor tenantContext)
    {
        _sessionRepo = sessionRepo;
        _spamReputationRepo = spamReputationRepo;
        _gateway = gateway;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Lists channel sessions for the tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier from the route.</param>
    /// <param name="channelId">Optional channel identifier filter.</param>
    /// <param name="status">Optional session lifecycle status filter such as <c>Active</c> or <c>Closed</c>.</param>
    /// <param name="operationalState">
    /// Optional derived routing state filter:
    /// <c>awaiting_classification</c>, <c>classified</c>, <c>pending_human_review</c>, <c>escalated_human</c>, or <c>spam_review</c>.
    /// </param>
    /// <param name="query">Optional identifier search term.</param>
    /// <param name="page">Zero-based page number.</param>
    /// <param name="pageSize">Page size between 1 and 100.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ChannelSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetActive(
        string tenantId,
        [FromQuery] string? channelId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? operationalState = null,
        [FromQuery] string? query = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var result = await _sessionRepo.SearchAsync(tenantId, channelId, status, operationalState, query, page, pageSize, ct);
        var commerce = HttpContext?.RequestServices?.GetService<ICommerceStore>();
        var items = new List<ChannelSessionDto>(result.Items.Count);
        foreach (var session in result.Items)
            items.Add(await MapSessionAsync(session, commerce, ct));

        return Ok(new PagedResponse<ChannelSessionDto>
        {
            Items = items,
            Total = result.Total,
            Page = Math.Max(0, page),
            PageSize = Math.Clamp(pageSize, 1, 100)
        });
    }

    /// <summary>
    /// Returns a single channel session with derived routing and spam-review state.
    /// </summary>
    [HttpGet("{sessionId}")]
    [ProducesResponseType(typeof(ChannelSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string tenantId, string sessionId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session == null) return NotFound();

        var commerce = HttpContext?.RequestServices?.GetService<ICommerceStore>();
        return Ok(await MapSessionAsync(session, commerce, ct));
    }

    [HttpPost("{sessionId}/close")]
    public async Task<IActionResult> CloseSession(string tenantId, string sessionId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        await _gateway.CloseSessionAsync(sessionId, tenantId, ct);
        return Ok(new { message = "Session closed successfully" });
    }

    /// <summary>
    /// Returns the persisted spam reputation for the customer associated with the session.
    /// </summary>
    [HttpGet("{sessionId}/spam-reputation")]
    [ProducesResponseType(typeof(SessionSpamReputationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSpamReputation(string tenantId, string sessionId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session == null) return NotFound();

        var reputation = await _spamReputationRepo.GetAsync(tenantId, session.ChannelId, session.Identifier, ct);
        return Ok(BuildSpamReputationDto(session, reputation));
    }

    /// <summary>
    /// Updates the persisted spam reputation for the customer associated with the session.
    /// </summary>
    [HttpPut("{sessionId}/spam-reputation")]
    [ProducesResponseType(typeof(SessionSpamReputationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSpamReputation(
        string tenantId,
        string sessionId,
        [FromBody] UpdateSessionSpamReputationRequest request,
        CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session == null) return NotFound();

        var normalizedStatus = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedStatus is not ("none" or "suspected" or "confirmed_spam" or "cleared"))
            return BadRequest(new { message = "Status must be one of: none, suspected, confirmed_spam, cleared." });

        var reputation = await _spamReputationRepo.GetAsync(tenantId, session.ChannelId, session.Identifier, ct)
            ?? ChannelSpamReputation.Create(tenantId, session.ChannelId, session.Identifier);

        switch (normalizedStatus)
        {
            case "suspected":
                reputation.MarkSuspected(request.ReasonCode);
                session.Metadata["routing.fallback.state"] = "spam_review";
                session.Metadata["routing.fallback.reason"] = request.ReasonCode ?? "manual_spam_review";
                session.Metadata["routing.guard.stage"] = "spam_review";
                session.Metadata["requires_human_review"] = "true";
                session.Metadata["reply_pending"] = "true";
                break;
            case "confirmed_spam":
                reputation.MarkConfirmed(request.ReasonCode);
                session.Metadata["routing.fallback.state"] = "spam_review";
                session.Metadata["routing.fallback.reason"] = request.ReasonCode ?? "manual_confirmed_spam";
                session.Metadata["routing.guard.stage"] = "spam_review";
                session.Metadata["requires_human_review"] = "true";
                session.Metadata["reply_pending"] = "true";
                break;
            case "cleared":
                reputation.Clear(request.ReasonCode);
                session.Metadata.Remove("routing.fallback.state");
                session.Metadata.Remove("routing.fallback.reason");
                session.Metadata["routing.guard.stage"] = "classified";
                session.Metadata["requires_human_review"] = "false";
                break;
            default:
                session.Metadata.Remove("routing.fallback.state");
                session.Metadata.Remove("routing.fallback.reason");
                session.Metadata["routing.guard.stage"] = "classified";
                session.Metadata["requires_human_review"] = "false";
                break;
        }

        await _spamReputationRepo.UpsertAsync(reputation, ct);
        await _sessionRepo.UpdateAsync(session, ct);

        return Ok(BuildSpamReputationDto(session, reputation));
    }

    [HttpGet("{sessionId}/messages")]
    [HttpPost("{sessionId}/messages")]
    public async Task<IActionResult> GetMessages(
        string tenantId,
        string sessionId,
        [FromQuery] int limit = 50,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var messageRepo = HttpContext.RequestServices.GetRequiredService<IChannelMessageRepository>();
        var threadRepo = HttpContext.RequestServices.GetService<IConversationThreadRepository>();
        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        var paged = Request.Query.ContainsKey("page") || Request.Query.ContainsKey("pageSize");
        if (paged || !string.IsNullOrWhiteSpace(cursor))
        {
            if (!string.IsNullOrWhiteSpace(cursor) && int.TryParse(cursor, out var cursorPage) && cursorPage >= 0)
                page = cursorPage;

            var result = await messageRepo.GetBySessionPagedAsync(sessionId, tenantId, page, pageSize, ct);
            if (session == null && result.Items.Count == 0)
                return NotFound();
            var hasMore = ((page + 1) * Math.Clamp(pageSize, 1, 100)) < result.Total;
            return Ok(new PagedResponse<ChannelMessageDto>
            {
                Items = session is null
                    ? result.Items.Select(MapMessage).OrderBy(x => x.CreatedAt).ToList()
                    : await BuildUnifiedMessagesAsync(session, result.Items, threadRepo, ct),
                Total = result.Total,
                Page = Math.Max(0, page),
                PageSize = Math.Clamp(pageSize, 1, 100),
                HasMore = hasMore,
                NextCursor = hasMore ? (page + 1).ToString() : null
            });
        }

        var messages = await messageRepo.GetBySessionAsync(sessionId, tenantId, limit, ct);
        if (session == null && messages.Count == 0)
            return NotFound();
        if (session is null)
            return Ok(messages.Select(MapMessage).OrderBy(x => x.CreatedAt).ToList());
        return Ok(await BuildUnifiedMessagesAsync(session, messages, threadRepo, ct));
    }

    private static ChannelSessionDto MapSession(ChannelSession session, ChannelSpamReputation? reputation = null)
    {
        var guardStage = session.Metadata.GetValueOrDefault("routing.guard.stage") ?? "classified";
        var fallbackState = session.Metadata.GetValueOrDefault("routing.fallback.state");
        var requiresHumanReview = string.Equals(session.Metadata.GetValueOrDefault("requires_human_review"), "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fallbackState, "spam_review", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fallbackState, "escalated_human", StringComparison.OrdinalIgnoreCase) ||
            reputation?.Status is SpamReputationStatus.Suspected or SpamReputationStatus.ConfirmedSpam;

        return new ChannelSessionDto
        {
            Id = session.Id,
            ChannelId = session.ChannelId,
            ChannelType = session.ChannelType,
            Identifier = session.Identifier,
            AgentId = session.AgentId,
            ThreadId = session.ThreadId,
            Status = session.Status.ToString(),
            MessageCount = session.MessageCount,
            CreatedAt = session.CreatedAt,
            LastActivityAt = session.LastActivityAt,
            ExpiresAt = session.ExpiresAt,
            WindowOpen = !session.IsExpired(),
            UnreadCount = session.Metadata.TryGetValue("unread_count", out var unread) && int.TryParse(unread, out var u) ? u : 0,
            ReplyPending = string.Equals(session.Metadata.GetValueOrDefault("reply_pending"), "true", StringComparison.OrdinalIgnoreCase),
            LastCustomerMessage = session.Metadata.GetValueOrDefault("last_customer_message"),
            LastAgentMessage = session.Metadata.GetValueOrDefault("last_agent_message"),
            LastError = session.Metadata.GetValueOrDefault("last_error"),
            LastFailureLevel = session.Metadata.GetValueOrDefault("last_failure_level"),
            CustomerKind = session.Metadata.GetValueOrDefault("customer_kind") ?? "unknown",
            DisplayName = session.Metadata.GetValueOrDefault("display_name"),
            RoutingWorkflowId = session.Metadata.GetValueOrDefault("routing_handoff_workflow"),
            RoutingStage = guardStage,
            RoutingFallbackState = fallbackState,
            RequiresHumanReview = requiresHumanReview,
            OperationalState = DeriveOperationalState(guardStage, fallbackState, requiresHumanReview, reputation),
            SpamReputationStatus = reputation?.Status switch
            {
                SpamReputationStatus.Suspected => "suspected",
                SpamReputationStatus.ConfirmedSpam => "confirmed_spam",
                SpamReputationStatus.Cleared => "cleared",
                _ => "none"
            },
            SpamSignalCount = reputation?.SignalCount ?? 0,
            SpamLastReasonCode = reputation?.LastReasonCode
        };
    }

    private static string DeriveOperationalState(
        string guardStage,
        string? fallbackState,
        bool requiresHumanReview,
        ChannelSpamReputation? reputation)
    {
        if (string.Equals(fallbackState, "spam_review", StringComparison.OrdinalIgnoreCase) ||
            reputation?.Status is SpamReputationStatus.Suspected or SpamReputationStatus.ConfirmedSpam)
            return "spam_review";

        if (string.Equals(fallbackState, "escalated_human", StringComparison.OrdinalIgnoreCase))
            return "escalated_human";

        if (requiresHumanReview)
            return "pending_human_review";

        if (string.Equals(guardStage, "accumulating", StringComparison.OrdinalIgnoreCase))
            return "awaiting_classification";

        return "classified";
    }

    private static SessionSpamReputationDto BuildSpamReputationDto(ChannelSession session, ChannelSpamReputation? reputation) => new()
    {
        SessionId = session.Id,
        ChannelId = session.ChannelId,
        Identifier = session.Identifier,
        Status = reputation?.Status switch
        {
            SpamReputationStatus.Suspected => "suspected",
            SpamReputationStatus.ConfirmedSpam => "confirmed_spam",
            SpamReputationStatus.Cleared => "cleared",
            _ => "none"
        },
        SignalCount = reputation?.SignalCount ?? 0,
        LastReasonCode = reputation?.LastReasonCode,
        UpdatedAt = reputation?.UpdatedAt
    };

    private async Task<ChannelSessionDto> MapSessionAsync(ChannelSession session, ICommerceStore? commerce, CancellationToken ct)
    {
        var reputation = await _spamReputationRepo.GetAsync(session.TenantId, session.ChannelId, session.Identifier, ct);
        if (commerce is null)
            return MapSession(session, reputation);

        var dto = MapSession(session, reputation);
        var party = await commerce.GetPartyByIdentityAsync(session.TenantId, session.ChannelType, session.Identifier, ct);
        if (party is null)
            return dto;

        return dto with
        {
            DisplayName = party.DisplayName ?? party.FullName ?? dto.DisplayName ?? session.Identifier,
            CustomerKind = party.Kind
        };
    }

    private static ChannelMessageDto MapMessage(ChannelMessage message) => new()
    {
        Id = message.Id,
        Direction = message.Direction.ToString(),
        Type = message.Type.ToString(),
        From = message.From,
        To = message.To,
        Content = message.Content,
        CreatedAt = message.CreatedAt,
        Status = message.Status.ToString(),
        AgentExecutionId = message.AgentExecutionId,
        ChannelMessageIdIn = message.Metadata.GetValueOrDefault("wa_message_id"),
        ChannelMessageIdOut = message.Metadata.GetValueOrDefault("wa_message_id_out"),
        Metadata = message.Metadata,
        ErrorMessage = message.ErrorMessage,
        Actor = message.Metadata.GetValueOrDefault("actor_label")
            ?? message.Metadata.GetValueOrDefault("actor_agent_id")
            ?? message.Metadata.GetValueOrDefault("actor")
            ?? (message.Direction == MessageDirection.Incoming ? "customer" : message.From),
        DeliveryState = message.Metadata.GetValueOrDefault("agentflow.delivery") ??
            (message.Direction == MessageDirection.Outgoing ? "sent" : "received")
    };

    private static async Task<IReadOnlyList<ChannelMessageDto>> BuildUnifiedMessagesAsync(
        ChannelSession session,
        IReadOnlyList<ChannelMessage> channelMessages,
        IConversationThreadRepository? threadRepo,
        CancellationToken ct)
    {
        var items = channelMessages.Select(MapMessage).ToList();
        var realMessages = items.ToList();
        if (threadRepo is null || string.IsNullOrWhiteSpace(session.ThreadId))
            return items.OrderBy(x => x.CreatedAt).ToList();

        var thread = await threadRepo.GetByIdAsync(session.ThreadId, session.TenantId, ct);
        if (thread is null)
            return items.OrderBy(x => x.CreatedAt).ToList();

        for (var index = 0; index < thread.Context.Turns.Count; index++)
        {
            var turn = thread.Context.Turns[index];
            if (!string.IsNullOrWhiteSpace(turn.UserMessage))
            {
                var inbound = new ChannelMessageDto
                {
                    Id = $"thread-{thread.Id}-u-{index}",
                    Direction = MessageDirection.Incoming.ToString(),
                    Type = MessageType.Text.ToString(),
                    From = session.Identifier,
                    To = null,
                    Content = turn.UserMessage,
                    CreatedAt = turn.Timestamp,
                    Status = "Merged",
                    Actor = "customer",
                    DeliveryState = "received",
                    Metadata = new Dictionary<string, string> { ["source"] = "thread" }
                };
                if (!HasEquivalentRealMessage(realMessages, inbound) && !HasEquivalentSyntheticMessage(items, inbound))
                    items.Add(inbound);
            }

            if (!string.IsNullOrWhiteSpace(turn.AssistantResponse))
            {
                var fallbackActor = !string.IsNullOrWhiteSpace(session.AgentId)
                    ? $"agent:{session.AgentId}"
                    : "bot";
                var outbound = new ChannelMessageDto
                {
                    Id = $"thread-{thread.Id}-a-{index}",
                    Direction = MessageDirection.Outgoing.ToString(),
                    Type = MessageType.Text.ToString(),
                    From = "bot",
                    To = session.Identifier,
                    Content = turn.AssistantResponse!,
                    CreatedAt = turn.Timestamp.AddMilliseconds(1),
                    Status = "Merged",
                    Actor = fallbackActor,
                    DeliveryState = "sent",
                    Metadata = new Dictionary<string, string> { ["source"] = "thread" }
                };
                if (!HasEquivalentRealMessage(realMessages, outbound) && !HasEquivalentSyntheticMessage(items, outbound))
                    items.Add(outbound);
            }
        }

        return items.OrderBy(x => x.CreatedAt).ToList();
    }

    private static bool HasEquivalentRealMessage(
        IReadOnlyList<ChannelMessageDto> realMessages,
        ChannelMessageDto candidate)
        => realMessages.Any(existing => AreEquivalentMessages(existing, candidate));

    private static bool HasEquivalentSyntheticMessage(
        IReadOnlyList<ChannelMessageDto> items,
        ChannelMessageDto candidate)
        => items.Any(existing => IsSyntheticThreadMessage(existing) && AreEquivalentMessages(existing, candidate));

    private static bool AreEquivalentMessages(ChannelMessageDto left, ChannelMessageDto right)
    {
        if (!string.Equals(left.Direction, right.Direction, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(NormalizeForKey(left.Content), NormalizeForKey(right.Content), StringComparison.Ordinal))
            return false;

        var delta = (left.CreatedAt - right.CreatedAt).Duration();
        return delta <= TimeSpan.FromSeconds(2);
    }

    private static bool IsSyntheticThreadMessage(ChannelMessageDto message)
        => string.Equals(message.Metadata.GetValueOrDefault("source"), "thread", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeForKey(string value)
    {
        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ')
            .ToArray());
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

public sealed record PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public long Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public bool HasMore { get; init; }
    public string? NextCursor { get; init; }
}

public sealed record ChannelSessionDto
{
    public required string Id { get; init; }
    public required string ChannelId { get; init; }
    public required string ChannelType { get; init; }
    public required string Identifier { get; init; }
    public string? AgentId { get; init; }
    public string? ThreadId { get; init; }
    public required string Status { get; init; }
    public int MessageCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool WindowOpen { get; init; }
    public int UnreadCount { get; init; }
    public bool ReplyPending { get; init; }
    public string? LastCustomerMessage { get; init; }
    public string? LastAgentMessage { get; init; }
    public string? LastError { get; init; }
    public string? LastFailureLevel { get; init; }
    public string CustomerKind { get; init; } = "unknown";
    public string? DisplayName { get; init; }
    public string? RoutingWorkflowId { get; init; }
    public string RoutingStage { get; init; } = "classified";
    public string? RoutingFallbackState { get; init; }
    public bool RequiresHumanReview { get; init; }
    public string OperationalState { get; init; } = "classified";
    public string SpamReputationStatus { get; init; } = "none";
    public int SpamSignalCount { get; init; }
    public string? SpamLastReasonCode { get; init; }
}

public sealed record UpdateSessionSpamReputationRequest
{
    public string? Status { get; init; }
    public string? ReasonCode { get; init; }
}

public sealed record SessionSpamReputationDto
{
    public required string SessionId { get; init; }
    public required string ChannelId { get; init; }
    public required string Identifier { get; init; }
    public required string Status { get; init; }
    public int SignalCount { get; init; }
    public string? LastReasonCode { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record ChannelMessageDto
{
    public required string Id { get; init; }
    public required string Direction { get; init; }
    public required string Type { get; init; }
    public required string From { get; init; }
    public string? To { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public required string Status { get; init; }
    public string? AgentExecutionId { get; init; }
    public string? ChannelMessageIdIn { get; init; }
    public string? ChannelMessageIdOut { get; init; }
    public string? ErrorMessage { get; init; }
    public string Actor { get; init; } = "system";
    public string DeliveryState { get; init; } = "unknown";
    public Dictionary<string, string> Metadata { get; init; } = new();
}
