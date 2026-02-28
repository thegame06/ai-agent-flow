using AgentFlow.Abstractions;
using AgentFlow.Api.Controllers;
using AgentFlow.Application.Memory;
using AgentFlow.Domain.Repositories;
using AgentFlow.Evaluation;
using AgentFlow.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Integration.Executions;

public sealed class HandoffEndpointTests
{
    [Fact]
    public async Task HandoffAsync_ReturnsBadRequest_WhenPayloadJsonIsInvalid()
    {
        var controller = BuildController();

        var result = await controller.HandoffAsync(
            "tenant-1",
            "manager-agent",
            new HandoffExecutionRequest
            {
                SessionId = "sess-1",
                TargetAgentId = "collections-bot",
                Intent = "collections_reminder",
                PayloadJson = "{invalid-json}"
            },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task HandoffAsync_RecordsAuditEvents_WhenHandoffSucceeds()
    {
        var auditEntries = new List<AuditEntry>();

        var controller = BuildController(
            setupHandoff: h => h
                .Setup(x => x.ExecuteAsync(It.IsAny<AgentHandoffRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AgentHandoffResponse
                {
                    Ok = true,
                    Retryable = false,
                    ResultJson = "{\"done\":true}"
                }),
            setupAudit: a => a
                .Setup(x => x.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
                .Callback<AuditEntry, CancellationToken>((entry, _) => auditEntries.Add(entry))
                .Returns(Task.CompletedTask));

        var result = await controller.HandoffAsync(
            "tenant-1",
            "manager-agent",
            new HandoffExecutionRequest
            {
                SessionId = "sess-1",
                CorrelationId = "corr-1",
                TargetAgentId = "collections-bot",
                Intent = "collections_reminder",
                PayloadJson = "{\"customerId\":\"C001\"}"
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, auditEntries.Count);
        Assert.Contains(auditEntries, x => x.EventType == AuditEventType.HandoffRequested);
        Assert.Contains(auditEntries, x => x.EventType == AuditEventType.HandoffCompleted);
    }

    private static AgentExecutionsController BuildController(
        Action<Mock<IAgentHandoffExecutor>>? setupHandoff = null,
        Action<Mock<IAuditMemory>>? setupAudit = null)
    {
        var executor = new Mock<IAgentExecutor>();
        var agentRepo = new Mock<IAgentDefinitionRepository>();
        var executionRepo = new Mock<IAgentExecutionRepository>();
        var canary = new Mock<ICanaryRoutingService>();
        var segment = new Mock<ISegmentRoutingService>();
        var authz = new Mock<IAgentAuthorizationService>();
        var handoff = new Mock<IAgentHandoffExecutor>();
        var audit = new Mock<IAuditMemory>();
        var tenantContext = new TenantContextAccessor();

        tenantContext.Set(new TenantContext
        {
            TenantId = "tenant-1",
            UserId = "u1",
            UserEmail = "u1@test.local",
            Permissions = new[] { AgentFlowPermissions.ExecutionHandoff },
            Roles = new[] { "operator" },
            IsPlatformAdmin = false
        });

        authz.Setup(x => x.CanHandoffExecutionAsync(It.IsAny<TenantContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        handoff.Setup(x => x.ExecuteAsync(It.IsAny<AgentHandoffRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentHandoffResponse { Ok = true, Retryable = false, ResultJson = "{}" });

        audit.Setup(x => x.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        setupHandoff?.Invoke(handoff);
        setupAudit?.Invoke(audit);

        return new AgentExecutionsController(
            executor.Object,
            agentRepo.Object,
            executionRepo.Object,
            canary.Object,
            segment.Object,
            authz.Object,
            handoff.Object,
            audit.Object,
            tenantContext,
            NullLogger<AgentExecutionsController>.Instance);
    }
}
