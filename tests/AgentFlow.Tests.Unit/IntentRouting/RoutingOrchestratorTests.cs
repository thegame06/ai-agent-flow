namespace AgentFlow.Tests.Unit.IntentRouting;

using AgentFlow.Application.Memory;
using AgentFlow.Intents.Classification.Models;
using AgentFlow.Intents.Ownership;
using AgentFlow.Intents.Ownership.Models;
using AgentFlow.Intents.Routing;
using AgentFlow.Intents.Routing.Models;
using AgentFlow.Security;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public sealed class RoutingOrchestratorTests
{
    private readonly Mock<IConversationOwnershipManager> _ownershipManager = new();
    private readonly Mock<IAuditMemory> _auditMemory = new();
    private readonly RoutingOrchestrator _orchestrator;

    public RoutingOrchestratorTests()
    {
        _orchestrator = new RoutingOrchestrator(
            _ownershipManager.Object,
            _auditMemory.Object,
            Mock.Of<ILogger<RoutingOrchestrator>>());
    }

    [Fact]
    public async Task RouteMessage_HighConfidence_RoutesToWorkflow()
    {
        var rule = new IntentRoutingRule { IntentKey = "comprar_producto", WorkflowDefinitionId = "wf-starter-sales", WorkflowName = "Starter ventas", TargetAgentId = "sales-agent", Priority = 100 };
        var classification = new IntentClassificationResult
        {
            Message = "quiero comprar",
            BestMatch = new IntentMatch { IntentKey = "comprar_producto", SimilarityScore = 0.92f, MatchedVia = "semantic", Rule = rule },
            AllCandidates = new List<IntentMatch>(),
            BestScore = 0.92f,
            Confidence = ConfidenceLevel.High,
            RequiresHumanReview = false,
            ExplanationJson = "{}"
        };

        _ownershipManager.Setup(x => x.GetStateAsync("tenant-1", "conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationOwnershipState { IsLocked = false, CurrentOwnerAgentId = null, LockedUntil = null });
        _ownershipManager.Setup(x => x.TryAcquireLockAsync("tenant-1", "conv-1", "sales-agent", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnershipLock { LockId = "lock-1", ConversationId = "conv-1", OwnerAgentId = "sales-agent", AcquiredAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });

        var decision = await _orchestrator.RouteMessageAsync(classification, new ConversationContext
        {
            ConversationId = "conv-1",
            TenantId = "tenant-1",
            Channel = "whatsapp",
            UserIdentifier = "user-1"
        }, CancellationToken.None);

        Assert.Equal(RoutingAction.Route, decision.Action);
        Assert.Equal("wf-starter-sales", decision.WorkflowDefinitionId);
        Assert.Equal("matched", decision.ReasonCode);
    }

    [Fact]
    public async Task RouteMessage_NoMatch_NoRulesConfigured_Fallback()
    {
        var classification = new IntentClassificationResult
        {
            Message = "hola",
            BestMatch = null,
            AllCandidates = new List<IntentMatch>(),
            BestScore = 0f,
            Confidence = ConfidenceLevel.NoMatch,
            RequiresHumanReview = true,
            ExplanationJson = "{}"
        };

        var decision = await _orchestrator.RouteMessageAsync(classification, new ConversationContext
        {
            ConversationId = "conv-2",
            TenantId = "tenant-1",
            Channel = "whatsapp",
            UserIdentifier = "user-2"
        }, CancellationToken.None);

        Assert.Equal(RoutingAction.Fallback, decision.Action);
        Assert.Equal("no_rules_configured", decision.ReasonCode);
    }

    [Fact]
    public async Task RouteMessage_LowConfidence_Queue()
    {
        var rule = new IntentRoutingRule { IntentKey = "comprar_producto", WorkflowDefinitionId = "wf-starter-sales", TargetAgentId = "sales-agent", Priority = 100 };
        var classification = new IntentClassificationResult
        {
            Message = "me interesa algo",
            BestMatch = new IntentMatch { IntentKey = "comprar_producto", SimilarityScore = 0.62f, MatchedVia = "semantic", Rule = rule },
            AllCandidates = new List<IntentMatch>(),
            BestScore = 0.62f,
            Confidence = ConfidenceLevel.Low,
            RequiresHumanReview = true,
            ExplanationJson = "{}"
        };

        var decision = await _orchestrator.RouteMessageAsync(classification, new ConversationContext
        {
            ConversationId = "conv-3",
            TenantId = "tenant-1",
            Channel = "whatsapp",
            UserIdentifier = "user-3"
        }, CancellationToken.None);

        Assert.Equal(RoutingAction.Queue, decision.Action);
        Assert.Equal("low_confidence", decision.ReasonCode);
    }

    [Fact]
    public async Task RouteMessage_LockedByAnotherAgent_Reject()
    {
        var rule = new IntentRoutingRule { IntentKey = "comprar_producto", WorkflowDefinitionId = "wf-starter-sales", TargetAgentId = "sales-agent", Priority = 100 };
        var classification = new IntentClassificationResult
        {
            Message = "quiero comprar",
            BestMatch = new IntentMatch { IntentKey = "comprar_producto", SimilarityScore = 0.92f, MatchedVia = "semantic", Rule = rule },
            AllCandidates = new List<IntentMatch>(),
            BestScore = 0.92f,
            Confidence = ConfidenceLevel.High,
            RequiresHumanReview = false,
            ExplanationJson = "{}"
        };

        _ownershipManager.Setup(x => x.GetStateAsync("tenant-1", "conv-4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationOwnershipState { IsLocked = true, CurrentOwnerAgentId = "other-agent", LockedUntil = DateTimeOffset.UtcNow.AddMinutes(1) });

        var decision = await _orchestrator.RouteMessageAsync(classification, new ConversationContext
        {
            ConversationId = "conv-4",
            TenantId = "tenant-1",
            Channel = "whatsapp",
            UserIdentifier = "user-4"
        }, CancellationToken.None);

        Assert.Equal(RoutingAction.Reject, decision.Action);
        Assert.Equal("agent_conflict", decision.ReasonCode);
    }
}
