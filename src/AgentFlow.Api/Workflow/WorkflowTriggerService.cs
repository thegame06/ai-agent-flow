using System.Text.Json;
using AgentFlow.Abstractions.Workflow;

namespace AgentFlow.Api.Workflow;

public interface IWorkflowTriggerService
{
    Task<WorkflowExecutionContract> TriggerEventAsync(
        string tenantId,
        string eventName,
        string requestedBy,
        string? correlationId,
        Dictionary<string, object?>? payload,
        CancellationToken ct = default);

    Task<WorkflowExecutionContract> RetryExecutionAsync(
        string tenantId,
        string executionId,
        string requestedBy,
        CancellationToken ct = default);
}

public sealed class WorkflowTriggerService : IWorkflowTriggerService
{
    private readonly IWorkflowStudioStore _store;
    private readonly IWorkflowExecutionQueue _queue;
    private readonly IWorkflowSecurityPolicyService _policy;

    public WorkflowTriggerService(
        IWorkflowStudioStore store,
        IWorkflowExecutionQueue queue,
        IWorkflowSecurityPolicyService policy)
    {
        _store = store;
        _queue = queue;
        _policy = policy;
    }

    public async Task<WorkflowExecutionContract> TriggerEventAsync(
        string tenantId,
        string eventName,
        string requestedBy,
        string? correlationId,
        Dictionary<string, object?>? payload,
        CancellationToken ct = default)
    {
        var defs = await _store.GetDefinitionsAsync(tenantId, ct);
        var definition = defs
            .Where(x => x.Status == WorkflowDefinitionStatus.Published && string.Equals(x.TriggerEventName, eventName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();

        if (definition is null)
            throw new InvalidOperationException("No published workflow matches the requested event.");
        _policy.ValidateDefinitionOrThrow(definition.DefinitionJson);
        _policy.ValidatePayloadOrThrow(payload);

        var now = DateTimeOffset.UtcNow;
        var execution = await _store.CreateExecutionAsync(new WorkflowExecutionContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            WorkflowDefinitionId = definition.Id,
            TriggerEventName = eventName,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
            PayloadJson = JsonSerializer.Serialize(payload ?? new Dictionary<string, object?>()),
            ContextJson = "{}",
            Status = WorkflowExecutionStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now,
            RequestedBy = requestedBy
        }, ct);

        await _queue.EnqueueAsync(new WorkflowQueueItem(tenantId, execution.Id), ct);
        return execution;
    }

    public async Task<WorkflowExecutionContract> RetryExecutionAsync(
        string tenantId,
        string executionId,
        string requestedBy,
        CancellationToken ct = default)
    {
        var previous = (await _store.GetExecutionsAsync(tenantId, 1000, ct))
            .FirstOrDefault(x => x.Id == executionId);
        if (previous is null)
            throw new InvalidOperationException("Execution not found.");
        if (previous.Status != WorkflowExecutionStatus.Failed)
            throw new InvalidOperationException("Only failed executions can be retried.");

        var now = DateTimeOffset.UtcNow;
        var retry = await _store.CreateExecutionAsync(new WorkflowExecutionContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            WorkflowDefinitionId = previous.WorkflowDefinitionId,
            TriggerEventName = previous.TriggerEventName,
            CorrelationId = string.IsNullOrWhiteSpace(previous.CorrelationId) ? Guid.NewGuid().ToString("N") : previous.CorrelationId,
            PayloadJson = previous.PayloadJson,
            ContextJson = previous.ContextJson,
            Status = WorkflowExecutionStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now,
            RequestedBy = requestedBy
        }, ct);

        await _queue.EnqueueAsync(new WorkflowQueueItem(tenantId, retry.Id), ct);
        return retry;
    }
}
