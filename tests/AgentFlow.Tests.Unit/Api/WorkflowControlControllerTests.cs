using System.Text.Json;
using AgentFlow.Abstractions.Workflow;
using AgentFlow.Api.Controllers;
using AgentFlow.Api.Workflow;
using AgentFlow.Application.Memory;
using AgentFlow.Security;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AgentFlow.Tests.Unit.Api;

public sealed class WorkflowControlControllerTests
{
    [Fact]
    public async Task GetMetrics_DefaultWindow24h_FiltersOldExecutions()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new FakeWorkflowStudioStore(new[]
        {
            new WorkflowExecutionContract
            {
                Id = "exec-new",
                TenantId = "tenant-1",
                WorkflowDefinitionId = "wf-1",
                TriggerEventName = "connect.message.received",
                Status = WorkflowExecutionStatus.Completed,
                CreatedAt = now.AddHours(-2),
                UpdatedAt = now.AddHours(-1),
                RequestedBy = "u1"
            },
            new WorkflowExecutionContract
            {
                Id = "exec-old",
                TenantId = "tenant-1",
                WorkflowDefinitionId = "wf-1",
                TriggerEventName = "connect.message.received",
                Status = WorkflowExecutionStatus.Completed,
                CreatedAt = now.AddDays(-2),
                UpdatedAt = now.AddDays(-2).AddMinutes(10),
                RequestedBy = "u1"
            }
        });
        var audit = new Mock<IAuditMemory>();
        audit.Setup(x => x.GetRecentAsync("tenant-1", 3000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditEntry>());

        var controller = BuildController(store, audit.Object, "tenant-1");
        var result = await controller.GetMetrics("tenant-1", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal("24h", json.RootElement.GetProperty("window").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GetMetrics_7d_BuildsProviderResolutionByRole()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new FakeWorkflowStudioStore(new[]
        {
            new WorkflowExecutionContract
            {
                Id = "exec-1",
                TenantId = "tenant-1",
                WorkflowDefinitionId = "wf-1",
                TriggerEventName = "connect.call.received",
                Status = WorkflowExecutionStatus.Completed,
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1).AddMinutes(1),
                RequestedBy = "voice-runtime"
            }
        });

        var auditRows = new List<AuditEntry>
        {
            new()
            {
                TenantId = "tenant-1",
                AgentId = "wf-1",
                UserId = "voice-runtime",
                EventType = AuditEventType.ConnectOperation,
                OccurredAt = now.AddDays(-1),
                EventJson = JsonSerializer.Serialize(new
                {
                    action = "voice.stt.provider.selected",
                    details = new
                    {
                        decision = "fallback",
                        provider = "deepgram"
                    }
                })
            },
            new()
            {
                TenantId = "tenant-1",
                AgentId = "wf-1",
                UserId = "voice-playback",
                EventType = AuditEventType.ConnectOperation,
                OccurredAt = now.AddDays(-1),
                EventJson = JsonSerializer.Serialize(new
                {
                    action = "voice.playback.delivered",
                    details = new
                    {
                        decision = "primary",
                        provider = "twilio"
                    }
                })
            },
            new()
            {
                TenantId = "tenant-1",
                AgentId = "wf-1",
                UserId = "voice-playback",
                EventType = AuditEventType.ConnectOperation,
                OccurredAt = now.AddDays(-1),
                EventJson = JsonSerializer.Serialize(new
                {
                    action = "voice.playback.failed"
                })
            }
        };

        var audit = new Mock<IAuditMemory>();
        audit.Setup(x => x.GetRecentAsync("tenant-1", 3000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(auditRows);

        var controller = BuildController(store, audit.Object, "tenant-1");
        var result = await controller.GetMetrics("tenant-1", "7d", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var byRole = json.RootElement
            .GetProperty("continuitySignals")
            .GetProperty("providerResolutionByRole");

        var stt = byRole.GetProperty("stt");
        Assert.Equal(0, stt.GetProperty("primary").GetInt32());
        Assert.Equal(1, stt.GetProperty("fallback").GetInt32());

        var callControl = byRole.GetProperty("callControl");
        Assert.Equal(1, callControl.GetProperty("primary").GetInt32());
        Assert.Equal(1, callControl.GetProperty("failed").GetInt32());
    }

    private static WorkflowControlController BuildController(
        IWorkflowStudioStore store,
        IAuditMemory audit,
        string tenantId)
    {
        var tenantContext = new TenantContextAccessor();
        tenantContext.Set(new TenantContext
        {
            TenantId = tenantId,
            UserId = "u1",
            UserEmail = "u1@test.local",
            Permissions = new[] { AgentFlowPermissions.AuditRead },
            Roles = Array.Empty<string>(),
            IsPlatformAdmin = false
        });

        return new WorkflowControlController(store, audit, tenantContext);
    }

    private sealed class FakeWorkflowStudioStore : IWorkflowStudioStore
    {
        private readonly IReadOnlyList<WorkflowExecutionContract> _executions;

        public FakeWorkflowStudioStore(IReadOnlyList<WorkflowExecutionContract> executions)
        {
            _executions = executions;
        }

        public Task<IReadOnlyList<WorkflowExecutionContract>> GetExecutionsAsync(string tenantId, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WorkflowExecutionContract>>(_executions.Where(x => x.TenantId == tenantId).Take(limit).ToList());

        public Task<IReadOnlyList<WorkflowExecutionStepLogContract>> GetStepLogsAsync(string tenantId, string executionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WorkflowExecutionStepLogContract>>(Array.Empty<WorkflowExecutionStepLogContract>());

        public Task<IReadOnlyList<WorkflowActivityCatalogContract>> GetActivitiesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkflowActivityCatalogContract> UpsertActivityAsync(WorkflowActivityCatalogContract activity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<WorkflowEventCatalogContract>> GetEventsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkflowEventCatalogContract> UpsertEventAsync(WorkflowEventCatalogContract evt, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<WorkflowTemplateContract>> GetTemplatesAsync(string tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkflowTemplateContract> UpsertTemplateAsync(WorkflowTemplateContract template, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<WorkflowDefinitionContract>> GetDefinitionsAsync(string tenantId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkflowDefinitionContract?> GetDefinitionAsync(string tenantId, string workflowId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkflowDefinitionContract> UpsertDefinitionAsync(WorkflowDefinitionContract definition, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkflowExecutionContract> CreateExecutionAsync(WorkflowExecutionContract execution, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkflowExecutionContract?> UpdateExecutionStatusAsync(string tenantId, string executionId, WorkflowExecutionStatus status, string? error, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkflowExecutionContract?> UpdateExecutionContextAsync(string tenantId, string executionId, string contextJson, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkflowExecutionStepLogContract> CreateStepLogAsync(WorkflowExecutionStepLogContract step, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkflowExecutionStepLogContract?> CompleteStepLogAsync(string tenantId, string stepId, WorkflowExecutionStatus status, string? outputJson, string? error, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
