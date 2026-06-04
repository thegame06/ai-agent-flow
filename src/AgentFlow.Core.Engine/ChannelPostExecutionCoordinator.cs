using AgentFlow.Abstractions;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Core.Engine;

public sealed record ChannelPostExecutionResult
{
    public required string FinalResponse { get; init; }
    public required string ExecutionIdForOutgoing { get; init; }
    public required string RespondingAgentKey { get; init; }
    public bool SuppressCustomerReply { get; init; }
}

public static class ChannelPostExecutionCoordinator
{
    public static async Task<ChannelPostExecutionResult> ContinueAsync(
        ChannelMessage incomingMessage,
        ChannelDefinition channel,
        ChannelSession? session,
        AgentExecutionRequest executionRequest,
        AgentExecutionResult executionResult,
        IAgentExecutor agentExecutor,
        IAgentHandoffExecutor handoffExecutor,
        IManagerHandoffPolicy handoffPolicy,
        IHumanEscalationNotifier? humanEscalationNotifier,
        IChannelSessionRepository sessionRepo,
        IChannelMessageRepository messageRepo,
        ILogger logger,
        CancellationToken ct)
    {
        var finalResponse = executionResult.FinalResponse ?? string.Empty;
        var executionIdForOutgoing = executionResult.ExecutionId;
        var respondingAgentKey = executionResult.AgentKey;
        var suppressCustomerReply = false;

        if (session is not null)
        {
            var fallbackDirective = ChannelGatewayResponseInterpreter.TryParseFallbackDirective(finalResponse);
            if (fallbackDirective is not null)
            {
                var escalationTarget = fallbackDirective.EscalationTarget
                    ?? session.Metadata.GetValueOrDefault("routing.fallback.escalation_target")
                    ?? executionRequest.Metadata.GetValueOrDefault("routing.fallback_escalation_target");

                session.Metadata["routing.fallback.state"] = fallbackDirective.State;
                session.Metadata["routing.fallback.turn"] = fallbackDirective.NextTurn.ToString();
                session.Metadata["routing.fallback.reason"] = fallbackDirective.ReasonCode ?? string.Empty;
                session.Metadata["requires_human_review"] = fallbackDirective.RequiresHumanReview ? "true" : "false";
                if (!string.IsNullOrWhiteSpace(escalationTarget))
                    session.Metadata["routing.fallback.escalation_target"] = escalationTarget;

                if (fallbackDirective.RequiresHumanReview &&
                    string.Equals(fallbackDirective.State, "escalated_human", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(executionRequest.AgentKey, channel.RouterAgentId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(escalationTarget) &&
                    humanEscalationNotifier is not null)
                {
                    var notifyResult = await humanEscalationNotifier.NotifyAsync(
                        new HumanEscalationNotificationRequest
                        {
                            TenantId = incomingMessage.TenantId,
                            QueueId = escalationTarget,
                            ConversationId = incomingMessage.SessionId,
                            UserId = incomingMessage.From,
                            Channel = channel.Type.ToString().ToLowerInvariant(),
                            LastMessage = incomingMessage.Content,
                            ReasonCode = fallbackDirective.ReasonCode ?? "agent_requested_human_handoff",
                            ExecutionId = executionIdForOutgoing,
                            CorrelationId = incomingMessage.SessionId
                        },
                        ct);

                    session.Metadata["routing.fallback.escalation_status"] = notifyResult.Delivered ? "delivered" : "failed";
                    if (!string.IsNullOrWhiteSpace(notifyResult.TicketId))
                        session.Metadata["routing.fallback.ticket_id"] = notifyResult.TicketId;
                    if (!string.IsNullOrWhiteSpace(notifyResult.QueueId))
                        session.Metadata["routing.fallback.queue_id"] = notifyResult.QueueId;
                }

                await sessionRepo.UpdateAsync(session, ct);
                finalResponse = fallbackDirective.CustomerMessage;
                suppressCustomerReply = fallbackDirective.SuppressCustomerReply;
            }
            else if (session.Metadata.ContainsKey("routing.fallback.state"))
            {
                session.Metadata.Remove("routing.fallback.state");
                session.Metadata.Remove("routing.fallback.turn");
                session.Metadata.Remove("routing.fallback.reason");
                await sessionRepo.UpdateAsync(session, ct);
            }
        }

        var routingHandoff = ChannelGatewayResponseInterpreter.TryParseRoutingHandoff(finalResponse);
        if (routingHandoff != null && session != null)
        {
            logger.LogInformation(
                "Router handed off session {SessionId} to WorkflowBrain {AgentId} (workflow: {WorkflowId}, intent: {Intent})",
                session.Id, routingHandoff.WorkflowBrainAgentId,
                routingHandoff.WorkflowExecutionId, routingHandoff.Intent);

            session.LinkAgent(routingHandoff.WorkflowBrainAgentId);
            session.Metadata["routing_handoff_workflow"] = routingHandoff.WorkflowExecutionId ?? string.Empty;
            session.Metadata["routing_handoff_intent"] = routingHandoff.Intent ?? string.Empty;
            session.Metadata["routing_handoff_at"] = DateTimeOffset.UtcNow.ToString("O");
            await sessionRepo.UpdateAsync(session, ct);

            var brainRequest = executionRequest with
            {
                AgentKey = routingHandoff.WorkflowBrainAgentId,
                Metadata = new Dictionary<string, string>(executionRequest.Metadata)
                {
                    ["channelMessageId"] = incomingMessage.Id,
                    ["routerExecutionId"] = executionResult.ExecutionId,
                    ["workflowExecutionId"] = routingHandoff.WorkflowExecutionId ?? string.Empty,
                    ["routingIntent"] = routingHandoff.Intent ?? string.Empty
                }
            };

            var brainResult = await agentExecutor.ExecuteAsync(brainRequest, ct);
            if (brainResult.Status == ExecutionStatus.Failed || string.IsNullOrWhiteSpace(brainResult.FinalResponse))
            {
                return new ChannelPostExecutionResult
                {
                        FinalResponse = string.Empty,
                        ExecutionIdForOutgoing = brainResult.ExecutionId,
                        RespondingAgentKey = brainResult.AgentKey,
                        SuppressCustomerReply = false
                    };
                }

            finalResponse = brainResult.FinalResponse!;
            executionIdForOutgoing = brainResult.ExecutionId;
            respondingAgentKey = brainResult.AgentKey;

            var transitionMessage = ChannelMessage.CreateOutgoing(
                incomingMessage.TenantId,
                incomingMessage.ChannelId,
                incomingMessage.SessionId,
                incomingMessage.From,
                $"[sistema] Conversacion asignada a workflow '{routingHandoff.WorkflowExecutionId ?? "-"}' y agente '{routingHandoff.WorkflowBrainAgentId}'."
            );
            transitionMessage.Metadata["actor"] = "system";
            transitionMessage.Metadata["agentflow.delivery"] = "suppressed";
            transitionMessage.Metadata["agentflow.visibility"] = "inbox_only";
            transitionMessage.Metadata["event_type"] = "workflow_handoff";
            transitionMessage.LinkExecution(executionIdForOutgoing);
            await messageRepo.InsertAsync(transitionMessage, ct);
        }
        else
        {
            var handoff = ChannelGatewayResponseInterpreter.TryParseHandoffDirective(finalResponse);
            if (handoff is not null && session is not null)
            {
                if (!handoffPolicy.IsAllowed(incomingMessage.TenantId, executionRequest.AgentKey, handoff.TargetAgentId))
                {
                    return new ChannelPostExecutionResult
                    {
                        FinalResponse = string.Empty,
                        ExecutionIdForOutgoing = executionIdForOutgoing,
                        RespondingAgentKey = respondingAgentKey,
                        SuppressCustomerReply = false
                    };
                }

                var handoffResponse = await handoffExecutor.ExecuteAsync(new AgentHandoffRequest
                {
                    TenantId = incomingMessage.TenantId,
                    SessionId = incomingMessage.SessionId,
                    ThreadId = session.ThreadId ?? incomingMessage.SessionId,
                    CorrelationId = incomingMessage.SessionId,
                    SourceAgentKey = executionRequest.AgentKey,
                    TargetAgentKey = handoff.TargetAgentId,
                    Intent = handoff.Intent,
                    PayloadJson = handoff.PayloadJson,
                    Metadata = new Dictionary<string, string>
                    {
                        ["channelId"] = incomingMessage.ChannelId,
                        ["source"] = "channel-gateway"
                    }
                }, ct);

                if (!handoffResponse.Ok)
                {
                    return new ChannelPostExecutionResult
                    {
                        FinalResponse = string.Empty,
                        ExecutionIdForOutgoing = executionIdForOutgoing,
                        RespondingAgentKey = respondingAgentKey,
                        SuppressCustomerReply = false
                    };
                }

                finalResponse = ChannelGatewayResponseInterpreter.ExtractResponseText(handoffResponse.ResultJson) ?? handoffResponse.ResultJson;
                executionIdForOutgoing = handoffResponse.StatePatch.TryGetValue("lastExecutionId", out var delegatedId)
                    ? delegatedId
                    : executionIdForOutgoing;
                respondingAgentKey = handoff.TargetAgentId;

                session.LinkAgent(handoff.TargetAgentId);
                await sessionRepo.UpdateAsync(session, ct);
            }
        }

        return new ChannelPostExecutionResult
        {
            FinalResponse = finalResponse,
            ExecutionIdForOutgoing = executionIdForOutgoing,
            RespondingAgentKey = respondingAgentKey,
            SuppressCustomerReply = suppressCustomerReply
        };
    }
}
