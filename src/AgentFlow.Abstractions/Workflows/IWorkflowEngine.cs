namespace AgentFlow.Abstractions.Workflows;

/// <summary>
/// Engine para ejecutar workflows definidos por el usuario.
/// Los workflows pueden ser disparados por el Router Agent basado en clasificación de intenciones.
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Dispara un workflow específico con contexto conversacional.
    /// </summary>
    /// <param name="workflowDefinitionId">ID del workflow definido (ej: "loan-officer-v1")</param>
    /// <param name="context">Contexto de la conversación que originó el trigger</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>ID de ejecución del workflow para tracking</returns>
    Task<WorkflowExecutionResult> TriggerAsync(
        string workflowDefinitionId,
        WorkflowTriggerContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Obtiene el estado de una ejecución de workflow en progreso.
    /// </summary>
    Task<WorkflowExecutionStatus?> GetExecutionStatusAsync(
        string executionId,
        string tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Cancela una ejecución de workflow en progreso.
    /// </summary>
    Task<bool> CancelExecutionAsync(
        string executionId,
        string tenantId,
        string reason,
        CancellationToken ct = default);
}

public sealed record WorkflowTriggerContext
{
    public required string TenantId { get; init; }
    public required string ConversationId { get; init; }
    public required string Channel { get; init; }
    public required string UserIdentifier { get; init; }
    public required string UserMessage { get; init; }
    public required string DetectedIntentKey { get; init; }
    public required double ConfidenceScore { get; init; }
    public Dictionary<string, object>? AdditionalMetadata { get; init; }
}

public sealed record WorkflowExecutionResult
{
    public required string ExecutionId { get; init; }
    public required string WorkflowDefinitionId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required WorkflowExecutionStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
}

public enum WorkflowExecutionStatus
{
    Pending,      // Encolado pero no iniciado
    Running,      // En ejecución
    Paused,       // Pausado (esperando input externo)
    Completed,    // Completado exitosamente
    Failed,       // Falló con error
    Cancelled,    // Cancelado manualmente
    Timeout       // Timeout alcanzado
}
