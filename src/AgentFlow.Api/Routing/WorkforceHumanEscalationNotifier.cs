using AgentFlow.Abstractions;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Connect;
using AgentFlow.Application.Memory;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Text.Json;

namespace AgentFlow.Api.Routing;

public sealed class WorkforceHumanEscalationNotifier : IHumanEscalationNotifier
{
    private readonly IMongoCollection<WorkforceQueueDocument> _queues;
    private readonly IConnectStore _connectStore;
    private readonly IAuditMemory _auditMemory;
    private readonly ILogger<WorkforceHumanEscalationNotifier> _logger;

    public WorkforceHumanEscalationNotifier(
        IMongoDatabase database,
        IConnectStore connectStore,
        IAuditMemory auditMemory,
        ILogger<WorkforceHumanEscalationNotifier> logger)
    {
        _queues = database.GetCollection<WorkforceQueueDocument>("workforce_queues");
        _connectStore = connectStore;
        _auditMemory = auditMemory;
        _logger = logger;
    }

    public async Task<HumanEscalationNotificationResult> NotifyAsync(
        HumanEscalationNotificationRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.QueueId))
        {
            return new HumanEscalationNotificationResult
            {
                Delivered = false,
                QueueId = string.Empty,
                Reason = "queue_id_missing"
            };
        }

        var queue = await _queues
            .Find(x => x.TenantId == request.TenantId && x.Id == request.QueueId)
            .FirstOrDefaultAsync(ct);

        if (queue is null || !queue.Active)
        {
            return new HumanEscalationNotificationResult
            {
                Delivered = false,
                QueueId = request.QueueId,
                QueueName = queue?.Name ?? string.Empty,
                Reason = queue is null ? "queue_not_found" : "queue_inactive"
            };
        }

        var activeMembers = queue.Members.Count(m => m.Active);
        var ticketId = $"esc-{Guid.NewGuid():N}";
        var eventKey = $"fallback:{request.TenantId}:{request.ConversationId}:{request.ExecutionId}:{queue.Id}";
        var now = DateTimeOffset.UtcNow;

        var payload = new
        {
            type = "human_escalation",
            ticketId,
            queueId = queue.Id,
            queueName = queue.Name,
            conversationId = request.ConversationId,
            userId = request.UserId,
            channel = request.Channel,
            reasonCode = request.ReasonCode,
            lastMessage = request.LastMessage,
            executionId = request.ExecutionId,
            correlationId = request.CorrelationId,
            createdAt = now
        };

        await _connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
        {
            Id = ticketId,
            TenantId = request.TenantId,
            Channel = "human-review",
            Recipient = $"queue:{queue.Id}",
            Content = JsonSerializer.Serialize(payload),
            ExternalEventKey = eventKey,
            Status = ConnectOperationalStatus.Escalated,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = "routing-fallback"
        }, ct);

        await _auditMemory.RecordAsync(new AuditEntry
        {
            ExecutionId = request.ExecutionId,
            AgentId = "routing-fallback",
            TenantId = request.TenantId,
            UserId = request.UserId,
            CorrelationId = request.CorrelationId,
            EventType = AuditEventType.ConnectOperation,
            EventJson = JsonSerializer.Serialize(new
            {
                action = "fallback.notify_queue",
                queueId = queue.Id,
                queueName = queue.Name,
                activeMembers,
                ticketId,
                conversationId = request.ConversationId
            })
        }, ct);

        _logger.LogInformation(
            "Fallback escalation delivered to queue {QueueId} ({QueueName}) with ticket {TicketId}",
            queue.Id, queue.Name, ticketId);

        return new HumanEscalationNotificationResult
        {
            Delivered = true,
            QueueId = queue.Id,
            QueueName = queue.Name,
            ActiveMembers = activeMembers,
            TicketId = ticketId
        };
    }

    private sealed record WorkforceQueueDocument
    {
        [BsonId]
        public string Id { get; set; } = string.Empty;
        [BsonElement("tenant_id")]
        public string TenantId { get; set; } = string.Empty;
        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;
        [BsonElement("active")]
        public bool Active { get; set; } = true;
        [BsonElement("members")]
        public List<WorkforceQueueMemberDocument> Members { get; set; } = new();
    }

    private sealed record WorkforceQueueMemberDocument
    {
        [BsonElement("member_id")]
        public string MemberId { get; set; } = string.Empty;
        [BsonElement("active")]
        public bool Active { get; set; } = true;
    }
}
