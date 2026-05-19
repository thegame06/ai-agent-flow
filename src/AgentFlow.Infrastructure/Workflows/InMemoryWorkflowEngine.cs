namespace AgentFlow.Infrastructure.Workflows;

using AgentFlow.Abstractions.Workflows;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

/// <summary>
/// Implementación in-memory del Workflow Engine para testing y desarrollo local.
/// En producción se reemplazará por el engine basado en DSL con persistencia MongoDB.
/// </summary>
public sealed class InMemoryWorkflowEngine : IWorkflowEngine
{
    private readonly ILogger<InMemoryWorkflowEngine> _logger;
    private readonly ConcurrentDictionary<string, WorkflowExecutionResult> _executions = new();

    public InMemoryWorkflowEngine(ILogger<InMemoryWorkflowEngine> logger)
    {
        _logger = logger;
    }

    public Task<WorkflowExecutionResult> TriggerAsync(
        string workflowDefinitionId,
        WorkflowTriggerContext context,
        CancellationToken ct = default)
    {
        var executionId = Guid.NewGuid().ToString("N");
        
        var result = new WorkflowExecutionResult
        {
            ExecutionId = executionId,
            WorkflowDefinitionId = workflowDefinitionId,
            StartedAt = DateTimeOffset.UtcNow,
            Status = WorkflowExecutionStatus.Running
        };

        _executions[executionId] = result;

        _logger.LogInformation(
            "Workflow triggered: ExecutionId={ExecutionId}, WorkflowId={WorkflowId}, Intent={Intent}, Confidence={Confidence:P}",
            executionId,
            workflowDefinitionId,
            context.DetectedIntentKey,
            context.ConfidenceScore);

        // Simular ejecución asíncrona
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);

                _executions[executionId] = result with
                {
                    Status = WorkflowExecutionStatus.Completed
                };

                _logger.LogInformation(
                    "Workflow completed: ExecutionId={ExecutionId}",
                    executionId);
            }
            catch (OperationCanceledException)
            {
                _executions[executionId] = result with
                {
                    Status = WorkflowExecutionStatus.Cancelled
                };

                _logger.LogWarning(
                    "Workflow cancelled: ExecutionId={ExecutionId}",
                    executionId);
            }
            catch (Exception ex)
            {
                _executions[executionId] = result with
                {
                    Status = WorkflowExecutionStatus.Failed,
                    ErrorMessage = ex.Message
                };

                _logger.LogError(ex,
                    "Workflow failed: ExecutionId={ExecutionId}",
                    executionId);
            }
        }, ct);

        return Task.FromResult(result);
    }

    public Task<WorkflowExecutionStatus?> GetExecutionStatusAsync(
        string executionId,
        string tenantId,
        CancellationToken ct = default)
    {
        if (_executions.TryGetValue(executionId, out var result))
        {
            return Task.FromResult<WorkflowExecutionStatus?>(result.Status);
        }

        return Task.FromResult<WorkflowExecutionStatus?>(null);
    }

    public Task<bool> CancelExecutionAsync(
        string executionId,
        string tenantId,
        string reason,
        CancellationToken ct = default)
    {
        if (_executions.TryGetValue(executionId, out var result))
        {
            _executions[executionId] = result with
            {
                Status = WorkflowExecutionStatus.Cancelled,
                ErrorMessage = reason
            };

            _logger.LogWarning(
                "Workflow cancellation requested: ExecutionId={ExecutionId}, Reason={Reason}",
                executionId,
                reason);

            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}
