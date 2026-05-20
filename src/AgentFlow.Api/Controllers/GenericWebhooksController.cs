using AgentFlow.Abstractions;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Workflow;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/webhooks/{channel}")]
public sealed class GenericWebhooksController : ControllerBase
{
    private readonly IConnectStore _connectStore;
    private readonly IChannelGateway _gateway;
    private readonly IChannelDefinitionRepository _channelRepo;
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly IChannelMessageRepository _messageRepo;
    private readonly IWorkflowAuditService _audit;
    private readonly ILogger<GenericWebhooksController> _logger;

    public GenericWebhooksController(
        IConnectStore connectStore,
        IChannelGateway gateway,
        IChannelDefinitionRepository channelRepo,
        IChannelSessionRepository sessionRepo,
        IChannelMessageRepository messageRepo,
        IWorkflowAuditService audit,
        ILogger<GenericWebhooksController> logger)
    {
        _connectStore = connectStore;
        _gateway = gateway;
        _channelRepo = channelRepo;
        _sessionRepo = sessionRepo;
        _messageRepo = messageRepo;
        _audit = audit;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(
        [FromRoute] string tenantId,
        [FromRoute] string channel,
        [FromBody] Dictionary<string, object?> payload,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var externalEventKey = BuildExternalEventKey(channel, payload);
        if (!string.IsNullOrWhiteSpace(externalEventKey))
        {
            var existing = await _connectStore.GetInboxMessageByExternalKeyAsync(tenantId, externalEventKey, ct);
            if (existing is not null)
            {
                return Ok(new
                {
                    status = "duplicate_accepted",
                    tenantId,
                    channel,
                    inboxMessageId = existing.Id
                });
            }
        }

        var recipient = ReadString(payload, "recipient")
            ?? ReadString(payload, "from")
            ?? ReadString(payload, "sender")
            ?? "unknown";
        var content = ReadString(payload, "message")
            ?? ReadString(payload, "content")
            ?? ReadString(payload, "text")
            ?? "(empty)";

        var channelSession = await ResolveOrCreateSessionAsync(tenantId, channel, recipient, ct);
        var assignedTo = channelSession?.AgentId;
        ChannelMessage? incomingMessage = null;
        if (channelSession is not null)
        {
            incomingMessage = ChannelMessage.CreateIncoming(tenantId, channelSession.ChannelId, channelSession.Id, recipient, content);
            incomingMessage.Metadata["actor"] = "customer";
            incomingMessage.Metadata["source"] = "generic-webhook";
            channelSession.RecordIncomingMessage(content);
            await _sessionRepo.UpdateAsync(channelSession, ct);
        }

        var inboxMessage = await _connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            Channel = channel,
            Recipient = recipient,
            Content = content,
            ExternalEventKey = externalEventKey,
            Status = ConnectOperationalStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = "webhook"
        }, ct);
        await _audit.RecordStudioActionAsync(
            tenantId,
            "webhook",
            "workflow.webhook.received",
            "connect.message.received",
            new { channel, recipient, inboxMessageId = inboxMessage.Id },
            HttpContext.TraceIdentifier,
            ct);

                // Route by channel gateway (router/intents) instead of auto-triggering
        // the latest published workflow by event name.
        string? agentResponse = null;
        string? agentExecutionId = null;
        if (incomingMessage is not null)
        {
            var gatewayResponse = await _gateway.ProcessMessageAsync(incomingMessage, ct);
            agentResponse = gatewayResponse.Content;
            agentExecutionId = gatewayResponse.AgentExecutionId;
        }


        return Ok(new
        {
            status = "accepted",
            tenantId,
            channel,
            inboxMessageId = inboxMessage.Id,
            workflowExecutionId = (string?)null,
            sessionId = channelSession?.Id,
            assignedTo,
            agentExecutionId,
            agentResponse
        });
    }

    private async Task<ChannelSession?> ResolveOrCreateSessionAsync(
        string tenantId,
        string channelSlug,
        string recipient,
        CancellationToken ct)
    {
        var normalized = channelSlug.Trim().ToLowerInvariant();
        var type = normalized switch
        {
            "whatsapp" => ChannelType.WhatsApp,
            "email" => ChannelType.Email,
            "webchat" => ChannelType.WebChat,
            "api" => ChannelType.Api,
            "slack" => ChannelType.Slack,
            _ => ChannelType.Custom
        };

        var channel = (await _channelRepo.GetByTypeAsync(type, tenantId, ct))
            .FirstOrDefault(c => c.Status == ChannelStatus.Active);

        if (channel is null)
            return null;

        var existing = await _sessionRepo.GetByChannelAndIdentifierAsync(channel.Id, recipient, tenantId, ct);
        if (existing != null && !existing.IsExpired())
        {
            if (string.IsNullOrWhiteSpace(existing.AgentId))
            {
                var selectedAgent = await SelectAgentForSessionAsync(channel, ct);
                if (!string.IsNullOrWhiteSpace(selectedAgent))
                {
                    existing.LinkAgent(selectedAgent);
                    await _sessionRepo.UpdateAsync(existing, ct);
                }
            }
            return existing;
        }

        var session = ChannelSession.Create(tenantId, channel.Id, channel.Type, recipient);
        var assigned = await SelectAgentForSessionAsync(channel, ct);
        if (!string.IsNullOrWhiteSpace(assigned))
            session.LinkAgent(assigned);
        await _sessionRepo.InsertAsync(session, ct);
        return session;
    }

    private async Task<string?> SelectAgentForSessionAsync(ChannelDefinition channel, CancellationToken ct)
    {
        var routingAgentsRaw = channel.Config.GetValueOrDefault("IntentAgents")
            ?? channel.Config.GetValueOrDefault("RoutingAgents");
        if (!string.IsNullOrWhiteSpace(channel.RouterAgentId))
            return channel.RouterAgentId;

        if (string.IsNullOrWhiteSpace(routingAgentsRaw))
            return channel.Config.GetValueOrDefault("DefaultAgentId");

        var candidates = routingAgentsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
            return channel.Config.GetValueOrDefault("DefaultAgentId");

        var active = await _sessionRepo.GetActiveByChannelAsync(channel.Id, channel.TenantId, ct);
        var loadByAgent = active
            .Where(s => !string.IsNullOrWhiteSpace(s.AgentId))
            .GroupBy(s => s.AgentId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var capacities = ParseRoutingCapacities(channel.Config.GetValueOrDefault("RoutingCapacities"));
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

    private static string? ReadString(Dictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var raw) || raw is null) return null;
        return raw.ToString();
    }

    private static string? BuildExternalEventKey(string channel, Dictionary<string, object?> payload)
    {
        var nativeId = ReadString(payload, "eventId")
            ?? ReadString(payload, "event_id")
            ?? ReadString(payload, "messageId")
            ?? ReadString(payload, "message_id")
            ?? ReadString(payload, "id");

        if (!string.IsNullOrWhiteSpace(nativeId))
            return $"{channel}:{nativeId}";

        var from = ReadString(payload, "from") ?? ReadString(payload, "sender");
        var recipient = ReadString(payload, "recipient");
        var content = ReadString(payload, "message") ?? ReadString(payload, "content") ?? ReadString(payload, "text");
        var timestamp = ReadString(payload, "timestamp") ?? ReadString(payload, "ts");
        if (string.IsNullOrWhiteSpace(from) &&
            string.IsNullOrWhiteSpace(recipient) &&
            string.IsNullOrWhiteSpace(content) &&
            string.IsNullOrWhiteSpace(timestamp))
            return null;

        return $"{channel}:{from}:{recipient}:{timestamp}:{content}".Trim();
    }
}

