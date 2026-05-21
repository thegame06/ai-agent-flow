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
    private readonly IChannelGateway _gateway;
    private readonly ITenantContextAccessor _tenantContext;

    public ChannelSessionsController(
        IChannelSessionRepository sessionRepo,
        IChannelGateway gateway,
        ITenantContextAccessor tenantContext)
    {
        _sessionRepo = sessionRepo;
        _gateway = gateway;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetActive(
        string tenantId,
        [FromQuery] string? channelId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? query = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var result = await _sessionRepo.SearchAsync(tenantId, channelId, status, query, page, pageSize, ct);
        var commerce = HttpContext.RequestServices.GetService<ICommerceStore>();
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

    [HttpGet("{sessionId}")]
    public async Task<IActionResult> GetById(string tenantId, string sessionId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session == null) return NotFound();

        var commerce = HttpContext.RequestServices.GetService<ICommerceStore>();
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

        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session == null) return NotFound();

        var messageRepo = HttpContext.RequestServices.GetRequiredService<IChannelMessageRepository>();
        var threadRepo = HttpContext.RequestServices.GetService<IConversationThreadRepository>();
        var paged = Request.Query.ContainsKey("page") || Request.Query.ContainsKey("pageSize");
        if (paged || !string.IsNullOrWhiteSpace(cursor))
        {
            if (!string.IsNullOrWhiteSpace(cursor) && int.TryParse(cursor, out var cursorPage) && cursorPage >= 0)
                page = cursorPage;

            var result = await messageRepo.GetBySessionPagedAsync(sessionId, tenantId, page, pageSize, ct);
            var hasMore = ((page + 1) * Math.Clamp(pageSize, 1, 100)) < result.Total;
            return Ok(new PagedResponse<ChannelMessageDto>
            {
                Items = await BuildUnifiedMessagesAsync(session, result.Items, threadRepo, ct),
                Total = result.Total,
                Page = Math.Max(0, page),
                PageSize = Math.Clamp(pageSize, 1, 100),
                HasMore = hasMore,
                NextCursor = hasMore ? (page + 1).ToString() : null
            });
        }

        var messages = await messageRepo.GetBySessionAsync(sessionId, tenantId, limit, ct);
        return Ok(await BuildUnifiedMessagesAsync(session, messages, threadRepo, ct));
    }

    private static ChannelSessionDto MapSession(ChannelSession session) => new()
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
        RoutingWorkflowId = session.Metadata.GetValueOrDefault("routing_handoff_workflow")
    };

    private static async Task<ChannelSessionDto> MapSessionAsync(ChannelSession session, ICommerceStore? commerce, CancellationToken ct)
    {
        if (commerce is null)
            return MapSession(session);

        var dto = MapSession(session);
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
        if (threadRepo is null || string.IsNullOrWhiteSpace(session.ThreadId))
            return items.OrderBy(x => x.CreatedAt).ToList();

        var thread = await threadRepo.GetByIdAsync(session.ThreadId, session.TenantId, ct);
        if (thread is null)
            return items.OrderBy(x => x.CreatedAt).ToList();

        var seen = new HashSet<string>(items.Select(BuildDedupKey), StringComparer.Ordinal);

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
                if (seen.Add(BuildDedupKey(inbound)))
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
                if (seen.Add(BuildDedupKey(outbound)))
                    items.Add(outbound);
            }
        }

        return items.OrderBy(x => x.CreatedAt).ToList();
    }

    private static string BuildDedupKey(ChannelMessageDto message)
        => $"{message.Direction}|{message.Actor}|{NormalizeForKey(message.Content)}|{message.CreatedAt:yyyyMMddHHmm}";

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
