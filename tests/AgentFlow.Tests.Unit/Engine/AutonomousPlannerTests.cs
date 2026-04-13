using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;
using Microsoft.Extensions.Logging;
using Moq;

namespace AgentFlow.Tests.Unit.Engine;

public sealed class AutonomousPlannerTests
{
    private readonly Mock<IAgentBrain> _brain = new();
    private readonly AutonomousPlanner _planner;

    public AutonomousPlannerTests()
    {
        _planner = new AutonomousPlanner(_brain.Object, Mock.Of<ILogger<AutonomousPlanner>>());
    }

    [Fact]
    public async Task CreatePlan_ValidBrainJson_ReturnsBoundedPlan()
    {
        _brain.Setup(b => b.ThinkAsync(It.IsAny<ThinkContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThinkResult
            {
                Decision = ThinkDecision.ProvideFinalAnswer,
                FinalAnswer = """
                {"steps":[{"description":"Investigate","tool":"search","successCriteria":"facts"},{"description":"Answer","successCriteria":"final"}],"stopCriteria":"Done"}
                """,
                TokensUsed = 120
            });

        var plan = await _planner.CreatePlan(new PlannerCreateContext
        {
            TenantId = "t1",
            ExecutionId = "e1",
            Goal = "Solve task",
            SystemPrompt = "You are planner",
            MaxSteps = 1
        });

        Assert.Single(plan.Steps);
        Assert.Equal("Investigate", plan.Steps[0].Description);
    }

    [Fact]
    public async Task RevisePlan_ToolFailure_ProducesNextRevision()
    {
        _brain.Setup(b => b.ThinkAsync(It.IsAny<ThinkContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThinkResult
            {
                Decision = ThinkDecision.ProvideFinalAnswer,
                FinalAnswer = "{\"steps\":[{\"description\":\"Fallback path\"}]}",
                TokensUsed = 80
            });

        var revised = await _planner.RevisePlan(new PlannerReviseContext
        {
            BaseContext = new PlannerCreateContext
            {
                TenantId = "t1",
                ExecutionId = "e1",
                Goal = "Solve task",
                SystemPrompt = "Planner",
                MaxSteps = 5
            },
            CurrentPlan = new ExecutionPlan
            {
                Revision = 0,
                Goal = "Solve task",
                Steps = [new PlannedExecutionStep { Description = "Old" }]
            },
            FailureReason = "Tool timeout",
            CompletedSteps = 0
        });

        Assert.Equal(1, revised.Revision);
        Assert.Equal("Fallback path", revised.Steps[0].Description);
    }

    [Fact]
    public void NextStep_BudgetExhausted_ReturnsStop()
    {
        var next = _planner.NextStep(new PlannerNextStepContext
        {
            Plan = new ExecutionPlan
            {
                Goal = "x",
                Steps = [new PlannedExecutionStep { Description = "a" }]
            },
            CompletedSteps = 0,
            RemainingTokenBudget = 0,
            MaxSteps = 5
        });

        Assert.True(next.ShouldStop);
        Assert.Equal("Token budget exhausted", next.StopReason);
    }
}
