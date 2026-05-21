using AgentFlow.Abstractions;
using AgentFlow.Application.Channels;
using AgentFlow.Application.Memory;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Core.Engine;

/// <summary>
/// Main gateway orchestrating multi-channel message routing and agent execution.
/// </summary>
public sealed class ChannelGateway : IChannelGateway
{
    private readonly Dictionary<ChannelType, IChannelHandler> _handlers = new();
    private readonly IChannelDefinitionRepository _channelRepo;
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly IChannelMessageRepository _messageRepo;
    private readonly IAgentExecutor _agentExecutor;
    private readonly IAgentHandoffExecutor _handoffExecutor;
    private readonly IManagerHandoffPolicy _handoffPolicy;
    private readonly IIntentRoutingStore _intentRoutingStore;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IAuditMemory _auditMemory;
    private readonly ILogger<ChannelGateway> _logger;

    public ChannelGateway(
        IChannelDefinitionRepository channelRepo,
        IChannelSessionRepository sessionRepo,
        IChannelMessageRepository messageRepo,
        IAgentExecutor agentExecutor,
        IAgentHandoffExecutor handoffExecutor,
        IManagerHandoffPolicy handoffPolicy,
        IIntentRoutingStore intentRoutingStore,
        ITenantContextAccessor tenantContext,
        IAuditMemory auditMemory,
        IEnumerable<IChannelHandler> handlers,
        ILogger<ChannelGateway> logger)
    {
        _channelRepo = channelRepo;
        _sessionRepo = sessionRepo;
        _messageRepo = messageRepo;
        _agentExecutor = agentExecutor;
        _handoffExecutor = handoffExecutor;
        _handoffPolicy = handoffPolicy;
        _intentRoutingStore = intentRoutingStore;
        _tenantContext = tenantContext;
        _auditMemory = auditMemory;
        _logger = logger;

        foreach (var handler in handlers)
        {
            RegisterHandler(handler);
        }
    }

    public void RegisterHandler(IChannelHandler handler)
    {
        _handlers[handler.SupportedChannelType] = handler;
        _logger.LogInformation("Registered channel handler for {ChannelType}", handler.SupportedChannelType);
    }

    public IChannelHandler? GetHandler(ChannelType channelType)
    {
        return _handlers.GetValueOrDefault(channelType);
    }

    public async Task<ChannelMessage> ProcessMessageAsync(ChannelMessage incomingMessage, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing message from channel {ChannelId}, session {SessionId}",
            incomingMessage.ChannelId, incomingMessage.SessionId);

        // Load channel definition
        var channel = await _channelRepo.GetByIdAsync(incomingMessage.ChannelId, incomingMessage.TenantId, ct);
        if (channel == null)
            throw new InvalidOperationException($"Channel {incomingMessage.ChannelId} not found");

        var handler = GetHandler(channel.Type);
        if (handler == null)
            throw new InvalidOperationException($"No handler registered for channel type {channel.Type}");

        // Save incoming message
        incomingMessage.Status = MessageStatus.Processing;
        await _messageRepo.InsertAsync(incomingMessage, ct);

