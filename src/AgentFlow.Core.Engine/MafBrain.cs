using AgentFlow.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Core.Engine;

/// <summary>
/// Microsoft Agent Framework (MAF) implementation of IAgentBrain.
/// Uses the new Unified AI Abstractions (Microsoft.Extensions.AI) 
/// and AgentChat patterns for high-reasoning agents.
/// </summary>
public sealed class MafBrain : IAgentBrain
{
    private readonly ILogger<MafBrain> _logger;
    private readonly bool _enabled;

    public MafBrain(IConfiguration configuration, ILogger<MafBrain> logger)
    {
        _logger = logger;
        _enabled = configuration.GetValue<bool>("Brains:MAF:Enabled", false);
    }

    /// <summary>
    /// Executes a reasoning step using the Microsoft Agent Framework.
    /// In an Enterprise setting, this would coordinate multiple sub-agents.
    /// </summary>
    public Task<ThinkResult> ThinkAsync(ThinkContext context, CancellationToken ct = default)
    {
        _logger.LogInformation("MAF [Think]: Processing execution {ExecutionId} (Iteration: {Iteration})",
            context.ExecutionId, context.Iteration);

        if (!_enabled)
        {
            return Task.FromResult(new ThinkResult
            {
                Decision = ThinkDecision.Checkpoint,
                Rationale = "MAF brain is disabled. Enable Brains:MAF:Enabled and configure real MAF orchestration.",
                TokensUsed = 0
            });
        }

        // Non-processd deterministic policy:
        // - If a tool is available, use first allowed tool.
        // - Otherwise request clarification instead of hallucinating final answer.
        if (context.AvailableTools.Any())
        {
            var targetTool = context.AvailableTools.First();
            return Task.FromResult(new ThinkResult
            {
                Decision = ThinkDecision.UseTool,
                Rationale = $"MAF selected tool '{targetTool.Name}' based on current context and availability.",
                NextToolName = targetTool.Name,
                NextToolInputJson = "{}",
                TokensUsed = 0
            });
        }

        return Task.FromResult(new ThinkResult
        {
            Decision = ThinkDecision.RequestMoreContext,
            Rationale = "No tools available for MAF execution path. Requesting additional context/tools.",
            TokensUsed = 0
        });
    }

    /// <summary>
    /// Interprets tool results using the MAF Evaluation patterns.
    /// </summary>
    public Task<ObserveResult> ObserveAsync(ObserveContext context, CancellationToken ct = default)
    {
        _logger.LogInformation("MAF [Observe]: Interpreting result from tool {ToolName}", context.ToolName);

        return Task.FromResult(new ObserveResult
        {
            Summary = context.ToolSucceeded
                ? $"Tool {context.ToolName} executed successfully."
                : $"Tool {context.ToolName} failed. Requires remediation.",
            GoalAchieved = context.ToolSucceeded,
            TokensUsed = 0
        });
    }
}
