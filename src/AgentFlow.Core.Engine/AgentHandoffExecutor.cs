using AgentFlow.Abstractions;
using AgentFlow.Observability;
using AgentFlow.Security;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentFlow.Core.Engine;

/// <summary>
/// Default internal handoff executor (Manager -> Subagent).
/// Maps handoff request to a standard agent execution.
/// </summary>
public sealed class AgentHandoffExecutor : IAgentHandoffExecutor
{
    private readonly IAgentExecutor _agentExecutor;
    private readonly IManagerHandoffPolicy _handoffPolicy;

    public AgentHandoffExecutor(IAgentExecutor agentExecutor, IManagerHandoffPolicy handoffPolicy)
    {
        _agentExecutor = agentExecutor;
        _handoffPolicy = handoffPolicy;
    }

    public async Task<AgentHandoffResponse> ExecuteAsync(AgentHandoffRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) ||
            string.IsNullOrWhiteSpace(request.ThreadId) ||
            string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            return new AgentHandoffResponse
            {
                SessionId = request.SessionId,
                ThreadId = request.ThreadId,
                CorrelationId = request.CorrelationId,
                Ok = false,
                ErrorCode = "handoff_identity_required",
                Retryable = false
            };
        }

        var policyDecision = _handoffPolicy.Evaluate(request.TenantId, request.SourceAgentKey, request.TargetAgentKey);
        if (!policyDecision.Allowed)
        {
            return new AgentHandoffResponse
            {
                SessionId = request.SessionId,
                ThreadId = request.ThreadId,
                CorrelationId = request.CorrelationId,
                Ok = false,
                ErrorCode = $"handoff_policy_denied:{policyDecision.Reason}",
                Retryable = false,
                StatePatch = new Dictionary<string, string>
                {
                    ["policy.allowed"] = "false",
                    ["policy.reason"] = policyDecision.Reason
                }
            };
        }

        using var activity = AgentFlowTelemetry.EngineSource.StartActivity("AgentHandoffHop", ActivityKind.Internal);
        activity?.SetTag("agentflow.handoff.contract_version", request.ContractVersion);
        activity?.SetTag("agentflow.handoff.correlation_id", request.CorrelationId);
        activity?.SetTag("agentflow.handoff.session_id", request.SessionId);
        activity?.SetTag("agentflow.handoff.thread_id", request.ThreadId);
        activity?.SetTag("agentflow.handoff.source_agent", request.SourceAgentKey);
        activity?.SetTag("agentflow.handoff.target_agent", request.TargetAgentKey);

        var startedAt = Stopwatch.GetTimestamp();
        var executionRequest = new AgentExecutionRequest
        {
            TenantId = request.TenantId,
            AgentKey = request.TargetAgentKey,
            UserId = request.SourceAgentKey,
            UserMessage = request.PayloadJson,
            SessionId = request.SessionId,
            ThreadId = request.ThreadId,
            CorrelationId = request.CorrelationId,
            Metadata = new Dictionary<string, string>(request.Metadata)
            {
                ["handoff.contractVersion"] = request.ContractVersion,
                ["handoff.intent"] = request.Intent,
                ["handoff.sourceAgent"] = request.SourceAgentKey,
                ["handoff.targetAgent"] = request.TargetAgentKey,
                ["handoff.sessionId"] = request.SessionId,
                ["handoff.threadId"] = request.ThreadId,
                ["handoff.correlationId"] = request.CorrelationId
            }
        };

        var result = await _agentExecutor.ExecuteAsync(executionRequest, ct);
        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var status = result.Status == ExecutionStatus.Completed ? "ok" : "error";
        activity?.SetTag("agentflow.handoff.status", status);
        activity?.SetTag("agentflow.handoff.latency_ms", elapsedMs);
        AgentFlowTelemetry.HandoffHopLatency.Record(elapsedMs, new TagList
        {
            { "status", status },
            { "source_agent", request.SourceAgentKey },
            { "target_agent", request.TargetAgentKey }
        });
        AgentFlowTelemetry.HandoffHops.Add(1, new TagList
        {
            { "status", status },
            { "source_agent", request.SourceAgentKey },
            { "target_agent", request.TargetAgentKey }
        });

        return new AgentHandoffResponse
        {
            SessionId = request.SessionId,
            ThreadId = request.ThreadId,
            CorrelationId = request.CorrelationId,
            Ok = result.Status == ExecutionStatus.Completed,
            ResultJson = result.FinalResponse,
            ErrorCode = result.ErrorCode,
            Retryable = result.Status == ExecutionStatus.Failed,
            StatePatch = new Dictionary<string, string>
            {
                ["lastExecutionId"] = result.ExecutionId,
                ["status"] = result.Status.ToString(),
                ["correlationId"] = request.CorrelationId,
                ["sessionId"] = request.SessionId,
                ["threadId"] = request.ThreadId
            }
        };
    }
}
