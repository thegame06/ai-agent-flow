using System.Text.Json;
using AgentFlow.Abstractions.Workflow;
using AgentFlow.Abstractions.Workflows;
using StudioExecutionStatus = AgentFlow.Abstractions.Workflow.WorkflowExecutionStatus;
using EngineExecutionStatus = AgentFlow.Abstractions.Workflows.WorkflowExecutionStatus;

namespace AgentFlow.Api.Workflow;

/// <summary>
/// Workflow engine backed by Workflow Studio store/runtime queue.
/// Used by Router intent routing to trigger real workflow executions.
/// </summary>
public sealed class WorkflowStudioEngine : IWorkflowEngine
{
    private readonly IWorkflowStudioStore _store;
    private readonly IWorkflowExecutionQueue _queue;
    private readonly IWorkflowSecurityPolicyService _policy;

    public WorkflowStudioEngine(
        IWorkflowStudioStore store,
        IWorkflowExecutionQueue queue,
        IWorkflowSecurityPolicyService policy)
    {
        _store = store;
        _queue = queue;
        _policy = policy;
    }

    public async Task<WorkflowExecutionResult> TriggerAsync(
        string workflowDefinitionId,
        WorkflowTriggerContext context,
        CancellationToken ct = default)
    {
        var definition = await _store.GetDefinitionAsync(context.TenantId, workflowDefinitionId, ct);
        if (definition is null)
            throw new InvalidOperationException($"Workflow definition '{workflowDefinitionId}' not found.");
        if (definition.Status != WorkflowDefinitionStatus.Published)
            throw new InvalidOperationException($"Workflow definition '{workflowDefinitionId}' is not published.");

        var payload = new Dictionary<string, object?>
        {
            ["conversationId"] = context.ConversationId,
            ["channel"] = context.Channel,
            ["userIdentifier"] = context.UserIdentifier,
            ["userMessage"] = context.UserMessage,
            ["detectedIntent"] = context.DetectedIntentKey,
            ["confidenceScore"] = context.ConfidenceScore
        };

        if (context.AdditionalMetadata is not null)
        {
            foreach (var kv in context.AdditionalMetadata)
                payload[kv.Key] = kv.Value;
        }

        _policy.ValidatePayloadOrThrow(payload);

        var now = DateTimeOffset.UtcNow;
        var execution = await _store.CreateExecutionAsync(new WorkflowExecutionContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = context.TenantId,
            WorkflowDefinitionId = definition.Id,
            TriggerEventName = string.IsNullOrWhiteSpace(definition.TriggerEventName)
                ? "intent.router"
                : definition.TriggerEventName,
            CorrelationId = string.IsNullOrWhiteSpace(context.ConversationId)
                ? Guid.NewGuid().ToString("N")
                : context.ConversationId,
            PayloadJson = JsonSerializer.Serialize(payload),
            ContextJson = "{}",
            Status = StudioExecutionStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now,
            RequestedBy = string.IsNullOrWhiteSpace(context.UserIdentifier)
                ? "router-agent"
                : context.UserIdentifier
        }, ct);

        await _queue.EnqueueAsync(new WorkflowQueueItem(context.TenantId, execution.Id), ct);

        return new WorkflowExecutionResult
        {
            ExecutionId = execution.Id,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            StartedAt = execution.CreatedAt,
            Status = MapStatus(execution.Status)
        };
    }

    public async Task<EngineExecutionStatus?> GetExecutionStatusAsync(
        string executionId,
        string tenantId,
        CancellationToken ct = default)
    {
        var execution = (await _store.GetExecutionsAsync(tenantId, 500, ct))
            .FirstOrDefault(x => x.Id == executionId);
        return execution is null ? null : MapStatus(execution.Status);
    }

    public async Task<bool> CancelExecutionAsync(
        string executionId,
        string tenantId,
        string reason,
        CancellationToken ct = default)
    {
        var updated = await _store.UpdateExecutionStatusAsync(
            tenantId,
            executionId,
            StudioExecutionStatus.Failed,
            reason,
            ct);
        return updated is not null;
    }

    private static EngineExecutionStatus MapStatus(StudioExecutionStatus status)
        => status switch
        {
            StudioExecutionStatus.Queued => EngineExecutionStatus.Pending,
            StudioExecutionStatus.Running => EngineExecutionStatus.Running,
            StudioExecutionStatus.Completed => EngineExecutionStatus.Completed,
            StudioExecutionStatus.Failed => EngineExecutionStatus.Failed,
            _ => EngineExecutionStatus.Failed
        };
}
