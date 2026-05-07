using AgentFlow.Abstractions.Workflow;
using AgentFlow.Api.Workflow;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/studio/workflows")]
[Authorize]
public sealed class WorkflowStudioController : ControllerBase
{
    private readonly IWorkflowStudioStore _store;
    private readonly IWorkflowTriggerService _triggerService;
    private readonly IWorkflowAuditService _audit;
    private readonly IWorkflowSecurityPolicyService _policy;
    private readonly ITenantContextAccessor _tenantContext;

    public WorkflowStudioController(
        IWorkflowStudioStore store,
        IWorkflowTriggerService triggerService,
        IWorkflowAuditService audit,
        IWorkflowSecurityPolicyService policy,
        ITenantContextAccessor tenantContext)
    {
        _store = store;
        _triggerService = triggerService;
        _audit = audit;
        _policy = policy;
        _tenantContext = tenantContext;
    }

    [HttpGet("catalog/activities")]
    public async Task<IActionResult> GetActivities(CancellationToken ct)
    {
        if (!CanAccessCatalog()) return Forbid();
        return Ok(await _store.GetActivitiesAsync(ct));
    }

    [HttpPut("catalog/activities/{typeName}")]
    public async Task<IActionResult> UpsertActivity([FromRoute] string typeName, [FromBody] UpsertWorkflowActivityRequest request, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var actor = _tenantContext.Current!.UserId;

        var saved = await _store.UpsertActivityAsync(new WorkflowActivityCatalogContract
        {
            TypeName = typeName,
            DisplayName = request.DisplayName,
            Category = request.Category,
            Description = request.Description,
            InputSchema = request.InputSchema,
            OutputSchema = request.OutputSchema,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = actor
        }, ct);
        await _audit.RecordStudioActionAsync("platform", actor, "workflow.catalog.activity.upsert", typeName, new
        {
            request.DisplayName,
            request.Category
        }, HttpContext.TraceIdentifier, ct);

        return Ok(saved);
    }

    [HttpGet("catalog/events")]
    public async Task<IActionResult> GetEvents(CancellationToken ct)
    {
        if (!CanAccessCatalog()) return Forbid();
        return Ok(await _store.GetEventsAsync(ct));
    }

