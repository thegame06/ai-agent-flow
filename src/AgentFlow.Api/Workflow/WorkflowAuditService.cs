using System.Text.Json;
using AgentFlow.Application.Memory;

namespace AgentFlow.Api.Workflow;

public interface IWorkflowAuditService
{
    Task RecordStudioActionAsync(
        string tenantId,
        string actor,
        string action,
        string workflowId,
        object details,
        string? correlationId,
        CancellationToken ct = default);

    Task RecordExecutionActionAsync(
        string tenantId,
        string actor,
        string action,
        string executionId,
        string workflowId,
        object details,
        string? correlationId,
        CancellationToken ct = default);
}

public sealed class WorkflowAuditService : IWorkflowAuditService
{
    private readonly IAuditMemory _auditMemory;
    private readonly ILogger<WorkflowAuditService> _logger;

    public WorkflowAuditService(IAuditMemory auditMemory, ILogger<WorkflowAuditService> logger)
    {
        _auditMemory = auditMemory;
        _logger = logger;
    }

    public Task RecordStudioActionAsync(
        string tenantId,
        string actor,
        string action,
        string workflowId,
        object details,
        string? correlationId,
        CancellationToken ct = default)
    {
        return RecordAsync(
            tenantId,
            actor,
            action,
            executionId: string.Empty,
            workflowId,
            details,
            correlationId,
            ct);
    }

    public Task RecordExecutionActionAsync(
        string tenantId,
        string actor,
        string action,
        string executionId,
        string workflowId,
        object details,
        string? correlationId,
        CancellationToken ct = default)
    {
        return RecordAsync(
            tenantId,
            actor,
            action,
            executionId,
            workflowId,
            details,
            correlationId,
            ct);
    }

    private async Task RecordAsync(
        string tenantId,
        string actor,
        string action,
        string executionId,
        string workflowId,
        object details,
        string? correlationId,
        CancellationToken ct)
    {
        try
        {
            await _auditMemory.RecordAsync(new AuditEntry
            {
                TenantId = tenantId,
                UserId = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
                AgentId = string.IsNullOrWhiteSpace(workflowId) ? "workflow" : workflowId,
                ExecutionId = executionId,
                EventType = AuditEventType.ConnectOperation,
                CorrelationId = correlationId ?? string.Empty,
                EventJson = JsonSerializer.Serialize(new
                {
                    action,
                    details
                }),
                OccurredAt = DateTimeOffset.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow audit record failed. Action={Action} TenantId={TenantId}", action, tenantId);
        }
    }
}
