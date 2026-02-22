using AgentFlow.Abstractions;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.ValueObjects;
using MongoDB.Bson.Serialization.Attributes;

namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// AgentExecution - Runtime instance of an AgentDefinition.
/// Captures the full Think → Plan → Act → Observe loop state.
/// Immutable history via Steps list. Never mutate Steps, only append.
/// </summary>
public sealed class AgentExecution : AggregateRoot
{
    public string AgentDefinitionId { get; private set; } = string.Empty;
    public string TriggeredBy { get; private set; } = string.Empty;
    public ExecutionStatus Status { get; private set; } = ExecutionStatus.Pending;
    public string CorrelationId { get; private set; } = string.Empty;

    // --- Input/Output ---
    public ExecutionInput Input { get; private set; } = default!;
    public ExecutionOutput? Output { get; private set; }

    // --- Execution Timeline ---
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int CurrentIteration { get; private set; } = 0;
    public int MaxIterations { get; private set; }

    // --- Step history (append-only, immutable audit trail) ---
    private readonly List<AgentStep> _steps = [];
    public IReadOnlyList<AgentStep> Steps => _steps.AsReadOnly();

    // --- Error tracking ---
    public string? ErrorMessage { get; private set; }
    public string? ErrorCode { get; private set; }
    public int RetryCount { get; private set; } = 0;

    // --- Context propagation ---
    public string? ParentExecutionId { get; private set; }
    public ExecutionPriority Priority { get; private set; } = ExecutionPriority.Normal;

    private AgentExecution() { }

    public static AgentExecution Create(
        string tenantId,
        string agentDefinitionId,
        string triggeredBy,
        ExecutionInput input,
        int maxIterations,
        string correlationId,
        string? parentExecutionId = null,
        ExecutionPriority priority = ExecutionPriority.Normal)
    {
        return new AgentExecution
        {
            TenantId = tenantId,
            AgentDefinitionId = agentDefinitionId,
            TriggeredBy = triggeredBy,
            Input = input,
            MaxIterations = maxIterations,
            CorrelationId = correlationId,
            ParentExecutionId = parentExecutionId,
            Priority = priority,
            CreatedBy = triggeredBy,
            UpdatedBy = triggeredBy
        };
    }

    public Result Start()
    {
        if (Status != ExecutionStatus.Pending)
            return Result.Failure(Error.EngineError($"Cannot start execution in status '{Status}'."));

        Status = ExecutionStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
        MarkUpdated(TriggeredBy);

        AddDomainEvent(new AgentExecutionStartedEvent(Id, TenantId, AgentDefinitionId, CorrelationId));
        return Result.Success();
    }

    public Result AppendStep(AgentStep step)
    {
        if (Status != ExecutionStatus.Running && Status != ExecutionStatus.HumanReviewPending)
            return Result.Failure(Error.EngineError("Cannot append steps to a non-active execution."));

        if (CurrentIteration >= MaxIterations)
        {
            Status = ExecutionStatus.Failed;
            ErrorCode = "Engine.MaxIterationsExceeded";
            ErrorMessage = $"Agent exceeded maximum iterations ({MaxIterations}).";
            AddDomainEvent(new AgentExecutionFailedEvent(Id, TenantId, ErrorCode, ErrorMessage));
            return Result.Failure(Error.EngineError(ErrorMessage));
        }

        _steps.Add(step);

        if (step.StepType == StepType.Think || step.StepType == StepType.Plan)
            CurrentIteration++;

        MarkUpdated(TriggeredBy);
        return Result.Success();
    }

    public Result Complete(ExecutionOutput output)
    {
        if (Status != ExecutionStatus.Running)
            return Result.Failure(Error.EngineError($"Cannot complete execution in status '{Status}'."));

        Status = ExecutionStatus.Completed;
        Output = output;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated(TriggeredBy);

        AddDomainEvent(new AgentExecutionCompletedEvent(Id, TenantId, AgentDefinitionId, CompletedAt.Value));
        return Result.Success();
    }

    public Result Fail(string errorCode, string errorMessage)
    {
        Status = ExecutionStatus.Failed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated(TriggeredBy);

        AddDomainEvent(new AgentExecutionFailedEvent(Id, TenantId, errorCode, errorMessage));
        return Result.Success();
    }

    public Result Cancel(string cancelledBy)
    {
        if (Status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
            return Result.Failure(Error.EngineError("Execution is already in a terminal state."));

        Status = ExecutionStatus.Cancelled;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated(cancelledBy);

        AddDomainEvent(new AgentExecutionCancelledEvent(Id, TenantId, cancelledBy));
        return Result.Success();
    }

    public Result PauseForReview(string reason)
    {
        if (Status != ExecutionStatus.Running)
            return Result.Failure(Error.EngineError($"Cannot pause execution in status '{Status}'."));

        Status = ExecutionStatus.HumanReviewPending;
        MarkUpdated(UpdatedBy);

        AddDomainEvent(new AgentExecutionPausedEvent(Id, TenantId, reason));
        return Result.Success();
    }

    public Result ResumeFromReview(string approvedBy)
    {
        if (Status != ExecutionStatus.HumanReviewPending)
            return Result.Failure(Error.EngineError($"Cannot resume execution in status '{Status}'."));

        Status = ExecutionStatus.Running;
        MarkUpdated(approvedBy);

        AddDomainEvent(new AgentExecutionResumedEvent(Id, TenantId, approvedBy));
        return Result.Success();
    }

    public TimeSpan? GetDuration() =>
        StartedAt.HasValue && CompletedAt.HasValue
            ? CompletedAt.Value - StartedAt.Value
            : null;
}

// Execution-related domain events
public record AgentExecutionStartedEvent(string ExecutionId, string TenantId, string AgentId, string CorrelationId) : DomainEvent;
public record AgentExecutionCompletedEvent(string ExecutionId, string TenantId, string AgentId, DateTimeOffset CompletedAt) : DomainEvent;
public record AgentExecutionFailedEvent(string ExecutionId, string TenantId, string ErrorCode, string ErrorMessage) : DomainEvent;
public record AgentExecutionCancelledEvent(string ExecutionId, string TenantId, string CancelledBy) : DomainEvent;
public record AgentExecutionPausedEvent(string ExecutionId, string TenantId, string Reason) : DomainEvent;
public record AgentExecutionResumedEvent(string ExecutionId, string TenantId, string ApprovedBy) : DomainEvent;
