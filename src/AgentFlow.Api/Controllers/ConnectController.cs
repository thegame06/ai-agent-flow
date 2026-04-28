using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Connect;
using AgentFlow.Application.Memory;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/connect")]
[Authorize]
public sealed class ConnectController : ControllerBase
{
    private readonly IConnectStore _store;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IAgentDefinitionRepository _agentRepository;
    private readonly IAuditMemory _auditMemory;

    public ConnectController(
        IConnectStore store,
        ITenantContextAccessor tenantContext,
        IAgentDefinitionRepository agentRepository,
        IAuditMemory auditMemory)
    {
        _store = store;
        _tenantContext = tenantContext;
        _agentRepository = agentRepository;
        _auditMemory = auditMemory;
    }

    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _store.GetTemplatesAsync(tenantId, ct));
    }

    [HttpPut("templates/{templateId}")]
    public async Task<IActionResult> UpsertTemplate([FromRoute] string tenantId, [FromRoute] string templateId, [FromBody] UpsertConnectTemplateRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();

        var linkedAgent = await ResolvePublishedAgentAsync(tenantId, request.PublishedWorkflowAgentId, ct);
        if (request.PublishedWorkflowAgentId is not null && linkedAgent is null)
            return BadRequest(new { message = "Published workflow agent not found or not published in Studio." });

        var now = DateTimeOffset.UtcNow;
        var actor = _tenantContext.Current!.UserId;
        var existing = await _store.GetTemplateAsync(tenantId, templateId, ct);

        var saved = await _store.UpsertTemplateAsync(new ConnectTemplateContract
        {
            Id = templateId,
            TenantId = tenantId,
            Name = request.Name,
            Channel = request.Channel,
            Body = request.Body,
            PublishedWorkflowAgentId = linkedAgent?.Id,
            PublishedWorkflowAgentName = linkedAgent?.Name,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            UpdatedBy = actor
        }, ct);

        await RecordTraceAsync(tenantId, "connect.template.upsert", templateId, new
        {
            request.Name,
            request.Channel,
            request.PublishedWorkflowAgentId
        }, ct);

        return Ok(saved);
    }

    [HttpGet("campaigns")]
    public async Task<IActionResult> GetCampaigns([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _store.GetCampaignsAsync(tenantId, ct));
    }

    [HttpPut("campaigns/{campaignId}")]
    public async Task<IActionResult> UpsertCampaign([FromRoute] string tenantId, [FromRoute] string campaignId, [FromBody] UpsertConnectCampaignRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();

        var template = await _store.GetTemplateAsync(tenantId, request.TemplateId, ct);
        if (template is null)
            return BadRequest(new { message = "Template not found for this tenant." });

        var linkedAgent = await ResolvePublishedAgentAsync(tenantId, request.PublishedWorkflowAgentId ?? template.PublishedWorkflowAgentId, ct);
        if ((request.PublishedWorkflowAgentId ?? template.PublishedWorkflowAgentId) is not null && linkedAgent is null)
            return BadRequest(new { message = "Published workflow agent not found or not published in Studio." });

        var now = DateTimeOffset.UtcNow;
        var actor = _tenantContext.Current!.UserId;
        var existing = (await _store.GetCampaignsAsync(tenantId, ct)).FirstOrDefault(x => x.Id == campaignId);

        var saved = await _store.UpsertCampaignAsync(new ConnectCampaignContract
        {
            Id = campaignId,
            TenantId = tenantId,
            Name = request.Name,
            Channel = request.Channel,
            TemplateId = request.TemplateId,
            PublishedWorkflowAgentId = linkedAgent?.Id,
            ScheduledAt = request.ScheduledAt,
            Enabled = request.Enabled,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            UpdatedBy = actor
        }, ct);

        await RecordTraceAsync(tenantId, "connect.campaign.upsert", campaignId, new
        {
            request.Name,
            request.Channel,
            request.TemplateId,
            WorkflowAgentId = linkedAgent?.Id
        }, ct);

        return Ok(saved);
    }

    [HttpGet("inbox")]
    public async Task<IActionResult> GetInbox([FromRoute] string tenantId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _store.GetInboxAsync(tenantId, limit, ct));
    }

    [HttpPost("inbox")]
    public async Task<IActionResult> EnqueueInbox([FromRoute] string tenantId, [FromBody] CreateConnectInboxMessageRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();

        var actor = _tenantContext.Current!.UserId;
        var now = DateTimeOffset.UtcNow;

        var created = await _store.CreateInboxMessageAsync(new ConnectInboxMessageContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            Channel = request.Channel,
            Recipient = request.Recipient,
            Content = request.Content,
            CampaignId = request.CampaignId,
            TemplateId = request.TemplateId,
            Status = ConnectOperationalStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = actor
        }, ct);

        await RecordTraceAsync(tenantId, "connect.inbox.enqueue", created.Id, new
        {
            created.Channel,
            created.CampaignId,
            created.TemplateId
        }, ct);

        return Ok(created);
    }

    [HttpPut("inbox/{messageId}/status")]
    public async Task<IActionResult> UpdateStatus([FromRoute] string tenantId, [FromRoute] string messageId, [FromBody] UpdateConnectStatusRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectOperate)) return Forbid();

        var updated = await _store.UpdateMessageStatusAsync(tenantId, messageId, request.Status, _tenantContext.Current!.UserId, request.LastError, ct);
        if (updated is null) return NotFound(new { message = "Message not found for tenant." });

        await RecordTraceAsync(tenantId, "connect.inbox.status", messageId, new
        {
            request.Status,
            request.LastError
        }, ct);

        return Ok(updated);
    }

    [HttpGet("monitoring/metrics")]
    public async Task<IActionResult> GetMonitoringMetrics([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var inbox = await _store.GetInboxAsync(tenantId, 5000, ct);

        var byChannel = BuildMetrics("channel", inbox.GroupBy(x => x.Channel));
        var byCampaign = BuildMetrics("campaign", inbox.GroupBy(x => x.CampaignId ?? "unassigned"));
        var byTemplate = BuildMetrics("template", inbox.GroupBy(x => x.TemplateId ?? "unassigned"));

        return Ok(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            rows = byChannel.Concat(byCampaign).Concat(byTemplate)
        });
    }

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }

    private async Task<Domain.Aggregates.AgentDefinition?> ResolvePublishedAgentAsync(string tenantId, string? agentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return null;

        var agent = await _agentRepository.GetByIdAsync(agentId, tenantId, ct);
        return agent is { Status: AgentStatus.Published } ? agent : null;
    }

    private async Task RecordTraceAsync(string tenantId, string action, string resourceId, object payload, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        await _auditMemory.RecordAsync(new AuditEntry
        {
            TenantId = tenantId,
            UserId = context.UserId,
            AgentId = "connect-module",
            ExecutionId = resourceId,
            EventType = AuditEventType.ConnectOperation,
            CorrelationId = HttpContext.TraceIdentifier,
            EventJson = System.Text.Json.JsonSerializer.Serialize(new { action, payload })
        }, ct);
    }

    private static IReadOnlyList<ConnectMetricsRowContract> BuildMetrics(
        string dimension,
        IEnumerable<IGrouping<string, ConnectInboxMessageContract>> groups)
    {
        return groups.Select(g =>
        {
            var total = g.Count();
            var delivered = g.Count(x => x.Status == ConnectOperationalStatus.Delivered);
            var read = g.Count(x => x.Status == ConnectOperationalStatus.Read);
            var failed = g.Count(x => x.Status == ConnectOperationalStatus.Failed);

            return new ConnectMetricsRowContract
            {
                Dimension = dimension,
                Key = g.Key,
                Total = total,
                Queued = g.Count(x => x.Status == ConnectOperationalStatus.Queued),
                Sent = g.Count(x => x.Status == ConnectOperationalStatus.Sent),
                Delivered = delivered,
                Read = read,
                Failed = failed,
                Escalated = g.Count(x => x.Status == ConnectOperationalStatus.Escalated),
                DeliveryRate = total == 0 ? 0 : Math.Round(delivered / (double)total, 4),
                ReadRate = total == 0 ? 0 : Math.Round(read / (double)total, 4),
                FailureRate = total == 0 ? 0 : Math.Round(failed / (double)total, 4)
            };
        }).OrderByDescending(x => x.Total).ToList();
    }
}

public sealed record UpsertConnectTemplateRequest
{
    public string Name { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string? PublishedWorkflowAgentId { get; init; }
}

public sealed record UpsertConnectCampaignRequest
{
    public string Name { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string TemplateId { get; init; } = string.Empty;
    public string? PublishedWorkflowAgentId { get; init; }
    public DateTimeOffset ScheduledAt { get; init; } = DateTimeOffset.UtcNow;
    public bool Enabled { get; init; } = true;
}

public sealed record CreateConnectInboxMessageRequest
{
    public string Channel { get; init; } = string.Empty;
    public string Recipient { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string? CampaignId { get; init; }
    public string? TemplateId { get; init; }
}

public sealed record UpdateConnectStatusRequest
{
    public ConnectOperationalStatus Status { get; init; }
    public string? LastError { get; init; }
}
