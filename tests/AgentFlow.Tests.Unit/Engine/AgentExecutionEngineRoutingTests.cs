using AgentFlow.Abstractions;
using AgentFlow.Abstractions.Workflows;
using AgentFlow.Application.Memory;
using AgentFlow.Core.Engine;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Repositories;
using AgentFlow.Intents.Classification;
using AgentFlow.Intents.Classification.Models;
using AgentFlow.Intents.Inbox;
using AgentFlow.Intents.Inbox.Models;
using AgentFlow.Intents.Routing;
using AgentFlow.Intents.Routing.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Engine;

public sealed class AgentExecutionEngineRoutingTests
{
    [Fact]
    public async Task ExecuteAsync_RouterHighConfidence_RoutesAndTriggersWorkflow_HappyPath()
    {
        var agentRepo = new Mock<IAgentDefinitionRepository>();
        var executionRepo = new Mock<IAgentExecutionRepository>();
        var threadRepo = new Mock<IConversationThreadRepository>();
        var brainResolver = new Mock<IAgentBrainResolver>();
        var toolExecutor = new Mock<IToolExecutor>();
        var policyEngine = new Mock<IPolicyEngine>();
        var eventTransport = new Mock<IAgentEventTransport>();
        var checkpointStore = new Mock<ICheckpointStore>();
        var toolRegistry = new Mock<IToolRegistry>();
        var planner = new Mock<IExecutionPlanner>();
        var intentScoring = new Mock<IIntentScoringEngine>();
        var routingOrchestrator = new Mock<IRoutingOrchestrator>();
        var workflowEngine = new Mock<IWorkflowEngine>();
        var inboxService = new Mock<IConversationInboxService>();

        var audit = new Mock<IAuditMemory>();
        var memory = new Mock<IAgentMemoryService>();
        memory.SetupGet(x => x.Audit).Returns(audit.Object);
        memory.SetupGet(x => x.Working).Returns(Mock.Of<IWorkingMemory>());
        memory.SetupGet(x => x.LongTerm).Returns(Mock.Of<ILongTermMemory>());
        memory.SetupGet(x => x.Vector).Returns(Mock.Of<IVectorMemory>());

        var routerAgent = BuildRouterAgent();
        agentRepo.Setup(x => x.GetByIdAsync("router-agent", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(routerAgent);

        intentScoring.Setup(x => x.ClassifyAsync("quiero comprar", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassificationResult
            {
                BestMatch = new IntentMatch
                {
                    IntentKey = "comprar_producto",
                    DisplayName = "Comprar producto",
                    Category = "sales",
                    SimilarityScore = 0.95,
                    Explanation = "high confidence"
                },
                AllCandidates = new List<ScoredCandidate>(),
                BestScore = 0.95,
                Confidence = ConfidenceLevel.High,
                ConfidenceNumeric = 0.95,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<ConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "comprar_producto",
                WorkflowDefinitionId = "wf-starter-sales",
                TargetAgentId = null,
                Action = RoutingAction.Route,
                ReasonCode = "HighConfidence",
                ExplanationJson = "{}",
                DecidedAt = DateTimeOffset.UtcNow,
                LockId = "lock-1"
            });

        workflowEngine.Setup(x => x.TriggerAsync(
                "wf-starter-sales",
                It.IsAny<WorkflowTriggerContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowExecutionResult
            {
                ExecutionId = "wf-exec-1",
                WorkflowDefinitionId = "wf-starter-sales",
                StartedAt = DateTimeOffset.UtcNow,
                Status = WorkflowExecutionStatus.Running
            });

        var engine = new AgentExecutionEngine(
            agentRepo.Object,
            executionRepo.Object,
            threadRepo.Object,
            brainResolver.Object,
            toolExecutor.Object,
            memory.Object,
            policyEngine.Object,
            eventTransport.Object,
            checkpointStore.Object,
            toolRegistry.Object,
            planner.Object,
            new TokenBudgetService(TokenBudgetConfig.Default),
            NullLogger<AgentExecutionEngine>.Instance,
            intentScoring.Object,
            routingOrchestrator.Object,
            workflowEngine.Object,
            inboxService.Object);

        var result = await engine.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = "tenant-1",
            AgentKey = "router-agent",
            UserId = "user-1",
            UserMessage = "quiero comprar",
            CorrelationId = "corr-1",
            SessionContext = new AgentSessionContext
            {
                SessionId = "sess-1",
                UserIdentifier = "145346172870721@lid",
                ChannelType = "whatsapp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Running, result.Status);
        Assert.Contains("wf-starter-sales", result.FinalResponse);
        workflowEngine.Verify(x => x.TriggerAsync(
            "wf-starter-sales",
            It.Is<WorkflowTriggerContext>(c =>
                c.TenantId == "tenant-1" &&
                c.Channel == "whatsapp" &&
                c.DetectedIntentKey == "comprar_producto"),
            It.IsAny<CancellationToken>()), Times.Once);
        inboxService.Verify(x => x.CreateOrUpdateAsync(It.IsAny<InboxConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_RouterNoMatch_SendsConversationToUnclassified()
    {
        var agentRepo = new Mock<IAgentDefinitionRepository>();
        var executionRepo = new Mock<IAgentExecutionRepository>();
        var threadRepo = new Mock<IConversationThreadRepository>();
        var brainResolver = new Mock<IAgentBrainResolver>();
        var toolExecutor = new Mock<IToolExecutor>();
        var policyEngine = new Mock<IPolicyEngine>();
        var eventTransport = new Mock<IAgentEventTransport>();
        var checkpointStore = new Mock<ICheckpointStore>();
        var toolRegistry = new Mock<IToolRegistry>();
        var planner = new Mock<IExecutionPlanner>();
        var intentScoring = new Mock<IIntentScoringEngine>();
        var routingOrchestrator = new Mock<IRoutingOrchestrator>();
        var workflowEngine = new Mock<IWorkflowEngine>();
        var inboxService = new Mock<IConversationInboxService>();

        var audit = new Mock<IAuditMemory>();
        var memory = new Mock<IAgentMemoryService>();
        memory.SetupGet(x => x.Audit).Returns(audit.Object);
        memory.SetupGet(x => x.Working).Returns(Mock.Of<IWorkingMemory>());
        memory.SetupGet(x => x.LongTerm).Returns(Mock.Of<ILongTermMemory>());
        memory.SetupGet(x => x.Vector).Returns(Mock.Of<IVectorMemory>());

        var routerAgent = BuildRouterAgent();
        agentRepo.Setup(x => x.GetByIdAsync("router-agent", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(routerAgent);

        intentScoring.Setup(x => x.ClassifyAsync("hola", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassificationResult
            {
                BestMatch = null,
                AllCandidates = new List<ScoredCandidate>(),
                BestScore = 0,
                Confidence = ConfidenceLevel.NoMatch,
                ConfidenceNumeric = 0,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<ConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "unknown",
                WorkflowDefinitionId = null,
                TargetAgentId = null,
                Action = RoutingAction.Fallback,
                ReasonCode = "NoMatch",
                ExplanationJson = "{}",
                DecidedAt = DateTimeOffset.UtcNow
            });

        inboxService.Setup(x => x.CreateOrUpdateAsync(It.IsAny<InboxConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InboxConversation c, CancellationToken _) => c);

        var engine = new AgentExecutionEngine(
            agentRepo.Object,
            executionRepo.Object,
            threadRepo.Object,
            brainResolver.Object,
            toolExecutor.Object,
            memory.Object,
            policyEngine.Object,
            eventTransport.Object,
            checkpointStore.Object,
            toolRegistry.Object,
            planner.Object,
            new TokenBudgetService(TokenBudgetConfig.Default),
            NullLogger<AgentExecutionEngine>.Instance,
            intentScoring.Object,
            routingOrchestrator.Object,
            workflowEngine.Object,
            inboxService.Object);

        var result = await engine.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = "tenant-1",
            AgentKey = "router-agent",
            UserId = "user-1",
            UserMessage = "hola",
            CorrelationId = "corr-2",
            SessionContext = new AgentSessionContext
            {
                SessionId = "sess-2",
                UserIdentifier = "145346172870721@lid",
                ChannelType = "whatsapp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Contains("No se pudo identificar", result.FinalResponse);
        workflowEngine.Verify(x => x.TriggerAsync(It.IsAny<string>(), It.IsAny<WorkflowTriggerContext>(), It.IsAny<CancellationToken>()), Times.Never);
        inboxService.Verify(x => x.CreateOrUpdateAsync(
            It.Is<InboxConversation>(c =>
                c.TenantId == "tenant-1" &&
                c.State == ConversationState.NoMatch &&
                c.RequiresHumanReview),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AgentDefinition BuildRouterAgent()
    {
        var create = AgentDefinition.Create(
            tenantId: "tenant-1",
            name: "Router",
            description: "Router agent",
            brain: new BrainConfiguration
            {
                ModelId = "gpt-4o-mini",
                Provider = "OpenAI",
                Temperature = 0.1f,
                MaxResponseTokens = 300,
                RequiresToolExecution = false,
                SystemPromptTemplate = "router"
            },
            loopConfig: new AgentLoopConfig
            {
                MaxIterations = 2,
                MaxExecutionTime = TimeSpan.FromSeconds(30),
                ToolCallTimeout = TimeSpan.FromSeconds(10),
                MaxRetries = 1,
                AllowParallelToolCalls = false,
                HitlConfig = new HumanInTheLoopConfig { Enabled = false }
            },
            memory: new MemoryConfig
            {
                EnableWorkingMemory = true,
                WorkingMemoryTtlSeconds = 600,
                EnableLongTermMemory = false,
                EnableVectorMemory = false
            },
            session: new SessionConfig
            {
                EnableThreads = true,
                DefaultThreadTtl = TimeSpan.FromHours(1),
                MaxTurnsPerThread = 10,
                ContextWindowSize = 5,
                AutoCreateThread = true,
                EnableSummarization = false
            },
            workflowSteps: Array.Empty<WorkflowStep>(),
            ownerUserId: "seed");

        var agent = create.Value!;
        agent.SetSystemRole(AgentSystemRole.Router);
        return agent;
    }
}
