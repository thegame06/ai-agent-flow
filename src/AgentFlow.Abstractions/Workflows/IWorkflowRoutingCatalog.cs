namespace AgentFlow.Abstractions.Workflows;

/// <summary>
/// Exposes published workflows as routing candidates for fallback matching.
/// </summary>
public interface IWorkflowRoutingCatalog
{
    Task<IReadOnlyList<WorkflowRoutingCandidate>> ListPublishedCandidatesAsync(
        string tenantId,
        string channel,
        CancellationToken ct = default);
}

public sealed record WorkflowRoutingCandidate
{
    public string WorkflowDefinitionId { get; init; } = string.Empty;
    public string WorkflowName { get; init; } = string.Empty;
    public string? WorkflowDescription { get; init; }
    public string? TargetAgentId { get; init; }
    public string IntentKey { get; init; } = string.Empty;
    public string? IntentLabel { get; init; }
    public string? IntentDescription { get; init; }
    public IReadOnlyList<string> ExamplePhrases { get; init; } = Array.Empty<string>();
    public double ConfidenceThreshold { get; init; } = 0.7d;
    public string TriggerEventName { get; init; } = string.Empty;
}
