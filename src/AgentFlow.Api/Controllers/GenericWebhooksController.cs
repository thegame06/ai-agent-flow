using AgentFlow.Abstractions.Connect;
using AgentFlow.Application.Channels;
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
    private readonly AgentFlow.Api.Channels.IInboundMessageIngest? _ingest;

    public GenericWebhooksController(
        IConnectStore connectStore,
        IChannelGateway gateway,
        IChannelDefinitionRepository channelRepo,
        IChannelSessionRepository sessionRepo,
        IChannelMessageRepository messageRepo,
        IWorkflowAuditService audit,
        ILogger<GenericWebhooksController> logger,
        AgentFlow.Api.Channels.IInboundMessageIngest? ingest = null)
    {
        _ingest = ingest;
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
            ?? string.Empty;

        var target = await ResolveChannelAsync(tenantId, channel, ct);
        if (target is null)
        {
            // No channel to answer through: keep the message for traceability only.
            var orphan = await _connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                Channel = channel,
                Recipient = recipient,
                Content = content.Length == 0 ? "(empty)" : content,
                ExternalEventKey = externalEventKey,
                Status = ConnectOperationalStatus.Queued,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = "webhook"
            }, ct);
            return Ok(new { status = "accepted", tenantId, channel, inboxMessageId = orphan.Id, sessionId = (string?)null });
        }

        // The same pipeline as every other channel: customer, session, inbox, campaign attribution, assistant.
        var ingest = _ingest ?? HttpContext.RequestServices.GetRequiredService<AgentFlow.Api.Channels.IInboundMessageIngest>();
        var result = await ingest.IngestAsync(new AgentFlow.Api.Channels.InboundMessage
        {
            TenantId = tenantId,
            Channel = target,
            Provider = channel.Trim().ToLowerInvariant(),
            ExternalId = externalEventKey,
            From = recipient,
            DisplayName = ReadString(payload, "displayName") ?? ReadString(payload, "name"),
            Text = content,
            Metadata = new Dictionary<string, string> { ["source"] = "generic-webhook" }
        }, ct: ct);

        await _audit.RecordStudioActionAsync(
            tenantId,
            "webhook",
            "workflow.webhook.received",
            "connect.message.received",
            new { channel, recipient, inboxMessageId = result.InboxMessageId },
            HttpContext.TraceIdentifier,
            ct);

        if (result.Status == AgentFlow.Api.Channels.InboundStatus.Duplicate)
            return Ok(new { status = "duplicate_accepted", tenantId, channel, inboxMessageId = result.InboxMessageId });

        return Ok(new
        {
            status = result.Status == AgentFlow.Api.Channels.InboundStatus.Ignored ? "ignored" : "accepted",
            tenantId,
            channel,
            inboxMessageId = result.InboxMessageId,
            workflowExecutionId = (string?)null,
            sessionId = result.Session?.Id,
            assignedTo = result.Session?.AgentId,
            agentExecutionId = result.Reply?.AgentExecutionId,
            agentResponse = result.Reply?.Content
        });
    }

    private async Task<ChannelDefinition?> ResolveChannelAsync(string tenantId, string channelSlug, CancellationToken ct)
    {
        var type = channelSlug.Trim().ToLowerInvariant() switch
        {
            "whatsapp" => ChannelType.WhatsApp,
            "email" => ChannelType.Email,
            "webchat" => ChannelType.WebChat,
            "api" => ChannelType.Api,
            "slack" => ChannelType.Slack,
            _ => ChannelType.Custom
        };
        return (await _channelRepo.GetByTypeAsync(type, tenantId, ct))
            .FirstOrDefault(c => c.Status == ChannelStatus.Active);
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

