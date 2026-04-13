using System.Text.Json;
using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Core.Engine;

public sealed class AutonomousPlanner : IExecutionPlanner
{
    private readonly IAgentBrain _brain;
    private readonly ILogger<AutonomousPlanner> _logger;

    public AutonomousPlanner(IAgentBrain brain, ILogger<AutonomousPlanner> logger)
    {
        _brain = brain;
        _logger = logger;
    }

    public async Task<ExecutionPlan> CreatePlan(PlannerCreateContext context, CancellationToken ct = default)
    {
        var requestedSteps = Math.Clamp(context.MaxSteps, 1, 25);
        var plan = await BuildPlanFromBrainAsync(context, $"Create an executable plan with up to {requestedSteps} steps.", 0, ct);
        return ApplyGuardrails(plan, requestedSteps);
    }

    public async Task<ExecutionPlan> RevisePlan(PlannerReviseContext context, CancellationToken ct = default)
    {
        var prompt = $"Revise the plan after failure. Failure: {context.FailureReason}. CompletedSteps: {context.CompletedSteps}.";
        var revised = await BuildPlanFromBrainAsync(context.BaseContext, prompt, context.CurrentPlan.Revision + 1, ct);
        return ApplyGuardrails(revised, context.BaseContext.MaxSteps);
    }

    public PlanNextStepResult NextStep(PlannerNextStepContext context)
    {
        if (context.RemainingTokenBudget <= 0)
            return new PlanNextStepResult { ShouldStop = true, StopReason = "Token budget exhausted" };

        if (context.CompletedSteps >= context.MaxSteps)
            return new PlanNextStepResult { ShouldStop = true, StopReason = "Maximum planned steps reached" };

        if (context.CompletedSteps >= context.Plan.Steps.Count)
            return new PlanNextStepResult { ShouldStop = true, StopReason = context.Plan.StopCriteria ?? "Plan completed" };

        return new PlanNextStepResult
        {
            ShouldStop = false,
            Step = context.Plan.Steps[context.CompletedSteps]
        };
    }

    private async Task<ExecutionPlan> BuildPlanFromBrainAsync(PlannerCreateContext context, string plannerInstruction, int revision, CancellationToken ct)
    {
        var think = await _brain.ThinkAsync(new ThinkContext
        {
            TenantId = context.TenantId,
            ExecutionId = context.ExecutionId,
            SystemPrompt = context.SystemPrompt,
            UserMessage = $"{plannerInstruction}\nGoal: {context.Goal}\nReturn JSON: {{\"steps\":[{{\"description\":\"...\",\"tool\":\"...\",\"successCriteria\":\"...\"}}],\"stopCriteria\":\"...\"}}",
            Iteration = revision,
            AvailableTools = context.AvailableTools
        }, ct);

        try
        {
            var json = think.FinalAnswer ?? think.Rationale ?? "{}";
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var steps = new List<PlannedExecutionStep>();
            if (root.TryGetProperty("steps", out var rawSteps) && rawSteps.ValueKind == JsonValueKind.Array)
            {
                foreach (var step in rawSteps.EnumerateArray())
                {
                    var description = step.TryGetProperty("description", out var d) ? d.GetString() : null;
                    if (string.IsNullOrWhiteSpace(description))
                        continue;

                    steps.Add(new PlannedExecutionStep
                    {
                        Description = description!,
                        SuggestedToolName = step.TryGetProperty("tool", out var t) ? t.GetString() : null,
                        SuccessCriteria = step.TryGetProperty("successCriteria", out var sc) ? sc.GetString() : null
                    });
                }
            }

            return new ExecutionPlan
            {
                Revision = revision,
                Goal = context.Goal,
                Steps = steps,
                StopCriteria = root.TryGetProperty("stopCriteria", out var stop) ? stop.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Planner JSON parsing failed. Using fallback one-step plan for execution {ExecutionId}", context.ExecutionId);
            return new ExecutionPlan
            {
                Revision = revision,
                Goal = context.Goal,
                Steps = [new PlannedExecutionStep { Description = context.Goal, SuccessCriteria = "Deliver final answer" }],
                StopCriteria = "Final answer delivered"
            };
        }
    }

    private static ExecutionPlan ApplyGuardrails(ExecutionPlan plan, int maxSteps)
    {
        var bounded = plan.Steps.Take(Math.Max(1, maxSteps)).ToList();
        if (bounded.Count == 0)
        {
            bounded.Add(new PlannedExecutionStep
            {
                Description = plan.Goal,
                SuccessCriteria = "Deliver final answer"
            });
        }

        return plan with { Steps = bounded };
    }
}
