using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgentFlow.Infrastructure.Channels.Api;

/// <summary>
/// REST API channel handler for direct system-to-system integration.
/// </summary>
public sealed class ApiChannelHandler : IChannelHandler
{
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ApiChannelHandler> _logger;

    public ChannelType SupportedChannelType => ChannelType.Api;

    public ApiChannelHandler(
        IChannelSessionRepository sessionRepo,
        IHttpClientFactory httpClientFactory,
        ILogger<ApiChannelHandler> logger)
    {
        _sessionRepo = sessionRepo;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<ChannelStatus> InitializeAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        _logger.LogInformation("API channel {ChannelId} ready", definition.Id);
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
        var apiMessage = rawMessage as ApiIncomingMessage;
        if (apiMessage == null) return null;

        var systemId = apiMessage.SystemId ?? "unknown-system";
        var session = await GetOrCreateSessionAsync(
            ChannelContext.Create(ChannelType.Api, definition.Id, Guid.NewGuid().ToString("N"), systemId),
            definition,
            ct
        );

        var message = ChannelMessage.CreateIncoming(
            tenantId: definition.TenantId,
            channelId: definition.Id,
            sessionId: session.Id,
            from: systemId,
            content: apiMessage.Content,
            rawPayload: System.Text.Json.JsonSerializer.Serialize(apiMessage)
        );

        message.Metadata.TryAdd("api_version", apiMessage.ApiVersion ?? "1.0");
        message.Metadata.TryAdd("correlation_id", apiMessage.CorrelationId ?? Guid.NewGuid().ToString("N"));

        session.RecordIncomingMessage(apiMessage.Content);
        await _sessionRepo.UpdateAsync(session, ct);
        return message;
    }

    public async Task<SendResult> SendReplyAsync(ChannelMessage message, ChannelDefinition definition, CancellationToken ct = default)
    {
        message.MarkSent();

        // If a WebhookCallbackUrl is configured, POST the response asynchronously.
        // This is the "async API" mode: caller fires and forgets, receives result via webhook.
        var webhookUrl = definition.Config.GetValueOrDefault("WebhookCallbackUrl");
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            try
            {
                var payload = new
                {
                    messageId      = message.Id,
                    sessionId      = message.SessionId,
                    correlationId  = message.Metadata.GetValueOrDefault("correlation_id"),
                    content        = message.Content,
                    from           = message.From,
                    channelId      = message.ChannelId,
                    tenantId       = message.TenantId,
                    executionId    = message.AgentExecutionId,
                    metadata       = message.Metadata
                };

                using var http = _httpClientFactory.CreateClient("webhook");
                var response = await http.PostAsJsonAsync(webhookUrl, payload, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Webhook delivery to {Url} failed with HTTP {Status} for message {MessageId}",
                        webhookUrl, (int)response.StatusCode, message.Id);
                }
                else
                {
                    _logger.LogDebug(
                        "Webhook delivered to {Url} for message {MessageId}",
                        webhookUrl, message.Id);
                }
            }
            catch (Exception ex)
            {
                // Log but never throw — the agent response was already generated correctly.
                // Webhook delivery failure is an infrastructure concern, not a domain error.
                _logger.LogError(ex,
                    "Webhook delivery exception for message {MessageId} → {Url}",
                    message.Id, webhookUrl);
            }
        }

        return SendResult.Ok(message.Id);
    }

    public ChannelContext ExtractContext(object rawMessage, ChannelDefinition definition)
    {
        var apiMessage = rawMessage as ApiIncomingMessage;
        if (apiMessage == null)
            throw new ArgumentException("Invalid API message type", nameof(rawMessage));

        var context = ChannelContext.Create(
            ChannelType.Api,
            definition.Id,
            Guid.NewGuid().ToString("N"),
            apiMessage.SystemId ?? "unknown"
        );

        context.AddMetadata("api_version", apiMessage.ApiVersion ?? "1.0");
        context.AddMetadata("correlation_id", apiMessage.CorrelationId ?? Guid.NewGuid().ToString("N"));
        context.AddMetadata("client_ip", apiMessage.ClientIp ?? "unknown");

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
        return ChannelSession.Create(
            definition.TenantId,
            context.ChannelId,
            ChannelType.Api,
            context.UserIdentifier
        );
    }

    public Task<HealthStatus> CheckHealthAsync(ChannelDefinition definition, CancellationToken ct = default)
    {
        return Task.FromResult(HealthStatus.Ok("API channel operational"));
    }

    private async Task<string?> SelectAgentForSessionAsync(ChannelDefinition definition, CancellationToken ct)
    {
        var routingAgentsRaw = definition.Config.GetValueOrDefault("IntentAgents")
            ?? definition.Config.GetValueOrDefault("RoutingAgents");
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

public sealed record ApiIncomingMessage
{
    public string? SystemId { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? ApiVersion { get; init; }
    public string? CorrelationId { get; init; }
    public string? ClientIp { get; init; }
    public Dictionary<string, string>? Context { get; init; }
}