    [HttpPut("catalog/events/{eventName}")]
    public async Task<IActionResult> UpsertEvent([FromRoute] string eventName, [FromBody] UpsertWorkflowEventRequest request, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var actor = _tenantContext.Current!.UserId;

        var saved = await _store.UpsertEventAsync(new WorkflowEventCatalogContract
        {
            EventName = eventName,
            DisplayName = request.DisplayName,
            Entity = request.Entity,
            Description = request.Description,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = actor
        }, ct);
        await _audit.RecordStudioActionAsync("platform", actor, "workflow.catalog.event.upsert", eventName, new
        {
            request.DisplayName,
            request.Entity
        }, HttpContext.TraceIdentifier, ct);

        return Ok(saved);
    }

    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();
        return Ok(await _store.GetTemplatesAsync(tenantId, ct));
    }

    [HttpPut("templates/{templateId}")]
    public async Task<IActionResult> UpsertTemplate([FromRoute] string tenantId, [FromRoute] string templateId, [FromBody] UpsertWorkflowTemplateRequest request, CancellationToken ct)
    {
        if (!CanManageTenant(tenantId)) return Forbid();

        var now = DateTimeOffset.UtcNow;
        var actor = _tenantContext.Current!.UserId;

        var saved = await _store.UpsertTemplateAsync(new WorkflowTemplateContract
        {
            Id = templateId,
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description,
            TriggerEventName = request.TriggerEventName,
            DefinitionJson = request.DefinitionJson,
            CreatedAt = request.CreatedAt ?? now,
            UpdatedAt = now,
            UpdatedBy = actor
        }, ct);
        await _audit.RecordStudioActionAsync(tenantId, actor, "workflow.template.upsert", templateId, new
        {
            request.Name,
            request.TriggerEventName
        }, HttpContext.TraceIdentifier, ct);

        return Ok(saved);
    }

    [HttpGet]
    public async Task<IActionResult> GetDefinitions([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();
        return Ok(await _store.GetDefinitionsAsync(tenantId, ct));
    }

    [HttpGet("{workflowId}")]
    public async Task<IActionResult> GetDefinition([FromRoute] string tenantId, [FromRoute] string workflowId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();

        var definition = await _store.GetDefinitionAsync(tenantId, workflowId, ct);
        return definition is null ? NotFound() : Ok(definition);
    }

    [HttpPut("{workflowId}")]
    public async Task<IActionResult> UpsertDefinition([FromRoute] string tenantId, [FromRoute] string workflowId, [FromBody] UpsertWorkflowDefinitionRequest request, CancellationToken ct)
    {
        if (!CanManageTenant(tenantId)) return Forbid();

        var now = DateTimeOffset.UtcNow;
        var actor = _tenantContext.Current!.UserId;

        var existing = await _store.GetDefinitionAsync(tenantId, workflowId, ct);
        var version = existing is null ? 1 : Math.Max(existing.Version, request.Version ?? existing.Version);

        var saved = await _store.UpsertDefinitionAsync(new WorkflowDefinitionContract
        {
            Id = workflowId,
            TenantId = tenantId,
            Name = request.Name,
            TriggerEventName = request.TriggerEventName,
            Version = version,
            Status = request.Status ?? existing?.Status ?? WorkflowDefinitionStatus.Draft,
            DefinitionJson = request.DefinitionJson,
            Metadata = request.Metadata,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            UpdatedBy = actor
        }, ct);
        await _audit.RecordStudioActionAsync(tenantId, actor, "workflow.definition.upsert", workflowId, new
        {
            request.Name,
            request.TriggerEventName,
            saved.Version,
            saved.Status
        }, HttpContext.TraceIdentifier, ct);

        return Ok(saved);
    }

    [HttpPost("{workflowId}/publish")]
    public async Task<IActionResult> PublishDefinition([FromRoute] string tenantId, [FromRoute] string workflowId, CancellationToken ct)
    {
        if (!CanManageTenant(tenantId)) return Forbid();

        var existing = await _store.GetDefinitionAsync(tenantId, workflowId, ct);
        if (existing is null) return NotFound(new { message = "Workflow definition not found." });
        try
        {
            _policy.ValidateDefinitionOrThrow(existing.DefinitionJson);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var saved = await _store.UpsertDefinitionAsync(existing with
        {
            Status = WorkflowDefinitionStatus.Published,
            Version = existing.Version + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = _tenantContext.Current!.UserId
        }, ct);
        await _audit.RecordStudioActionAsync(tenantId, _tenantContext.Current!.UserId, "workflow.definition.publish", workflowId, new
        {
            saved.Version
        }, HttpContext.TraceIdentifier, ct);

        return Ok(saved);
    }

    [HttpPost("run-event")]
    public async Task<IActionResult> RunEvent([FromRoute] string tenantId, [FromBody] RunWorkflowEventRequest request, CancellationToken ct)
    {
        if (!CanManageTenant(tenantId)) return Forbid();

        try
        {
            _policy.ValidatePayloadOrThrow(request.Payload);
            var execution = await _triggerService.TriggerEventAsync(
                tenantId,
                request.EventName,
                _tenantContext.Current!.UserId,
                request.CorrelationId,
                request.Payload,
                ct);
            await _audit.RecordExecutionActionAsync(
                tenantId,
                _tenantContext.Current!.UserId,
                "workflow.execution.trigger",
                execution.Id,
                execution.WorkflowDefinitionId,
                new { request.EventName, request.CorrelationId },
                HttpContext.TraceIdentifier,
                ct);
            return Ok(execution);
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("No published workflow matches", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { message = ex.Message });
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("executions")]
    public async Task<IActionResult> GetExecutions([FromRoute] string tenantId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();
        return Ok(await _store.GetExecutionsAsync(tenantId, limit, ct));
    }

    [HttpGet("executions/{executionId}/steps")]
    public async Task<IActionResult> GetExecutionSteps([FromRoute] string tenantId, [FromRoute] string executionId, CancellationToken ct = default)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();
        return Ok(await _store.GetStepLogsAsync(tenantId, executionId, ct));
    }

    [HttpPost("executions/{executionId}/retry")]
    public async Task<IActionResult> RetryExecution([FromRoute] string tenantId, [FromRoute] string executionId, CancellationToken ct = default)
    {
        if (!CanManageTenant(tenantId)) return Forbid();

        try
        {
            var retried = await _triggerService.RetryExecutionAsync(
                tenantId,
                executionId,
                _tenantContext.Current!.UserId,
                ct);
            await _audit.RecordExecutionActionAsync(
                tenantId,
                _tenantContext.Current!.UserId,
                "workflow.execution.retry",
                retried.Id,
                retried.WorkflowDefinitionId,
                new { sourceExecutionId = executionId },
                HttpContext.TraceIdentifier,
                ct);
            return Ok(retried);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private bool CanAccessCatalog()
    {
        var context = _tenantContext.Current!;
        return context.IsPlatformAdmin || context.HasPermission(AgentFlowPermissions.AgentRead);
    }

    private bool CanManage()
    {
        var context = _tenantContext.Current!;
        return context.IsPlatformAdmin || context.HasPermission(AgentFlowPermissions.AgentUpdate);
    }

    private bool CanAccessTenant(string tenantId)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(AgentFlowPermissions.AgentRead) || context.IsPlatformAdmin);
    }

    private bool CanManageTenant(string tenantId)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(AgentFlowPermissions.AgentUpdate) || context.IsPlatformAdmin);
    }
}

public sealed record UpsertWorkflowActivityRequest
{
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = "General";
    public string Description { get; init; } = string.Empty;
    public Dictionary<string, string> InputSchema { get; init; } = new();
    public Dictionary<string, string> OutputSchema { get; init; } = new();
}

public sealed record UpsertWorkflowEventRequest
{
    public string DisplayName { get; init; } = string.Empty;
    public string Entity { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed record UpsertWorkflowTemplateRequest
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string TriggerEventName { get; init; } = string.Empty;
    public string DefinitionJson { get; init; } = "{}";
    public DateTimeOffset? CreatedAt { get; init; }
}

public sealed record UpsertWorkflowDefinitionRequest
{
    public string Name { get; init; } = string.Empty;
    public string TriggerEventName { get; init; } = string.Empty;
    public string DefinitionJson { get; init; } = "{}";
    public WorkflowDefinitionStatus? Status { get; init; }
    public int? Version { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public sealed record RunWorkflowEventRequest
{
    public string EventName { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public Dictionary<string, object?>? Payload { get; init; }
}
