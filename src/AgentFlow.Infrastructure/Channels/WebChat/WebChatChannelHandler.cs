using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Channels.WebChat;

/// <summary>
/// Web chat widget channel handler (embedded widget on websites).
/// </summary>
public sealed class WebChatChannelHandler : IChannelHandler
{
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly ILogger<WebChatChannelHandler> _logger;

    public ChannelType SupportedChannelType => ChannelType.WebChat;

    public WebChatChannelHandler(
        IChannelSessionRepository sessionRepo,
        ILogger<WebChatChannelHandler> logger)
    {
        _sessionRepo = sessionRepo;
        _logger = logger;
    }

    public Task<ChannelStatus> InitializeAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        _logger.LogInformation("WebChat channel {ChannelId} ready", definition.Id);
        definition.Activate();
        return Task.FromResult(ChannelStatus.Active);
    }

    public Task ShutdownAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        definition.Deactivate();
        return Task.CompletedTask;
    }

    public async Task<ChannelMessage?> ProcessIncomingMessageAsync(object rawMessage, ChannelDefinition definition, CancellationToken ct = default)
    {
        var webMessage = rawMessage as WebChatIncomingMessage;
        if (webMessage == null) return null;

        var userId = webMessage.UserId;
        var session = await GetOrCreateSessionAsync(
            ChannelContext.Create(ChannelType.WebChat, definition.Id, Guid.NewGuid().ToString("N"), userId, webMessage.UserName),
            definition,
            ct
        );

        var message = ChannelMessage.CreateIncoming(
            tenantId: definition.TenantId,
            channelId: definition.Id,
            sessionId: session.Id,
            from: userId,
            content: webMessage.Content,
            rawPayload: System.Text.Json.JsonSerializer.Serialize(webMessage)
        );

        message.Metadata.TryAdd("browser", webMessage.Browser ?? "unknown");
        message.Metadata.TryAdd("page_url", webMessage.PageUrl ?? "unknown");

        session.RecordIncomingMessage(webMessage.Content);
        await _sessionRepo.UpdateAsync(session, ct);

        return message;
    }

    public async Task<SendResult> SendReplyAsync(ChannelMessage message, ChannelDefinition definition, CancellationToken ct = default)
    {
        // Web chat replies are sent via WebSocket/SignalR to connected clients
        // For now, mark as sent (actual delivery handled by SignalR hub)
        message.MarkSent();
        await Task.CompletedTask;
        return SendResult.Ok(message.Id);
    }

    public ChannelContext ExtractContext(object rawMessage, ChannelDefinition definition)
    {
        var webMessage = rawMessage as WebChatIncomingMessage;
        if (webMessage == null)
            throw new ArgumentException("Invalid WebChat message type", nameof(rawMessage));

        var context = ChannelContext.Create(
            ChannelType.WebChat,
            definition.Id,
            Guid.NewGuid().ToString("N"),
            webMessage.UserId,
            webMessage.UserName
        );

        context.AddMetadata("browser", webMessage.Browser ?? "unknown");
        context.AddMetadata("page_url", webMessage.PageUrl ?? "unknown");
        context.AddMetadata("ip_address", webMessage.IpAddress ?? "unknown");

        return context;
    }

    public async Task<ChannelSession> GetOrCreateSessionAsync(ChannelContext context, ChannelDefinition definition, CancellationToken ct = default)
    {
        var existing = await _sessionRepo.GetByChannelAndIdentifierAsync(
            context.ChannelId,
            context.UserIdentifier,
            definition.TenantId,
            ct);

        if (existing != null && !existing.IsExpired())
        {
            if (string.IsNullOrWhiteSpace(existing.AgentId))
            {
                var selected = await SelectAgentForSessionAsync(definition, ct);
                if (!string.IsNullOrWhiteSpace(selected))
                    existing.LinkAgent(selected);
            }
            return existing;
        }

        var session = GetOrCreateSessionSync(context, definition);
        var assigned = await SelectAgentForSessionAsync(definition, ct);
        if (!string.IsNullOrWhiteSpace(assigned))
            session.LinkAgent(assigned);
        await _sessionRepo.InsertAsync(session, ct);
        return session;
    }

    private ChannelSession GetOrCreateSessionSync(ChannelContext context, ChannelDefinition definition)
    {
        // Synchronous helper for session creation
        return ChannelSession.Create(
            definition.TenantId,
            context.ChannelId,
            ChannelType.WebChat,
            context.UserIdentifier
        );
    }

    public Task<HealthStatus> CheckHealthAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        // Web chat is always healthy if the server is running
        return Task.FromResult(HealthStatus.Ok("WebChat server running"));
    }

    private async Task<string?> SelectAgentForSessionAsync(ChannelDefinition definition, CancellationToken ct)
    {
        var routingAgentsRaw = definition.Config.GetValueOrDefault("RoutingAgents");
        if (!string.IsNullOrWhiteSpace(definition.RouterAgentId))
            return definition.RouterAgentId;

        if (string.IsNullOrWhiteSpace(routingAgentsRaw))
            return definition.Config.GetValueOrDefault("DefaultAgentId");

        var candidates = routingAgentsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
            return definition.Config.GetValueOrDefault("DefaultAgentId");

        var active = await _sessionRepo.GetActiveByChannelAsync(definition.Id, definition.TenantId, ct);
        var loadByAgent = active
            .Where(s => !string.IsNullOrWhiteSpace(s.AgentId))
            .GroupBy(s => s.AgentId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var capacities = ParseRoutingCapacities(definition.Config.GetValueOrDefault("RoutingCapacities"));
        var withinCapacity = candidates
            .Where(agentId => !capacities.TryGetValue(agentId, out var max) || (loadByAgent.TryGetValue(agentId, out var current) ? current : 0) < max)
            .ToList();
        var pool = withinCapacity.Count > 0 ? withinCapacity : candidates;

        return pool
            .OrderBy(a => loadByAgent.TryGetValue(a, out var count) ? count : 0)
            .ThenBy(a => a, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static Dictionary<string, int> ParseRoutingCapacities(string? raw)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return result;
        var entries = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[1], out var cap) && cap > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                result[parts[0]] = cap;
        }
        return result;
    }
}

public sealed record WebChatIncomingMessage
{
    public string UserId { get; init; } = string.Empty;
    public string? UserName { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? Browser { get; init; }
    public string? PageUrl { get; init; }
    public string? IpAddress { get; init; }
}
