using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;
using AgentFlow.Security;

namespace AgentFlow.Tests.Integration.Orchestration;

public sealed class HandoffOrchestrationIntegrationTests
{
    [Fact]
    public async Task Handoff_IsDenied_WhenPolicyBlocksTarget()
    {
        var policy = new FakePolicy((_, _, _) => new HandoffPolicyDecision(false, "target_not_in_allowlist", true, ["agent-a"]));
        var executor = new AgentHandoffExecutor(new RecordingAgentExecutor(), policy);

        var result = await executor.ExecuteAsync(new AgentHandoffRequest
        {
            TenantId = "tenant-1",
            SessionId = "sess-1",
            ThreadId = "thread-1",
            CorrelationId = "corr-1",
            SourceAgentKey = "manager",
            TargetAgentKey = "agent-b",
            Intent = "delegate",
            PayloadJson = "{}"
        });

        Assert.False(result.Ok);
        Assert.StartsWith("handoff_policy_denied", result.ErrorCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handoff_CanChainSubAgents_WithSameTraceIdentity()
    {
        var policy = new FakePolicy((_, _, _) => new HandoffPolicyDecision(true, "target_in_allowlist", true, ["agent-a", "agent-b"]));
        var fakeExecutor = new RecordingAgentExecutor();
        var handoff = new AgentHandoffExecutor(fakeExecutor, policy);

        var first = await handoff.ExecuteAsync(new AgentHandoffRequest
        {
            TenantId = "tenant-1",
            SessionId = "sess-9",
            ThreadId = "thread-9",
            CorrelationId = "corr-9",
            SourceAgentKey = "manager",
            TargetAgentKey = "agent-a",
            Intent = "step-1",
            PayloadJson = "{\"next\":\"agent-b\"}"
        });

        var second = await handoff.ExecuteAsync(new AgentHandoffRequest
        {
            TenantId = "tenant-1",
            SessionId = first.SessionId,
            ThreadId = first.ThreadId,
            CorrelationId = first.CorrelationId,
            SourceAgentKey = "agent-a",
            TargetAgentKey = "agent-b",
            Intent = "step-2",
            PayloadJson = "{\"from\":\"agent-a\"}"
        });

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.Equal(2, fakeExecutor.Requests.Count);
        Assert.All(fakeExecutor.Requests, r => Assert.Equal("corr-9", r.CorrelationId));
        Assert.All(fakeExecutor.Requests, r => Assert.Equal("sess-9", r.SessionId));
        Assert.All(fakeExecutor.Requests, r => Assert.Equal("thread-9", r.ThreadId));
    }

    [Fact]
    public async Task Handoff_RecordsCompleteTraceability_ByCorrelationId()
    {
        var policy = new FakePolicy((_, _, _) => new HandoffPolicyDecision(true, "target_in_allowlist", true, ["agent-a"]));
        var fakeExecutor = new RecordingAgentExecutor();
        var handoff = new AgentHandoffExecutor(fakeExecutor, policy);

        var correlationId = "corr-trace-77";
        await handoff.ExecuteAsync(new AgentHandoffRequest
        {
            TenantId = "tenant-1",
            SessionId = "sess-77",
            ThreadId = "thread-77",
            CorrelationId = correlationId,
            SourceAgentKey = "manager",
            TargetAgentKey = "agent-a",
            Intent = "traceability",
            PayloadJson = "{}"
        });

        var request = Assert.Single(fakeExecutor.Requests);
        Assert.Equal(correlationId, request.CorrelationId);
        Assert.Equal("thread-77", request.ThreadId);
        Assert.Equal("sess-77", request.SessionId);
        Assert.Equal(correlationId, request.Metadata["handoff.correlationId"]);
    }

    private sealed class RecordingAgentExecutor : IAgentExecutor
    {
        public List<AgentExecutionRequest> Requests { get; } = [];

        public Task<AgentExecutionResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new AgentExecutionResult
            {
                ExecutionId = $"exec-{Requests.Count}",
                AgentKey = request.AgentKey,
                AgentVersion = "v1",
                Status = ExecutionStatus.Completed,
                FinalResponse = "{\"ok\":true}",
                ThreadId = request.ThreadId
            });
        }

        public Task<AgentExecutionResult> ResumeAsync(string executionId, string tenantId, CheckpointDecision decision, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Result> CancelAsync(string executionId, string tenantId, string cancelledBy, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakePolicy(Func<string, string, string, HandoffPolicyDecision> evaluator) : IManagerHandoffPolicy
    {
        public bool IsAllowed(string tenantId, string sourceAgentId, string targetAgentId)
            => evaluator(tenantId, sourceAgentId, targetAgentId).Allowed;

        public IReadOnlyList<string> GetAllowedTargets(string tenantId, string sourceAgentId)
            => Array.Empty<string>();

        public HandoffPolicyDecision Evaluate(string tenantId, string sourceAgentId, string targetAgentId)
            => evaluator(tenantId, sourceAgentId, targetAgentId);
    }
}
