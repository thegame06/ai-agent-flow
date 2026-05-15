using AgentFlow.Abstractions;

namespace AgentFlow.Core.Engine;

/// <summary>
/// Legacy Semantic Kernel brain placeholder.
/// Microsoft Agent Framework is the supported runtime for new executions.
/// </summary>
public sealed class SemanticKernelBrain : IAgentBrain
{
    public Task<ThinkResult> ThinkAsync(ThinkContext context, CancellationToken ct = default)
    {
        return Task.FromResult(new ThinkResult
        {
            Decision = ThinkDecision.Checkpoint,
            Rationale = "SemanticKernelBrain is legacy and disabled. Use MicrosoftAgentFramework as AgentBrain:DefaultProvider.",
            TokensUsed = 0
        });
    }

    public Task<ObserveResult> ObserveAsync(ObserveContext context, CancellationToken ct = default)
    {
        return Task.FromResult(new ObserveResult
        {
            Summary = "SemanticKernelBrain is legacy and disabled. Use MicrosoftAgentFramework as AgentBrain:DefaultProvider.",
            GoalAchieved = false,
            TokensUsed = 0
        });
    }
}
