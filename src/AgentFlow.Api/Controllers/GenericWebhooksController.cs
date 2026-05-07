using AgentFlow.Abstractions.Connect;
using AgentFlow.Abstractions.Workflow;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Workflow;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/webhooks/{channel}")]
public sealed class GenericWebhooksController : ControllerBase
{
    private readonly IConnectStore _connectStore;
    private readonly IWorkflowTriggerService _triggerService;
    private readonly IWorkflowAuditService _audit;
    private readonly ILogger<GenericWebhooksController> _logger;

    public GenericWebhooksController(
        IConnectStore connectStore,
        IWorkflowTriggerService triggerService,
        IWorkflowAuditService audit,
        ILogger<GenericWebhooksController> logger)
    {
        _connectStore = connectStore;
        _triggerService = triggerService;
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
        var recipient = ReadString(payload, "recipient")
            ?? ReadString(payload, "from")
            ?? ReadString(payload, "sender")
            ?? "unknown";
        var content = ReadString(payload, "message")
            ?? ReadString(payload, "content")
            ?? ReadString(payload, "text")
            ?? "(empty)";

        var inboxMessage = await _connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            Channel = channel,
            Recipient = recipient,
            Content = content,
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

        var workflowPayload = new Dictionary<string, object?>
        {
            ["channel"] = channel,
            ["recipient"] = recipient,
            ["content"] = content,
            ["inboxMessageId"] = inboxMessage.Id,
            ["raw"] = payload
        };

        WorkflowExecutionContract? execution = null;
        try
        {
            execution = await _triggerService.TriggerEventAsync(
                tenantId,
                "connect.message.received",
                "webhook",
                HttpContext.TraceIdentifier,
                workflowPayload,
                ct);
            await _audit.RecordExecutionActionAsync(
                tenantId,
                "webhook",
                "workflow.execution.trigger.webhook",
                execution.Id,
                execution.WorkflowDefinitionId,
                new { channel, inboxMessageId = inboxMessage.Id },
                HttpContext.TraceIdentifier,
                ct);
        }
        catch (InvalidOperationException)
        {
            _logger.LogInformation("No published workflow for connect.message.received in tenant {TenantId}", tenantId);
        }

        return Ok(new
        {
            status = "accepted",
            tenantId,
            channel,
            inboxMessageId = inboxMessage.Id,
            workflowExecutionId = execution?.Id
        });
    }

    private static string? ReadString(Dictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var raw) || raw is null) return null;
        return raw.ToString();
    }
}
