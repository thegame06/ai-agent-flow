using AgentFlow.Abstractions.Workflow;
using AgentFlow.Api.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentFlow.Tests.Unit.Workflow;

public sealed class WorkflowCatalogSeederTests
{
    [Fact]
    public async Task StartAsync_Seeds_NewActivities_AndEvents_WhenCatalogEmpty()
    {
        var store = new InMemoryWorkflowStudioStore();

        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowStudioStore>(store);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var seeder = new WorkflowCatalogSeeder(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkflowCatalogSeeder>.Instance);

        await seeder.StartAsync(CancellationToken.None);

        var activities = await store.GetActivitiesAsync(CancellationToken.None);
        var events = await store.GetEventsAsync(CancellationToken.None);

        Assert.Contains(activities, x => x.TypeName == "kyc.document_check");
        Assert.Contains(activities, x => x.TypeName == "kyc.review_case");
        Assert.Contains(activities, x => x.TypeName == "payments.create_intent");
        Assert.Contains(activities, x => x.TypeName == "human.assign");
        Assert.Contains(activities, x => x.TypeName == "human.handoff");
        Assert.Contains(activities, x => x.TypeName == "http.request");
        Assert.Contains(activities, x => x.TypeName == "mcp.tool_call");
        Assert.Contains(activities, x => x.TypeName == "voice.call");
        Assert.Contains(activities, x => x.TypeName == "callcenter.outbound_call");
        Assert.Contains(events, x => x.EventName == "kyc.document.submitted");
        Assert.Contains(events, x => x.EventName == "payments.intent.created");
    }

    private sealed class InMemoryWorkflowStudioStore : IWorkflowStudioStore
    {
        private readonly List<WorkflowActivityCatalogContract> _activities = [];
        private readonly List<WorkflowEventCatalogContract> _events = [];

        public Task<IReadOnlyList<WorkflowActivityCatalogContract>> GetActivitiesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkflowActivityCatalogContract>>(_activities.ToList());

        public Task<WorkflowActivityCatalogContract> UpsertActivityAsync(WorkflowActivityCatalogContract activity, CancellationToken ct = default)
        {
            _activities.RemoveAll(x => x.TypeName == activity.TypeName);
            _activities.Add(activity);
            return Task.FromResult(activity);
        }

        public Task<IReadOnlyList<WorkflowEventCatalogContract>> GetEventsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkflowEventCatalogContract>>(_events.ToList());

        public Task<WorkflowEventCatalogContract> UpsertEventAsync(WorkflowEventCatalogContract evt, CancellationToken ct = default)
        {
            _events.RemoveAll(x => x.EventName == evt.EventName);
            _events.Add(evt);
            return Task.FromResult(evt);
        }

        public Task<IReadOnlyList<WorkflowTemplateContract>> GetTemplatesAsync(string tenantId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowTemplateContract>>([]);
        public Task<WorkflowTemplateContract> UpsertTemplateAsync(WorkflowTemplateContract template, CancellationToken ct = default) => Task.FromResult(template);
        public Task<IReadOnlyList<WorkflowDefinitionContract>> GetDefinitionsAsync(string tenantId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowDefinitionContract>>([]);
        public Task<WorkflowDefinitionContract?> GetDefinitionAsync(string tenantId, string workflowId, CancellationToken ct = default) => Task.FromResult<WorkflowDefinitionContract?>(null);
        public Task<WorkflowDefinitionContract> UpsertDefinitionAsync(WorkflowDefinitionContract definition, CancellationToken ct = default) => Task.FromResult(definition);
        public Task<IReadOnlyList<WorkflowExecutionContract>> GetExecutionsAsync(string tenantId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowExecutionContract>>([]);
        public Task<WorkflowExecutionContract> CreateExecutionAsync(WorkflowExecutionContract execution, CancellationToken ct = default) => Task.FromResult(execution);
        public Task<WorkflowExecutionContract?> UpdateExecutionStatusAsync(string tenantId, string executionId, WorkflowExecutionStatus status, string? error, CancellationToken ct = default) => Task.FromResult<WorkflowExecutionContract?>(null);
        public Task<WorkflowExecutionContract?> UpdateExecutionContextAsync(string tenantId, string executionId, string contextJson, CancellationToken ct = default) => Task.FromResult<WorkflowExecutionContract?>(null);
        public Task<WorkflowExecutionStepLogContract> CreateStepLogAsync(WorkflowExecutionStepLogContract step, CancellationToken ct = default) => Task.FromResult(step);
        public Task<WorkflowExecutionStepLogContract?> CompleteStepLogAsync(string tenantId, string stepId, WorkflowExecutionStatus status, string? outputJson, string? error, CancellationToken ct = default) => Task.FromResult<WorkflowExecutionStepLogContract?>(null);
        public Task<IReadOnlyList<WorkflowExecutionStepLogContract>> GetStepLogsAsync(string tenantId, string executionId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowExecutionStepLogContract>>([]);
    }
}
