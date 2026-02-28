using AgentFlow.Abstractions;

namespace AgentFlow.Core.Engine;

/// <summary>
/// Default internal handoff executor (Manager -> Subagent).
/// Maps handoff request to a standard agent execution.
/// </summary>
public sealed class AgentHandoffExecutor : IAgentHandoffExecutor
{
    private readonly IAgentExecutor _agentExecutor;

    public AgentHandoffExecutor(IAgentExecutor agentExecutor)
    {
        _agentExecutor = agentExecutor;
    }

    public async Task<AgentHandoffResponse> ExecuteAsync(AgentHandoffRequest request, CancellationToken ct = default)
    {
        var executionRequest = new AgentExecutionRequest
        {
            TenantId = request.TenantId,
            AgentKey = request.TargetAgentKey,
            UserId = request.SourceAgentKey,
            UserMessage = request.PayloadJson,
            SessionId = request.SessionId,
            ThreadId = request.SessionId,
            CorrelationId = request.CorrelationId,
            Metadata = new Dictionary<string, string>(request.Metadata)
            {
                ["handoff.intent"] = request.Intent,
                ["handoff.sourceAgent"] = request.SourceAgentKey,
                ["handoff.targetAgent"] = request.TargetAgentKey
            }
        };

        var result = await _agentExecutor.ExecuteAsync(executionRequest, ct);

        return new AgentHandoffResponse
        {
            Ok = result.Status == ExecutionStatus.Completed,
            ResultJson = result.FinalResponse,
            ErrorCode = result.ErrorCode,
            Retryable = result.Status == ExecutionStatus.Failed,
            StatePatch = new Dictionary<string, string>
            {
                ["lastExecutionId"] = result.ExecutionId,
                ["status"] = result.Status.ToString()
            }
        };
    }
}