        try
        {
            var session = await _sessionRepo.GetByIdAsync(incomingMessage.SessionId, incomingMessage.TenantId, ct);
            var agentKey = ResolveAgentKey(channel, session);

            if (session != null)
            {
                session.LinkAgent(agentKey);
                await _sessionRepo.UpdateAsync(session, ct);
            }

            // Build typed session context so every agent knows who it's talking to
            // and whether the conversation window is open, without parsing ContextJson.
            var sessionContext = session != null ? new AgentSessionContext
            {
                SessionId      = session.Id,
                UserIdentifier = session.Identifier,
                DisplayName    = session.Metadata.GetValueOrDefault("display_name"),
                ChannelType    = channel.Type.ToString(),
                ChannelId      = channel.Id,
                IsWindowOpen   = !session.IsExpired(),
                WindowHours    = channel.SessionWindowHours,
                WindowExpiresAt = session.ExpiresAt
            } : null;

            // Execute agent
            // When the Router is executing, inject the intent catalog for this channel
            // so the LLM can classify messages without extra tool calls.
            var intentCatalogJson = (string?)null;
            if (channel.RouterAgentId == agentKey || session?.AgentId == channel.RouterAgentId)
            {
                var rules = await _intentRoutingStore.GetRulesByChannelAsync(
                    incomingMessage.TenantId, channel.Type.ToString().ToLowerInvariant(), ct);
                if (rules is { Count: > 0 })
                {
                    intentCatalogJson = System.Text.Json.JsonSerializer.Serialize(
                        rules.Select(r => new
                        {
                            intentKey        = r.IntentKey,
                            description      = r.IntentDescription,
                            examplePhrases   = r.ExamplePhrases,
                            targetAgentId    = r.TargetAgentId,
                            workflowId       = r.WorkflowDefinitionId
                        }));
                }
            }

            var ambientContext = _tenantContext.Current;
            var executionContext = ambientContext ?? new TenantContext
            {
                TenantId = incomingMessage.TenantId,
                UserId = incomingMessage.From,
                IsPlatformAdmin = false,
                Roles = new[] { "developer" },
                Permissions = AgentFlowRoles.Developer.ToList()
            };
            if (ambientContext is null)
                _tenantContext.Set(executionContext);

            var executionRequest = new AgentExecutionRequest
            {
                TenantId = incomingMessage.TenantId,
                AgentKey = agentKey,
                UserId = executionContext.UserId,
                UserMessage = incomingMessage.Content,
                ContextJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    ChannelType = channel.Type.ToString(),
                    ChannelId = channel.Id,
                    SessionId = incomingMessage.SessionId,
                    From = incomingMessage.From,
                    // Injected for the Router: the full intent catalog for this channel.
                    // The Router uses this to emit the correct routing_handoff directive
                    // (targetAgentId + workflowId) without calling af_list_workflows.
                    IntentCatalog = intentCatalogJson
                }),
                CorrelationId = incomingMessage.SessionId,
                ThreadId = session?.ThreadId,
                Priority = ExecutionPriority.Normal,
                SessionContext = sessionContext,
                Metadata = new Dictionary<string, string>
                {
                    // Pass the originating message ID so AgentExecutionEngine
                    // can stamp it into AgentExecution.ChannelMessageId
                    ["channelMessageId"] = incomingMessage.Id,
                    ["permissions"] = string.Join(",", executionContext.Permissions),
                    ["mcp.policy.allow_actions"] = "tools.execute",
                    ["routing.intent_confidence_threshold"] = channel.Config.GetValueOrDefault("IntentConfidenceThreshold") ?? "0.70",
                    ["routing.assistant_confidence_threshold"] = channel.Config.GetValueOrDefault("AssistantConfidenceThreshold") ?? "0.80"
                }
            };

            var executionResult = await _agentExecutor.ExecuteAsync(executionRequest, ct);
            incomingMessage.LinkExecution(executionResult.ExecutionId);
            if (session != null)
            {
                session.LinkThreadIfMissing(executionResult.ThreadId);
                await _sessionRepo.UpdateAsync(session, ct);
            }

            if (executionResult.Status == ExecutionStatus.Failed ||
                string.IsNullOrWhiteSpace(executionResult.FinalResponse))
            {
                await MarkInboundFailureWithoutCustomerReplyAsync(
                    incomingMessage,
                    executionResult,
                    "agent_execution_failed",
                    ct);
                return incomingMessage;
            }

            var finalResponse = executionResult.FinalResponse;
            var executionIdForOutgoing = executionResult.ExecutionId;
            var respondingAgentKey = executionResult.AgentKey;

            // ── Router → WorkflowBrain session handoff ────────────────────────
            // When the Router emits a routing_handoff directive, re-assign the
            // session to the WorkflowBrain agent. From the next message onward,
            // ResolveAgentKey will return the WorkflowBrain (sticky routing).
            // The Router does NOT send a visible reply to the customer — the
            // WorkflowBrain will greet and continue the conversation.
            var routingHandoff = TryParseRoutingHandoff(finalResponse);
            if (routingHandoff != null && session != null)
            {
                _logger.LogInformation(
                    "Router handed off session {SessionId} to WorkflowBrain {AgentId} (workflow: {WorkflowId}, intent: {Intent})",
                    session.Id, routingHandoff.WorkflowBrainAgentId,
                    routingHandoff.WorkflowExecutionId, routingHandoff.Intent);

                session.LinkAgent(routingHandoff.WorkflowBrainAgentId);
                session.Metadata["routing_handoff_workflow"] = routingHandoff.WorkflowExecutionId ?? string.Empty;
                session.Metadata["routing_handoff_intent"]   = routingHandoff.Intent ?? string.Empty;
                session.Metadata["routing_handoff_at"]       = DateTimeOffset.UtcNow.ToString("O");
                await _sessionRepo.UpdateAsync(session, ct);

                // Now execute the WorkflowBrain as the first turn of the workflow
                var brainRequest = executionRequest with
                {
                    AgentKey  = routingHandoff.WorkflowBrainAgentId,
                    Metadata  = new Dictionary<string, string>(executionRequest.Metadata)
                    {
                        ["channelMessageId"]      = incomingMessage.Id,
                        ["routerExecutionId"]     = executionResult.ExecutionId,
                        ["workflowExecutionId"]   = routingHandoff.WorkflowExecutionId ?? string.Empty,
                        ["routingIntent"]         = routingHandoff.Intent ?? string.Empty
                    }
                };

                var brainResult = await _agentExecutor.ExecuteAsync(brainRequest, ct);
                if (brainResult.Status == ExecutionStatus.Failed ||
                    string.IsNullOrWhiteSpace(brainResult.FinalResponse))
                {
                    incomingMessage.LinkExecution(brainResult.ExecutionId);
                    await MarkInboundFailureWithoutCustomerReplyAsync(
                        incomingMessage,
                        brainResult,
                        "workflow_brain_execution_failed",
                        ct);
                    return incomingMessage;
                }

                finalResponse = brainResult.FinalResponse;
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
                await _messageRepo.InsertAsync(transitionMessage, ct);
            }
            else
            {
                // Standard A2A handoff (agent-to-agent delegation)
                var handoff = TryParseHandoffDirective(finalResponse);
                if (handoff is not null && session is not null)
                {
                    if (_handoffPolicy.IsAllowed(incomingMessage.TenantId, agentKey, handoff.TargetAgentId))
                    {
                        var handoffResponse = await _handoffExecutor.ExecuteAsync(new AgentHandoffRequest
                        {
                            TenantId = incomingMessage.TenantId,
                            SessionId = incomingMessage.SessionId,
                            ThreadId = session.ThreadId ?? incomingMessage.SessionId,
                            CorrelationId = incomingMessage.SessionId,
                            SourceAgentKey = agentKey,
                            TargetAgentKey = handoff.TargetAgentId,
                            Intent = handoff.Intent,
                            PayloadJson = handoff.PayloadJson,
                            Metadata = new Dictionary<string, string>
                            {
                                ["channelId"] = incomingMessage.ChannelId,
                                ["source"] = "channel-gateway"
                            }
                        }, ct);

                        if (handoffResponse.Ok)
                        {
                            finalResponse = ExtractResponseText(handoffResponse.ResultJson) ?? handoffResponse.ResultJson;
                            executionIdForOutgoing = handoffResponse.StatePatch.TryGetValue("lastExecutionId", out var delegatedId)
                                ? delegatedId
                                : executionIdForOutgoing;
                            respondingAgentKey = handoff.TargetAgentId;

                            session.LinkAgent(handoff.TargetAgentId);
                            await _sessionRepo.UpdateAsync(session, ct);

                            if (string.IsNullOrWhiteSpace(finalResponse))
                            {
                                await MarkInboundFailureWithoutCustomerReplyAsync(
                                    incomingMessage,
                                    CreateGatewayFailureResult(executionIdForOutgoing, agentKey, "handoff_empty_response", "Delegated agent produced no customer-safe reply."),
                                    "agent_handoff_failed",
                                    ct);
                                return incomingMessage;
                            }
                        }
                        else
                        {
                            await MarkInboundFailureWithoutCustomerReplyAsync(
                                incomingMessage,
                                CreateGatewayFailureResult(executionIdForOutgoing, agentKey, "handoff_failed", "Agent handoff failed."),
                                "agent_handoff_failed",
                                ct);
                            return incomingMessage;
                        }
                    }
                    else
                    {
                        await MarkInboundFailureWithoutCustomerReplyAsync(
                            incomingMessage,
                            CreateGatewayFailureResult(executionIdForOutgoing, agentKey, "handoff_policy_denied", "Agent handoff target is not allowed by policy."),
                            "agent_handoff_policy_denied",
                            ct);
                        return incomingMessage;
                    }
                }
            }

            // Create outgoing message
            var customerResponse = finalResponse!;
            if (ShouldSuppressCustomerDelivery(customerResponse))
            {
                var systemMessage = ChannelMessage.CreateOutgoing(
                    incomingMessage.TenantId,
                    incomingMessage.ChannelId,
                    incomingMessage.SessionId,
                    incomingMessage.From,
                    customerResponse
                );
                systemMessage.Metadata["actor"] = "system";
                systemMessage.Metadata["actor_label"] = "Sistema";
                systemMessage.Metadata["agentflow.delivery"] = "suppressed";
                systemMessage.Metadata["agentflow.visibility"] = "inbox_only";
                systemMessage.LinkExecution(executionIdForOutgoing);
                await _messageRepo.InsertAsync(systemMessage, ct);

                var suppressSession = await _sessionRepo.GetByIdAsync(incomingMessage.SessionId, incomingMessage.TenantId, ct);
                if (suppressSession != null)
                {
                    suppressSession.RecordOutgoingMessage("[system] Mensaje interno suprimido para el cliente.");
                    await _sessionRepo.UpdateAsync(suppressSession, ct);
                }

                await RecordOutgoingAuditAsync(
                    incomingMessage,
                    executionIdForOutgoing,
                    "suppressed",
                    customerResponse,
                    true,
                    ct);

                return systemMessage;
            }

            var outgoingMessage = ChannelMessage.CreateOutgoing(
                incomingMessage.TenantId,
                incomingMessage.ChannelId,
                incomingMessage.SessionId,
                incomingMessage.From,
                customerResponse
            );
            outgoingMessage.Metadata["actor"] = "bot";
            outgoingMessage.Metadata["actor_agent_id"] = respondingAgentKey;
            outgoingMessage.Metadata["actor_label"] = string.IsNullOrWhiteSpace(respondingAgentKey)
                ? "Agente"
                : $"Agente {respondingAgentKey}";
            if (session is not null && session.Metadata.TryGetValue("routing_handoff_workflow", out var workflowId) && !string.IsNullOrWhiteSpace(workflowId))
            {
                outgoingMessage.Metadata["workflow_execution_id"] = workflowId;
            }
            outgoingMessage.Metadata["agentflow.delivery"] = "sent";

            outgoingMessage.LinkExecution(executionIdForOutgoing);

            // Send reply through channel
            var sendResult = await SendMessageAsync(incomingMessage.ChannelId, outgoingMessage, ct);
            if (!sendResult.Success)
            {
                _logger.LogError("Failed to send reply: {Error}", sendResult.Error);
            }
            await RecordOutgoingAuditAsync(
                incomingMessage,
                executionIdForOutgoing,
                sendResult.Success ? "sent" : "failed",
                customerResponse,
                false,
                ct);

            return outgoingMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from channel {ChannelId}", incomingMessage.ChannelId);
            incomingMessage.MarkFailed(ex.Message);
            incomingMessage.Metadata["agentflow.delivery"] = "not_sent";
            incomingMessage.Metadata["agentflow.failure_level"] = "channel";
            incomingMessage.Metadata["agentflow.error"] = ex.Message;
            await _messageRepo.UpdateAsync(incomingMessage, ct);
            var session = await _sessionRepo.GetByIdAsync(incomingMessage.SessionId, incomingMessage.TenantId, ct);
            if (session != null)
            {
                session.MarkReplyFailure(ex.Message, "channel", "Failed");
                await _sessionRepo.UpdateAsync(session, ct);
            }
            throw;
        }
    }

    private async Task MarkInboundFailureWithoutCustomerReplyAsync(
        ChannelMessage incomingMessage,
        AgentExecutionResult executionResult,
        string failureLevel,
        CancellationToken ct)
    {
        var error = executionResult.ErrorMessage
            ?? executionResult.ErrorCode
            ?? "Agent execution produced no customer-safe reply.";

        incomingMessage.MarkFailed(error);
        incomingMessage.Metadata["agentflow.delivery"] = "not_sent";
        incomingMessage.Metadata["agentflow.failure_level"] = failureLevel;
        incomingMessage.Metadata["agentflow.error_code"] = executionResult.ErrorCode ?? string.Empty;
        incomingMessage.Metadata["agentflow.error"] = error;
        incomingMessage.Metadata["agentflow.execution_status"] = executionResult.Status.ToString();

        await _messageRepo.UpdateAsync(incomingMessage, ct);
        var session = await _sessionRepo.GetByIdAsync(incomingMessage.SessionId, incomingMessage.TenantId, ct);
        if (session != null)
        {
            session.MarkReplyFailure(error, failureLevel, executionResult.Status.ToString());
            session.LinkThreadIfMissing(executionResult.ThreadId);
            await _sessionRepo.UpdateAsync(session, ct);
        }
        await _auditMemory.RecordAsync(new AuditEntry
        {
            ExecutionId = executionResult.ExecutionId,
            AgentId = executionResult.AgentKey,
            TenantId = incomingMessage.TenantId,
            UserId = incomingMessage.From,
            EventType = AuditEventType.ExecutionFailed,
            CorrelationId = incomingMessage.SessionId,
            EventJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                level = failureLevel,
                channelId = incomingMessage.ChannelId,
                sessionId = incomingMessage.SessionId,
                channelMessageId = incomingMessage.Id,
                customerReplySent = false,
                executionResult.ErrorCode,
                error
            })
        }, ct);
    }

    private static AgentExecutionResult CreateGatewayFailureResult(
        string executionId,
        string agentKey,
        string errorCode,
        string errorMessage) => new()
    {
        ExecutionId = executionId,
        AgentKey = agentKey,
        AgentVersion = "channel-gateway",
        Status = ExecutionStatus.Failed,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };

    public async Task<SendResult> SendMessageAsync(string channelId, ChannelMessage message, CancellationToken ct = default)
    {
        var channel = await _channelRepo.GetByIdAsync(channelId, message.TenantId, ct);
        if (channel == null)
            return SendResult.Fail($"Channel {channelId} not found");

        var handler = GetHandler(channel.Type);
        if (handler == null)
            return SendResult.Fail($"No handler for channel type {channel.Type}");

        await _messageRepo.InsertAsync(message, ct);
        var result = await handler.SendReplyAsync(message, channel, ct);

        var session = await _sessionRepo.GetByIdAsync(message.SessionId, message.TenantId, ct);
        if (session != null)
        {
            if (result.Success)
                session.RecordOutgoingMessage(message.Content);
            else
                session.MarkReplyFailure(result.Error ?? "Failed to send message.", "channel_send_failed", "Failed");

            await _sessionRepo.UpdateAsync(session, ct);
        }

        return result;
    }

    private static bool ShouldSuppressCustomerDelivery(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return false;
        var text = response.Trim().ToLowerInvariant();
        return text.Contains("herramienta de sesión activa no está disponible", StringComparison.OrdinalIgnoreCase)
            || text.Contains("herramienta de sesion activa no esta disponible", StringComparison.OrdinalIgnoreCase)
            || text.Contains("no hay suficiente contexto comercial", StringComparison.OrdinalIgnoreCase)
            || text.Contains("tool", StringComparison.OrdinalIgnoreCase) && text.Contains("not available", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RecordOutgoingAuditAsync(
        ChannelMessage incomingMessage,
        string executionId,
        string delivery,
        string response,
        bool systemOnly,
        CancellationToken ct)
    {
        await _auditMemory.RecordAsync(new AuditEntry
        {
            ExecutionId = executionId,
            AgentId = "channel-gateway",
            TenantId = incomingMessage.TenantId,
            UserId = incomingMessage.From,
            EventType = AuditEventType.ConnectOperation,
            CorrelationId = incomingMessage.SessionId,
            EventJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                action = "channel.outgoing.reply",
                channelId = incomingMessage.ChannelId,
                sessionId = incomingMessage.SessionId,
                delivery,
                systemOnly,
                responsePreview = response.Length > 280 ? response[..280] : response
            })
        }, ct);
    }

    public async Task<IReadOnlyList<ChannelSession>> GetActiveSessionsAsync(string channelId, string tenantId, CancellationToken ct = default)
    {
        return await _sessionRepo.GetActiveByChannelAsync(channelId, tenantId, ct);
    }

    public async Task CloseSessionAsync(string sessionId, string tenantId, CancellationToken ct = default)
    {
        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session != null)
        {
            session.Close();
            await _sessionRepo.UpdateAsync(session, ct);
        }
    }

    public async Task<BroadcastResult> BroadcastAsync(string channelId, string tenantId, string content, CancellationToken ct = default)
    {
        var sessions = await GetActiveSessionsAsync(channelId, tenantId, ct);
        var failedIds = new List<string>();

        foreach (var session in sessions)
        {
            try
            {
                var message = ChannelMessage.CreateOutgoing(tenantId, channelId, session.Id, session.Identifier, content);
                await SendMessageAsync(channelId, message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast to session {SessionId}", session.Id);
                failedIds.Add(session.Id);
            }
        }

        var successCount = sessions.Count - failedIds.Count;
        return failedIds.Count == 0
            ? BroadcastResult.Ok(successCount)
            : BroadcastResult.Partial(successCount, failedIds.Count, failedIds);
    }

    private static HandoffDirective? TryParseHandoffDirective(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "handoff", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!root.TryGetProperty("targetAgentId", out var targetEl) || string.IsNullOrWhiteSpace(targetEl.GetString()))
                return null;

            var intent = root.TryGetProperty("intent", out var intentEl) && !string.IsNullOrWhiteSpace(intentEl.GetString())
                ? intentEl.GetString()!
                : "delegated_task";

            var payloadJson = root.TryGetProperty("payload", out var payloadEl)
                ? payloadEl.GetRawText()
                : "{}";

            return new HandoffDirective(targetEl.GetString()!, intent, payloadJson);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractResponseText(string? responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
            return responseJson;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (root.TryGetProperty("message", out var msg) && msg.ValueKind == System.Text.Json.JsonValueKind.String)
                    return msg.GetString();
                if (root.TryGetProperty("finalResponse", out var final) && final.ValueKind == System.Text.Json.JsonValueKind.String)
                    return final.GetString();
            }
        }
        catch
        {
            // response is plain text
        }

        return responseJson;
    }

    private sealed record HandoffDirective(string TargetAgentId, string Intent, string PayloadJson);

    /// <summary>
    /// Emitted by the Router agent when it successfully triggers a workflow.
    /// The gateway re-assigns the session to the WorkflowBrain agent and the
    /// Router stops responding until the workflow completes or the session resets.
    /// 
    /// JSON shape the Router must emit:
    /// { "type": "routing_handoff", "workflowBrainAgentId": "...", "workflowExecutionId": "...", "intent": "..." }
    /// </summary>
    private sealed record RoutingHandoffDirective(
        string WorkflowBrainAgentId,
        string? WorkflowExecutionId,
        string? Intent);

    private static RoutingHandoffDirective? TryParseRoutingHandoff(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "routing_handoff", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!root.TryGetProperty("workflowBrainAgentId", out var agentEl) ||
                string.IsNullOrWhiteSpace(agentEl.GetString()))
                return null;

            var execId = root.TryGetProperty("workflowExecutionId", out var execEl) ? execEl.GetString() : null;
            var intent = root.TryGetProperty("intent", out var intentEl) ? intentEl.GetString() : null;
            return new RoutingHandoffDirective(agentEl.GetString()!, execId, intent);
        }
        catch { return null; }
    }

    private static string ResolveAgentKey(ChannelDefinition channel, ChannelSession? session)
    {
        // Sticky routing: preserve owner agent for the current session.
        if (!string.IsNullOrWhiteSpace(session?.AgentId))
            return session.AgentId!;

        if (!string.IsNullOrWhiteSpace(channel.RouterAgentId))
            return channel.RouterAgentId!;

        return channel.Config.GetValueOrDefault("DefaultAgentId")
            ?? channel.Metadata?.GetValueOrDefault("DefaultAgentId")
            ?? "default-agent";
    }

}
