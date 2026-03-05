using AgentFlow.Api.Controllers;
using AgentFlow.Application.Memory;
using AgentFlow.Security;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AgentFlow.Tests.Integration.Audit;

public sealed class AuditControllerTests
{
    [Fact]
    public async Task GetAuditLogs_UsesCorrelationQuery_WhenProvided()
    {
        var audit = new Mock<IAuditMemory>();
        var tenantContext = new TenantContextAccessor();
        tenantContext.Set(new TenantContext
        {
            TenantId = "tenant-1",
            UserId = "u1",
            UserEmail = "u1@test.local",
            Permissions = Array.Empty<string>(),
            Roles = new[] { "operator" },
            IsPlatformAdmin = false
        });

        audit.Setup(x => x.GetByCorrelationAsync("tenant-1", "corr-1", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditEntry>
            {
                new()
                {
                    TenantId = "tenant-1",
                    AgentId = "manager-agent",
                    UserId = "u1",
                    EventType = AuditEventType.RoutingDecision,
                    CorrelationId = "corr-1",
                    EventJson = "{}",
                    ExecutionId = "exec-1"
                }
            });

        var controller = new AuditController(audit.Object, tenantContext);
        var result = await controller.GetAuditLogs("tenant-1", 100, "corr-1", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        audit.Verify(x => x.GetByCorrelationAsync("tenant-1", "corr-1", 100, It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(x => x.GetRecentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAuditLogs_FiltersByAction_WhenProvided()
    {
        var audit = new Mock<IAuditMemory>();
        var tenantContext = new TenantContextAccessor();
        tenantContext.Set(new TenantContext
        {
            TenantId = "tenant-1",
            UserId = "u1",
            UserEmail = "u1@test.local",
            Permissions = Array.Empty<string>(),
            Roles = new[] { "operator" },
            IsPlatformAdmin = false
        });

        audit.Setup(x => x.GetRecentAsync("tenant-1", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditEntry>
            {
                new() { TenantId = "tenant-1", AgentId = "a1", UserId = "u1", EventType = AuditEventType.RoutingDecision, CorrelationId = "c1", EventJson = "{}", ExecutionId = "e1" },
                new() { TenantId = "tenant-1", AgentId = "a1", UserId = "u1", EventType = AuditEventType.HandoffCompleted, CorrelationId = "c1", EventJson = "{}", ExecutionId = "e2" }
            });

        var controller = new AuditController(audit.Object, tenantContext);
        var result = await controller.GetAuditLogs("tenant-1", 100, null, "RoutingDecision", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("RoutingDecision", json);
        Assert.DoesNotContain("HandoffCompleted", json);
    }
}
