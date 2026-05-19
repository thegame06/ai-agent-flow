namespace AgentFlow.Tests.Unit.IntentRouting;

using AgentFlow.Abstractions;
using AgentFlow.Application.Memory;
using AgentFlow.Intents.Classification.Models;
using AgentFlow.Intents.Ownership;
using AgentFlow.Intents.Ownership.Models;
using AgentFlow.Intents.Routing;
using AgentFlow.Intents.Routing.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Tests unitarios para RoutingOrchestrator - Happy Path.
/// Valida las 4 acciones: Route, Queue, Fallback, Reject.
/// </summary>
public sealed class RoutingOrchestratorTests
{
    private readonly Mock<IConversationOwnershipManager> _ownershipManager;
    private readonly Mock<IAuditMemory> _auditMemory;
    private readonly Mock<ILogger<RoutingOrchestrator>> _logger;
    private readonly RoutingOrchestrator _orchestrator;

    public RoutingOrchestratorTests()
    {
        _ownershipManager = new Mock<IConversationOwnershipManager>();
        _auditMemory = new Mock<IAuditMemory>();
        _logger = new Mock<ILogger<RoutingOrchestrator>>();

        _orchestrator = new RoutingOrchestrator(
            _ownershipManager.Object,
            _auditMemory.Object,
            _logger.Object);
    }

