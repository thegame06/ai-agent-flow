using AgentFlow.Abstractions;
using AgentFlow.Abstractions.Workflows;
using AgentFlow.Application.Memory;
using AgentFlow.Core.Engine;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Repositories;
using AgentFlow.Domain.ValueObjects;
using AgentFlow.Intents.Classification;
using AgentFlow.Intents.Classification.Models;
using AgentFlow.Intents.Inbox;
using AgentFlow.Intents.Inbox.Models;
using AgentFlow.Intents.Routing;
using AgentFlow.Intents.Routing.Models;
using AgentFlow.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RoutingConversationContext = AgentFlow.Intents.Routing.Models.ConversationContext;

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

        var rule = CreateRule("comprar_producto", "wf-starter-sales", "sales-agent", 100);
        intentScoring.Setup(x => x.ClassifyAsync("quiero comprar", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassificationResult
            {
                Message = "quiero comprar",
                BestMatch = new IntentMatch { IntentKey = "comprar_producto", SimilarityScore = 0.95f, MatchedVia = "semantic", Rule = rule },
                AllCandidates = new List<IntentMatch>(),
                BestScore = 0.95f,
                Confidence = ConfidenceLevel.High,
                RequiresHumanReview = false,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<RoutingConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "comprar_producto",
                WorkflowDefinitionId = "wf-starter-sales",
                TargetAgentId = "sales-agent",
                Action = RoutingAction.Route,
                ReasonCode = "matched",
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
            governancePolicy: null,
            intentScoringEngine: intentScoring.Object,
            routingOrchestrator: routingOrchestrator.Object,
            workflowEngine: workflowEngine.Object,
            conversationInboxService: inboxService.Object);

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
                ChannelType = "WhatsApp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            },
            Metadata = new Dictionary<string, string>
            {
                ["routing.intent_confidence_threshold"] = "0.70",
                ["routing.assistant_confidence_threshold"] = "0.80"
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Running, result.Status);
        workflowEngine.Verify(x => x.TriggerAsync(
            "wf-starter-sales",
            It.IsAny<WorkflowTriggerContext>(),
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
                Message = "hola",
                BestMatch = null,
                AllCandidates = new List<IntentMatch>(),
                BestScore = 0f,
                Confidence = ConfidenceLevel.NoMatch,
                RequiresHumanReview = true,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<RoutingConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "unknown",
                WorkflowDefinitionId = null,
                TargetAgentId = null,
                Action = RoutingAction.Fallback,
                ReasonCode = "no_rules_configured",
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
            governancePolicy: null,
            intentScoringEngine: intentScoring.Object,
            routingOrchestrator: routingOrchestrator.Object,
            workflowEngine: workflowEngine.Object,
            conversationInboxService: inboxService.Object);

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
                ChannelType = "WhatsApp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            },
            Metadata = new Dictionary<string, string>
            {
                ["routing.intent_confidence_threshold"] = "0.70",
                ["routing.assistant_confidence_threshold"] = "0.80"
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        workflowEngine.Verify(x => x.TriggerAsync(It.IsAny<string>(), It.IsAny<WorkflowTriggerContext>(), It.IsAny<CancellationToken>()), Times.Never);
        inboxService.Verify(x => x.CreateOrUpdateAsync(
            It.Is<InboxConversation>(c => c.State == ConversationState.NoMatch && c.RequiresHumanReview),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RouterNoMatch_DuringAccumulation_SuppressesReplyAndKeepsAwaitingClassification()
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

        agentRepo.Setup(x => x.GetByIdAsync("router-agent", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRouterAgent());

        intentScoring.Setup(x => x.ClassifyAsync("Hola\nMe pueden dar informacion?\n...", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassificationResult
            {
                Message = "Hola\nMe pueden dar informacion?\n...",
                BestMatch = null,
                AllCandidates = new List<IntentMatch>(),
                BestScore = 0f,
                Confidence = ConfidenceLevel.NoMatch,
                RequiresHumanReview = true,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<RoutingConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "unknown",
                WorkflowDefinitionId = null,
                TargetAgentId = null,
                Action = RoutingAction.Fallback,
                ReasonCode = "no_rules_configured",
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
            governancePolicy: null,
            intentScoringEngine: intentScoring.Object,
            routingOrchestrator: routingOrchestrator.Object,
            workflowEngine: workflowEngine.Object,
            conversationInboxService: inboxService.Object);

        var result = await engine.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = "tenant-1",
            AgentKey = "router-agent",
            UserId = "user-1",
            UserMessage = "Hola\nMe pueden dar informacion?\n...",
            CorrelationId = "corr-acc",
            SessionContext = new AgentSessionContext
            {
                SessionId = "sess-acc",
                UserIdentifier = "145346172870721@lid",
                ChannelType = "WhatsApp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            },
            Metadata = new Dictionary<string, string>
            {
                ["routing.accumulation_active"] = "true",
                ["routing.suppress_replies_while_accumulating"] = "true",
                ["routing.min_messages_before_classification"] = "3",
                ["routing.inbound_message_count"] = "3",
                ["routing.max_unclassified_messages_before_escalation"] = "4",
                ["channel.latest_user_message"] = "..."
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Contains("\"suppressCustomerReply\":true", result.FinalResponse);
        inboxService.Verify(x => x.CreateOrUpdateAsync(
            It.Is<InboxConversation>(c => c.State == ConversationState.AwaitingClassification && !c.RequiresHumanReview),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RouterNoMatch_OnFirstMessage_SuppressesReplyUntilMinimumContext()
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

        agentRepo.Setup(x => x.GetByIdAsync("router-agent", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRouterAgent());

        intentScoring.Setup(x => x.ClassifyAsync("hola", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassificationResult
            {
                Message = "hola",
                BestMatch = null,
                AllCandidates = new List<IntentMatch>(),
                BestScore = 0f,
                Confidence = ConfidenceLevel.NoMatch,
                RequiresHumanReview = true,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<RoutingConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "unknown",
                WorkflowDefinitionId = null,
                TargetAgentId = null,
                Action = RoutingAction.Fallback,
                ReasonCode = "no_rules_configured",
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
            governancePolicy: null,
            intentScoringEngine: intentScoring.Object,
            routingOrchestrator: routingOrchestrator.Object,
            workflowEngine: workflowEngine.Object,
            conversationInboxService: inboxService.Object);

        var result = await engine.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = "tenant-1",
            AgentKey = "router-agent",
            UserId = "user-1",
            UserMessage = "hola",
            CorrelationId = "corr-first",
            SessionContext = new AgentSessionContext
            {
                SessionId = "sess-first",
                UserIdentifier = "145346172870721@lid",
                ChannelType = "WhatsApp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            },
            Metadata = new Dictionary<string, string>
            {
                ["routing.accumulation_active"] = "true",
                ["routing.suppress_replies_while_accumulating"] = "true",
                ["routing.min_messages_before_classification"] = "3",
                ["routing.inbound_message_count"] = "1",
                ["routing.max_unclassified_messages_before_escalation"] = "4",
                ["channel.latest_user_message"] = "hola"
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Contains("\"suppressCustomerReply\":true", result.FinalResponse);
        Assert.Contains("\"reasonCode\":\"awaiting_minimum_context\"", result.FinalResponse);
        inboxService.Verify(x => x.CreateOrUpdateAsync(
            It.Is<InboxConversation>(c => c.State == ConversationState.AwaitingClassification && !c.RequiresHumanReview),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RouterNoMatch_OnFirstLowSignalMessage_StillAccumulatesContext()
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

        agentRepo.Setup(x => x.GetByIdAsync("router-agent", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRouterAgent());

        intentScoring.Setup(x => x.ClassifyAsync("Hahaha", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassificationResult
            {
                Message = "Hahaha",
                BestMatch = null,
                AllCandidates = new List<IntentMatch>(),
                BestScore = 0f,
                Confidence = ConfidenceLevel.NoMatch,
                RequiresHumanReview = true,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<RoutingConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "unknown",
                WorkflowDefinitionId = null,
                TargetAgentId = null,
                Action = RoutingAction.Fallback,
                ReasonCode = "no_rules_configured",
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
            governancePolicy: null,
            intentScoringEngine: intentScoring.Object,
            routingOrchestrator: routingOrchestrator.Object,
            workflowEngine: workflowEngine.Object,
            conversationInboxService: inboxService.Object);

        var result = await engine.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = "tenant-1",
            AgentKey = "router-agent",
            UserId = "user-1",
            UserMessage = "Hahaha",
            CorrelationId = "corr-low-signal",
            SessionContext = new AgentSessionContext
            {
                SessionId = "sess-low-signal",
                UserIdentifier = "145346172870721@lid",
                ChannelType = "WhatsApp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            },
            Metadata = new Dictionary<string, string>
            {
                ["routing.accumulation_active"] = "true",
                ["routing.suppress_replies_while_accumulating"] = "true",
                ["routing.min_messages_before_classification"] = "3",
                ["routing.inbound_message_count"] = "1",
                ["routing.max_unclassified_messages_before_escalation"] = "4",
                ["channel.latest_user_message"] = "Hahaha"
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Contains("\"suppressCustomerReply\":true", result.FinalResponse);
        Assert.Contains("\"reasonCode\":\"awaiting_minimum_context\"", result.FinalResponse);
        Assert.DoesNotContain("revision para seguimiento", result.FinalResponse, StringComparison.OrdinalIgnoreCase);
        inboxService.Verify(x => x.CreateOrUpdateAsync(
            It.Is<InboxConversation>(c => c.State == ConversationState.AwaitingClassification && !c.RequiresHumanReview),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RouterNoMatch_WithEscalationTarget_NotifiesQueue()
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
        var escalationNotifier = new Mock<IHumanEscalationNotifier>();

        var audit = new Mock<IAuditMemory>();
        var memory = new Mock<IAgentMemoryService>();
        memory.SetupGet(x => x.Audit).Returns(audit.Object);
        memory.SetupGet(x => x.Working).Returns(Mock.Of<IWorkingMemory>());
        memory.SetupGet(x => x.LongTerm).Returns(Mock.Of<ILongTermMemory>());
        memory.SetupGet(x => x.Vector).Returns(Mock.Of<IVectorMemory>());

        agentRepo.Setup(x => x.GetByIdAsync("router-agent", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRouterAgent());

        intentScoring.Setup(x => x.ClassifyAsync("hola", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassificationResult
            {
                Message = "hola",
                BestMatch = null,
                AllCandidates = new List<IntentMatch>(),
                BestScore = 0f,
                Confidence = ConfidenceLevel.NoMatch,
                RequiresHumanReview = true,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<RoutingConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "unknown",
                WorkflowDefinitionId = null,
                TargetAgentId = null,
                Action = RoutingAction.Fallback,
                ReasonCode = "no_rules_configured",
                ExplanationJson = "{}",
                DecidedAt = DateTimeOffset.UtcNow
            });

        inboxService.Setup(x => x.CreateOrUpdateAsync(It.IsAny<InboxConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InboxConversation c, CancellationToken _) => c);

        escalationNotifier.Setup(x => x.NotifyAsync(
                It.IsAny<HumanEscalationNotificationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanEscalationNotificationResult
            {
                Delivered = true,
                QueueId = "ventas-n1",
                QueueName = "Ventas N1",
                ActiveMembers = 3,
                TicketId = "esc-1"
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
            governancePolicy: null,
            intentScoringEngine: intentScoring.Object,
            routingOrchestrator: routingOrchestrator.Object,
            workflowEngine: workflowEngine.Object,
            conversationInboxService: inboxService.Object,
            humanEscalationNotifier: escalationNotifier.Object);

        await engine.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = "tenant-1",
            AgentKey = "router-agent",
            UserId = "user-1",
            UserMessage = "hola",
            CorrelationId = "corr-3",
            SessionContext = new AgentSessionContext
            {
                SessionId = "sess-3",
                UserIdentifier = "145346172870721@lid",
                ChannelType = "WhatsApp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            },
            Metadata = new Dictionary<string, string>
            {
                ["routing.no_match_action"] = "human_review_only",
                ["routing.fallback_escalation_target"] = "ventas-n1"
            }
        }, CancellationToken.None);

        escalationNotifier.Verify(x => x.NotifyAsync(
            It.Is<HumanEscalationNotificationRequest>(r =>
                r.TenantId == "tenant-1" &&
                r.QueueId == "ventas-n1" &&
                r.ConversationId == "sess-3"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RouterNoMatch_WhenEscalationNotificationFails_DoesNotRunStandardLoop()
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
        var escalationNotifier = new Mock<IHumanEscalationNotifier>();

        var audit = new Mock<IAuditMemory>();
        var memory = new Mock<IAgentMemoryService>();
        memory.SetupGet(x => x.Audit).Returns(audit.Object);
        memory.SetupGet(x => x.Working).Returns(Mock.Of<IWorkingMemory>());
        memory.SetupGet(x => x.LongTerm).Returns(Mock.Of<ILongTermMemory>());
        memory.SetupGet(x => x.Vector).Returns(Mock.Of<IVectorMemory>());

        agentRepo.Setup(x => x.GetByIdAsync("router-agent", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRouterAgent());

        intentScoring.Setup(x => x.ClassifyAsync("Cccccc", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassificationResult
            {
                Message = "Cccccc",
                BestMatch = null,
                AllCandidates = new List<IntentMatch>(),
                BestScore = 0f,
                Confidence = ConfidenceLevel.NoMatch,
                RequiresHumanReview = true,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<RoutingConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "unknown",
                WorkflowDefinitionId = null,
                TargetAgentId = null,
                Action = RoutingAction.Fallback,
                ReasonCode = "no_rules_configured",
                ExplanationJson = "{}",
                DecidedAt = DateTimeOffset.UtcNow
            });

        inboxService.Setup(x => x.CreateOrUpdateAsync(It.IsAny<InboxConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InboxConversation c, CancellationToken _) => c);

        escalationNotifier.Setup(x => x.NotifyAsync(
                It.IsAny<HumanEscalationNotificationRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("queue unavailable"));

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
            governancePolicy: null,
            intentScoringEngine: intentScoring.Object,
            routingOrchestrator: routingOrchestrator.Object,
            workflowEngine: workflowEngine.Object,
            conversationInboxService: inboxService.Object,
            humanEscalationNotifier: escalationNotifier.Object);

        var result = await engine.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = "tenant-1",
            AgentKey = "router-agent",
            UserId = "user-1",
            UserMessage = "Cccccc",
            CorrelationId = "corr-escalation-fails",
            SessionContext = new AgentSessionContext
            {
                SessionId = "sess-escalation-fails",
                UserIdentifier = "145346172870721@lid",
                ChannelType = "WhatsApp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            },
            Metadata = new Dictionary<string, string>
            {
                ["routing.no_match_action"] = "clarify_then_route",
                ["routing.fallback_escalation_target"] = "ventas-n1"
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Contains("\"state\":\"escalated_human\"", result.FinalResponse);
        Assert.Equal(0, result.TotalTokensUsed);
        executionRepo.Verify(x => x.InsertAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()), Times.Never);
        audit.Verify(x => x.RecordAsync(
            It.Is<AuditEntry>(a => a.EventJson.Contains("fallback.escalation_notification_failed")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RouterIntentRoutingException_FailsClosedWithoutStandardLoop()
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

        var audit = new Mock<IAuditMemory>();
        var memory = new Mock<IAgentMemoryService>();
        memory.SetupGet(x => x.Audit).Returns(audit.Object);
        memory.SetupGet(x => x.Working).Returns(Mock.Of<IWorkingMemory>());
        memory.SetupGet(x => x.LongTerm).Returns(Mock.Of<ILongTermMemory>());
        memory.SetupGet(x => x.Vector).Returns(Mock.Of<IVectorMemory>());

        agentRepo.Setup(x => x.GetByIdAsync("router-agent", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRouterAgent());

        intentScoring.Setup(x => x.ClassifyAsync("?", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("classifier unavailable"));

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
            governancePolicy: null,
            intentScoringEngine: intentScoring.Object,
            routingOrchestrator: routingOrchestrator.Object);

        var result = await engine.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = "tenant-1",
            AgentKey = "router-agent",
            UserId = "user-1",
            UserMessage = "?",
            CorrelationId = "corr-routing-fails",
            SessionContext = new AgentSessionContext
            {
                SessionId = "sess-routing-fails",
                UserIdentifier = "145346172870721@lid",
                ChannelType = "WhatsApp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            },
            Metadata = new Dictionary<string, string>
            {
                ["routing.is_router_agent"] = "true",
                ["routing.fallback_escalation_target"] = "ventas-n1"
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Contains("\"reasonCode\":\"intent_routing_exception\"", result.FinalResponse);
        Assert.Equal(0, result.TotalTokensUsed);
        executionRepo.Verify(x => x.InsertAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()), Times.Never);
        audit.Verify(x => x.RecordAsync(
            It.Is<AuditEntry>(a => a.EventJson.Contains("routing.fail_closed")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ChannelMarkedRouter_UsesRoutingPath_EvenWithoutRouterSystemRole()
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

        agentRepo.Setup(x => x.GetByIdAsync("router-agent", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildStandardAgent());

        intentScoring.Setup(x => x.ClassifyAsync("zzzzzz", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassificationResult
            {
                Message = "zzzzzz",
                BestMatch = null,
                AllCandidates = new List<IntentMatch>(),
                BestScore = 0f,
                Confidence = ConfidenceLevel.NoMatch,
                RequiresHumanReview = true,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<RoutingConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "unknown",
                WorkflowDefinitionId = null,
                TargetAgentId = null,
                Action = RoutingAction.Fallback,
                ReasonCode = "no_match",
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
            governancePolicy: null,
            intentScoringEngine: intentScoring.Object,
            routingOrchestrator: routingOrchestrator.Object,
            workflowEngine: workflowEngine.Object,
            conversationInboxService: inboxService.Object);

        var result = await engine.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = "tenant-1",
            AgentKey = "router-agent",
            UserId = "user-1",
            UserMessage = "zzzzzz",
            CorrelationId = "corr-4",
            SessionContext = new AgentSessionContext
            {
                SessionId = "sess-4",
                UserIdentifier = "145346172870721@lid",
                ChannelType = "WhatsApp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            },
            Metadata = new Dictionary<string, string>
            {
                ["routing.is_router_agent"] = "true"
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Contains("routing_fallback", result.FinalResponse);
        inboxService.Verify(x => x.CreateOrUpdateAsync(It.IsAny<InboxConversation>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RouterLowConfidence_WithClarifyThenRoute_UsesConfiguredFallbackQuestion()
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

        agentRepo.Setup(x => x.GetByIdAsync("router-agent", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRouterAgent());

        var rule = CreateRule("consulta_general", "wf-support", "support-agent", 10);
        intentScoring.Setup(x => x.ClassifyAsync("ayuda", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassificationResult
            {
                Message = "ayuda",
                BestMatch = new IntentMatch { IntentKey = "consulta_general", SimilarityScore = 0.55f, MatchedVia = "semantic", Rule = rule },
                AllCandidates = new List<IntentMatch>(),
                BestScore = 0.55f,
                Confidence = ConfidenceLevel.Low,
                RequiresHumanReview = true,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<RoutingConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "consulta_general",
                WorkflowDefinitionId = "wf-support",
                TargetAgentId = "support-agent",
                Action = RoutingAction.Queue,
                ReasonCode = "low_confidence",
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
            governancePolicy: null,
            intentScoringEngine: intentScoring.Object,
            routingOrchestrator: routingOrchestrator.Object,
            workflowEngine: workflowEngine.Object,
            conversationInboxService: inboxService.Object);

        var result = await engine.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = "tenant-1",
            AgentKey = "router-agent",
            UserId = "user-1",
            UserMessage = "ayuda",
            CorrelationId = "corr-5",
            SessionContext = new AgentSessionContext
            {
                SessionId = "sess-5",
                UserIdentifier = "145346172870721@lid",
                ChannelType = "WhatsApp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            },
            Metadata = new Dictionary<string, string>
            {
                ["routing.no_match_action"] = "clarify_then_route",
                ["routing.fallback_questions_json"] = """[{"text":"¿Qué trámite necesitas realizar?","field":"intent","required":true,"active":true}]"""
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Contains("\"customerMessage\":\"", result.FinalResponse);
        Assert.Contains("\"state\":\"clarifying\"", result.FinalResponse);
    }

    [Fact]
    public async Task ExecuteAsync_RouterLowSignalSpam_SkipsClarificationAndEscalates()
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

        agentRepo.Setup(x => x.GetByIdAsync("router-agent", "tenant-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRouterAgent());

        intentScoring.Setup(x => x.ClassifyAsync("nanananana", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassificationResult
            {
                Message = "nanananana",
                BestMatch = null,
                AllCandidates = new List<IntentMatch>(),
                BestScore = 0.10f,
                Confidence = ConfidenceLevel.NoMatch,
                RequiresHumanReview = true,
                ExplanationJson = "{}"
            });

        routingOrchestrator.Setup(x => x.RouteMessageAsync(
                It.IsAny<IntentClassificationResult>(),
                It.IsAny<RoutingConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingDecision
            {
                IntentKey = "unknown",
                WorkflowDefinitionId = null,
                TargetAgentId = null,
                Action = RoutingAction.Fallback,
                ReasonCode = "no_match",
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
            governancePolicy: null,
            intentScoringEngine: intentScoring.Object,
            routingOrchestrator: routingOrchestrator.Object,
            workflowEngine: workflowEngine.Object,
            conversationInboxService: inboxService.Object);

        var result = await engine.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = "tenant-1",
            AgentKey = "router-agent",
            UserId = "user-1",
            UserMessage = "nanananana",
            CorrelationId = "corr-6",
            SessionContext = new AgentSessionContext
            {
                SessionId = "sess-6",
                UserIdentifier = "145346172870721@lid",
                ChannelType = "WhatsApp",
                ChannelId = "ch-1",
                IsWindowOpen = true,
                WindowHours = 24
            },
            Metadata = new Dictionary<string, string>
            {
                ["routing.no_match_action"] = "clarify_then_route",
                ["routing.fallback_questions_json"] = """[{"text":"¿Qué trámite necesitas realizar?","field":"intent","required":true,"active":true}]"""
            }
        }, CancellationToken.None);

        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Contains("\"state\":\"escalated_human\"", result.FinalResponse);
        Assert.DoesNotContain("¿Qué trámite necesitas realizar?", result.FinalResponse);
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

    private static AgentDefinition BuildStandardAgent()
    {
        var create = AgentDefinition.Create(
            tenantId: "tenant-1",
            name: "Standard",
            description: "Standard assistant",
            brain: new BrainConfiguration
            {
                ModelId = "gpt-4o-mini",
                Provider = "OpenAI",
                Temperature = 0.1f,
                MaxResponseTokens = 300,
                RequiresToolExecution = false,
                SystemPromptTemplate = "assistant"
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

        return create.Value!;
    }

    private static IntentRoutingRule CreateRule(string intentKey, string workflowId, string targetAgentId, int priority)
    {
        var now = DateTimeOffset.UtcNow;
        return new IntentRoutingRule
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = "tenant-1",
            IntentKey = intentKey,
            SourceAgentId = "router-agent",
            TargetAgentId = targetAgentId,
            WorkflowDefinitionId = workflowId,
            WorkflowName = workflowId,
            Priority = priority,
            Enabled = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
