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
    public async Task<IActionResult> GetActive(string tenantId, [FromQuery] string? channelId = null, CancellationToken ct = default)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        IReadOnlyList<Domain.Aggregates.ChannelSession> sessions;

        if (!string.IsNullOrEmpty(channelId))
        {
            sessions = await _gateway.GetActiveSessionsAsync(channelId, tenantId, ct);
        }
        else
        {
            var count = await _sessionRepo.GetActiveCountAsync(tenantId, ct);
            sessions = await _sessionRepo.GetActiveByUserAsync("%", tenantId, ct);
        }

        return Ok(sessions.Select(s => new ChannelSessionDto
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
            ExpiresAt = s.ExpiresAt
        }));
    }

    [HttpGet("{sessionId}")]
    public async Task<IActionResult> GetById(string tenantId, string sessionId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session == null) return NotFound();

        return Ok(new ChannelSessionDto
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
            ExpiresAt = session.ExpiresAt
        });
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
    public async Task<IActionResult> GetMessages(string tenantId, string sessionId, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var messageRepo = HttpContext.RequestServices.GetRequiredService<IChannelMessageRepository>();
        var messages = await messageRepo.GetBySessionAsync(sessionId, tenantId, limit, ct);

        return Ok(messages.Select(m => new ChannelMessageDto
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
            Metadata = m.Metadata
        }));
    }
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
    public Dictionary<string, string> Metadata { get; init; } = new();
}