    [Fact]
    public async Task RouteMessage_HighConfidence_RoutesToWorkflow()
    {
        // Arrange
        var classification = new IntentClassificationResult
        {
            BestMatch = new IntentMatch
            {
                IntentKey = "loan_application",
                DisplayName = "Solicitud de Préstamo",
                Category = "Sales",
                SimilarityScore = 0.95,
                Explanation = "High confidence match"
            },
            AllCandidates = new List<ScoredCandidate>(),
            BestScore = 0.95,
            Confidence = ConfidenceLevel.High,
            ConfidenceNumeric = 0.95,
            ExplanationJson = "{}"
        };

        var context = new ConversationContext
        {
            ConversationId = "conv-123",
            TenantId = "tenant-1",
            Channel = "whatsapp",
            UserIdentifier = "user-456",
            CurrentAgentId = null // No hay agente activo
        };

        _ownershipManager.Setup(x => x.GetStateAsync(context.TenantId, context.ConversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OwnershipState?)null); // No lock existente

        _ownershipManager.Setup(x => x.TryAcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var decision = await _orchestrator.RouteMessageAsync(classification, context, CancellationToken.None);

        // Assert
        Assert.NotNull(decision);
        Assert.Equal(RoutingAction.Route, decision.Action);
        Assert.Equal("loan-officer-workflow", decision.WorkflowDefinitionId); // Basado en mapping interno
        Assert.True(decision.OwnershipAcquired);
        Assert.NotNull(decision.ExplanationJson);
        Assert.Equal("HighConfidence", decision.ReasonCode);

        _ownershipManager.Verify(x => x.TryAcquireLockAsync(
            context.TenantId,
            context.ConversationId,
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RouteMessage_MediumConfidence_RoutesToWorkflow()
    {
        // Arrange
        var classification = new IntentClassificationResult
        {
            BestMatch = new IntentMatch
            {
                IntentKey = "payment_status",
                DisplayName = "Estado de Pago",
                Category = "Payments",
                SimilarityScore = 0.82,
                Explanation = "Medium confidence match"
            },
            AllCandidates = new List<ScoredCandidate>(),
            BestScore = 0.82,
            Confidence = ConfidenceLevel.Medium,
            ConfidenceNumeric = 0.82,
            ExplanationJson = "{}"
        };

        var context = new ConversationContext
        {
            ConversationId = "conv-456",
            TenantId = "tenant-1",
            Channel = "web",
            UserIdentifier = "user-789"
        };

        _ownershipManager.Setup(x => x.GetStateAsync(context.TenantId, context.ConversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OwnershipState?)null);

        _ownershipManager.Setup(x => x.TryAcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var decision = await _orchestrator.RouteMessageAsync(classification, context, CancellationToken.None);

        // Assert
        Assert.NotNull(decision);
        Assert.Equal(RoutingAction.Route, decision.Action);
        Assert.Equal("payment-status-workflow", decision.WorkflowDefinitionId);
        Assert.True(decision.OwnershipAcquired);
        Assert.Equal("MediumConfidence", decision.ReasonCode);
    }

    [Fact]
    public async Task RouteMessage_LowConfidence_QueuesForReview()
    {
        // Arrange
        var classification = new IntentClassificationResult
        {
            BestMatch = new IntentMatch
            {
                IntentKey = "general_support",
                DisplayName = "Soporte General",
                Category = "Support",
                SimilarityScore = 0.65,
                Explanation = "Low confidence match"
            },
            AllCandidates = new List<ScoredCandidate>(),
            BestScore = 0.65,
            Confidence = ConfidenceLevel.Low,
            ConfidenceNumeric = 0.65,
            ExplanationJson = "{}"
        };

        var context = new ConversationContext
        {
            ConversationId = "conv-789",
            TenantId = "tenant-1",
            Channel = "whatsapp",
            UserIdentifier = "user-abc"
        };

        // Act
        var decision = await _orchestrator.RouteMessageAsync(classification, context, CancellationToken.None);

        // Assert
        Assert.NotNull(decision);
        Assert.Equal(RoutingAction.Queue, decision.Action);
        Assert.Null(decision.WorkflowDefinitionId); // No workflow para low confidence
        Assert.False(decision.OwnershipAcquired);
        Assert.Equal("LowConfidence", decision.ReasonCode);

        // No debe intentar adquirir lock para Queue
        _ownershipManager.Verify(x => x.TryAcquireLockAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RouteMessage_NoMatch_FallsBackToHuman()
    {
        // Arrange
        var classification = new IntentClassificationResult
        {
            BestMatch = null,
            AllCandidates = new List<ScoredCandidate>(),
            BestScore = 0.0,
            Confidence = ConfidenceLevel.NoMatch,
            ConfidenceNumeric = 0.0,
            ExplanationJson = "{}"
        };

        var context = new ConversationContext
        {
            ConversationId = "conv-xyz",
            TenantId = "tenant-1",
            Channel = "whatsapp",
            UserIdentifier = "user-def"
        };

        // Act
        var decision = await _orchestrator.RouteMessageAsync(classification, context, CancellationToken.None);

        // Assert
        Assert.NotNull(decision);
        Assert.Equal(RoutingAction.Fallback, decision.Action);
        Assert.Null(decision.WorkflowDefinitionId);
        Assert.False(decision.OwnershipAcquired);
        Assert.Equal("NoMatch", decision.ReasonCode);

        _ownershipManager.Verify(x => x.TryAcquireLockAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RouteMessage_ConversationLocked_RejectsWithConflict()
    {
        // Arrange
        var classification = new IntentClassificationResult
        {
            BestMatch = new IntentMatch
            {
                IntentKey = "loan_application",
                DisplayName = "Solicitud de Préstamo",
                Category = "Sales",
                SimilarityScore = 0.95,
                Explanation = "High confidence match"
            },
            AllCandidates = new List<ScoredCandidate>(),
            BestScore = 0.95,
            Confidence = ConfidenceLevel.High,
            ConfidenceNumeric = 0.95,
            ExplanationJson = "{}"
        };

        var context = new ConversationContext
        {
            ConversationId = "conv-locked",
            TenantId = "tenant-1",
            Channel = "whatsapp",
            UserIdentifier = "user-ghi"
        };

        // Simular que otro agente ya tiene el lock
        _ownershipManager.Setup(x => x.GetStateAsync(context.TenantId, context.ConversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnershipState
            {
                ConversationId = context.ConversationId,
                OwnerAgentId = "another-agent-123",
                AcquiredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                IsActive = true
            });

        // Act
        var decision = await _orchestrator.RouteMessageAsync(classification, context, CancellationToken.None);

        // Assert
        Assert.NotNull(decision);
        Assert.Equal(RoutingAction.Reject, decision.Action);
        Assert.Null(decision.WorkflowDefinitionId);
        Assert.False(decision.OwnershipAcquired);
        Assert.Equal("ConversationLocked", decision.ReasonCode);

        // No debe intentar adquirir lock si ya está ocupado
        _ownershipManager.Verify(x => x.TryAcquireLockAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RouteMessage_LockAcquisitionFails_FallsBackToQueue()
    {
        // Arrange
        var classification = new IntentClassificationResult
        {
            BestMatch = new IntentMatch
            {
                IntentKey = "loan_application",
                DisplayName = "Solicitud de Préstamo",
                Category = "Sales",
                SimilarityScore = 0.95,
                Explanation = "High confidence match"
            },
            AllCandidates = new List<ScoredCandidate>(),
            BestScore = 0.95,
            Confidence = ConfidenceLevel.High,
            ConfidenceNumeric = 0.95,
            ExplanationJson = "{}"
        };

        var context = new ConversationContext
        {
            ConversationId = "conv-fail-lock",
            TenantId = "tenant-1",
            Channel = "whatsapp",
            UserIdentifier = "user-jkl"
        };

        _ownershipManager.Setup(x => x.GetStateAsync(context.TenantId, context.ConversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OwnershipState?)null);

        // Simular falla al adquirir lock (race condition)
        _ownershipManager.Setup(x => x.TryAcquireLockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var decision = await _orchestrator.RouteMessageAsync(classification, context, CancellationToken.None);

        // Assert
        Assert.NotNull(decision);
        Assert.Equal(RoutingAction.Queue, decision.Action); // Fallback a Queue
        Assert.Null(decision.WorkflowDefinitionId);
        Assert.False(decision.OwnershipAcquired);
        Assert.Equal("LockAcquisitionFailed", decision.ReasonCode);
    }
}
