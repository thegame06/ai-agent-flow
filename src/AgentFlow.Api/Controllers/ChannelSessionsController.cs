using AgentFlow.Application.Channels;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        return Ok(new PagedResponse<ChannelSessionDto>
        {
            Items = result.Items.Select(MapSession).ToList(),
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

        return Ok(MapSession(session));
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

        var messageRepo = HttpContext.RequestServices.GetRequiredService<IChannelMessageRepository>();
        var paged = Request.Query.ContainsKey("page") || Request.Query.ContainsKey("pageSize");
        if (paged || !string.IsNullOrWhiteSpace(cursor))
        {
            if (!string.IsNullOrWhiteSpace(cursor) && int.TryParse(cursor, out var cursorPage) && cursorPage >= 0)
            {
                page = cursorPage;
            }
            var result = await messageRepo.GetBySessionPagedAsync(sessionId, tenantId, page, pageSize, ct);
            var hasMore = ((page + 1) * Math.Clamp(pageSize, 1, 100)) < result.Total;
            return Ok(new PagedResponse<ChannelMessageDto>
            {
                Items = result.Items.Select(MapMessage).ToList(),
                Total = result.Total,
                Page = Math.Max(0, page),
                PageSize = Math.Clamp(pageSize, 1, 100),
                HasMore = hasMore,
                NextCursor = hasMore ? (page + 1).ToString() : null
            });
        }

        var messages = await messageRepo.GetBySessionAsync(sessionId, tenantId, limit, ct);
        return Ok(messages.Select(MapMessage));
    }

    private static ChannelSessionDto MapSession(Domain.Aggregates.ChannelSession s) => new()
    {
        Id = s.Id,
        ChannelId = s.ChannelId,
        ChannelType = s.ChannelType,
        Identifier = s.Identifier,
        AgentId = s.AgentId,
        ThreadId = s.ThreadId,
        Status = s.Status.ToString(),
        MessageCount = s.MessageCount,
        CreatedAt = s.CreatedAt,
        LastActivityAt = s.LastActivityAt,
        ExpiresAt = s.ExpiresAt,
        WindowOpen = !s.IsExpired(),
        CustomerKind = s.Metadata.GetValueOrDefault("customer_kind") ?? "unknown",
        DisplayName = s.Metadata.GetValueOrDefault("display_name")
    };

    private static ChannelMessageDto MapMessage(Domain.Aggregates.ChannelMessage m) => new()
    {
        Id = m.Id,
        Direction = m.Direction.ToString(),
        Type = m.Type.ToString(),
        From = m.From,
        To = m.To,
        Content = m.Content,
        CreatedAt = m.CreatedAt,
        Status = m.Status.ToString(),
        AgentExecutionId = m.AgentExecutionId,
        ChannelMessageIdIn = m.Metadata.GetValueOrDefault("wa_message_id"),
        ChannelMessageIdOut = m.Metadata.GetValueOrDefault("wa_message_id_out"),
        Metadata = m.Metadata,
        ErrorMessage = m.ErrorMessage,
        Actor = m.Metadata.GetValueOrDefault("actor") ??
            (m.Direction == Domain.Aggregates.MessageDirection.Incoming ? "customer" : m.From),
        DeliveryState = m.Metadata.GetValueOrDefault("agentflow.delivery") ??
            (m.Direction == Domain.Aggregates.MessageDirection.Outgoing ? "sent" : "received")
    };
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
    public string CustomerKind { get; init; } = "unknown";
    public string? DisplayName { get; init; }
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
