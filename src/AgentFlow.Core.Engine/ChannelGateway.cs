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
    private readonly IChannelExecutionRequestFactory _executionRequestFactory;
    private readonly IAuditMemory _auditMemory;
    private readonly IChannelDeliveryPolicy _deliveryPolicy;
    private readonly IChannelCapabilityPolicy _capabilityPolicy;
    private readonly IChannelSpamReputationRepository _spamReputationRepo;
    private readonly IHumanEscalationNotifier? _humanEscalationNotifier;
    private readonly ITenantRuntimeSettingsReader? _tenantRuntimeSettingsReader;
    private readonly ILogger<ChannelGateway> _logger;

    public ChannelGateway(
        IChannelDefinitionRepository channelRepo,
        IChannelSessionRepository sessionRepo,
        IChannelMessageRepository messageRepo,
        IAgentExecutor agentExecutor,
        IAgentHandoffExecutor handoffExecutor,
        IManagerHandoffPolicy handoffPolicy,
        IChannelExecutionRequestFactory executionRequestFactory,
        IAgentDefinitionRepository agentRepo,
        IAuditMemory auditMemory,
        IChannelCapabilityPolicy capabilityPolicy,
        IChannelSpamReputationRepository spamReputationRepo,
        ITenantRuntimeSettingsReader? tenantRuntimeSettingsReader,
        IEnumerable<IChannelHandler> handlers,
        IHumanEscalationNotifier? humanEscalationNotifier,
        ILogger<ChannelGateway> logger,
        IChannelDeliveryPolicy? deliveryPolicy = null)
    {
        _channelRepo = channelRepo;
        _sessionRepo = sessionRepo;
        _messageRepo = messageRepo;
        _agentExecutor = agentExecutor;
        _handoffExecutor = handoffExecutor;
        _handoffPolicy = handoffPolicy;
        _executionRequestFactory = executionRequestFactory;
        _auditMemory = auditMemory;
        _capabilityPolicy = capabilityPolicy;
        _spamReputationRepo = spamReputationRepo;
        _tenantRuntimeSettingsReader = tenantRuntimeSettingsReader;
        _humanEscalationNotifier = humanEscalationNotifier;
        _deliveryPolicy = deliveryPolicy ?? new ChannelDeliveryPolicy(agentRepo, auditMemory);
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

        if (incomingMessage.Direction == MessageDirection.Incoming &&
            !string.IsNullOrWhiteSpace(incomingMessage.ExternalMessageId))
        {
            var existingIncoming = await _messageRepo.GetByExternalMessageIdAsync(
                incomingMessage.TenantId,
                incomingMessage.ChannelId,
                incomingMessage.ExternalMessageId,
                MessageDirection.Incoming,
                ct);

            if (existingIncoming is not null)
            {
                _logger.LogInformation(
                    "Skipping duplicate inbound message. Tenant={TenantId} Channel={ChannelId} ExternalMessageId={ExternalMessageId} ExistingMessageId={ExistingMessageId}",
                    incomingMessage.TenantId,
                    incomingMessage.ChannelId,
                    incomingMessage.ExternalMessageId,
                    existingIncoming.Id);

                if (!string.IsNullOrWhiteSpace(existingIncoming.AgentExecutionId))
                {
                    var existingOutgoing = await _messageRepo.GetLatestOutgoingByExecutionIdAsync(
                        incomingMessage.TenantId,
                        existingIncoming.AgentExecutionId,
                        ct);
                    if (existingOutgoing is not null)
                        return existingOutgoing;
                }

                return existingIncoming;
            }
        }

        // Load channel definition
        var channel = await _channelRepo.GetByIdAsync(incomingMessage.ChannelId, incomingMessage.TenantId, ct);
        if (channel == null)
            throw new InvalidOperationException($"Channel {incomingMessage.ChannelId} not found");

        var handler = GetHandler(channel.Type);
        if (handler == null)
            throw new InvalidOperationException($"No handler registered for channel type {channel.Type}");
        _capabilityPolicy.EnsureSupportsAny(channel, incomingMessage.Id, "text.send", "call.control", "call.outbound");

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

            var inboundGuard = await EvaluateInboundGuardAsync(incomingMessage, channel, session, agentKey, ct);
            if (inboundGuard is not null)
            {
                incomingMessage.LinkExecution(inboundGuard.ExecutionId);
                incomingMessage.Metadata["agentflow.delivery"] = "guarded";
                incomingMessage.Metadata["agentflow.inbound_guard_reason"] = inboundGuard.ReasonCode;
                incomingMessage.Metadata["agentflow.execution_status"] = "Guarded";
                await _messageRepo.UpdateAsync(incomingMessage, ct);

                if (inboundGuard.NotifyHumanReview &&
                    !string.IsNullOrWhiteSpace(inboundGuard.EscalationTarget) &&
                    _humanEscalationNotifier is not null)
                {
                    try
                    {
                        var notifyResult = await _humanEscalationNotifier.NotifyAsync(
                            new HumanEscalationNotificationRequest
                            {
                                TenantId = incomingMessage.TenantId,
                                QueueId = inboundGuard.EscalationTarget,
                                ConversationId = incomingMessage.SessionId,
                                UserId = incomingMessage.From,
                                Channel = channel.Type.ToString().ToLowerInvariant(),
                                LastMessage = incomingMessage.Content,
                                ReasonCode = inboundGuard.ReasonCode,
                                ExecutionId = inboundGuard.ExecutionId,
                                CorrelationId = incomingMessage.SessionId
                            },
                            ct);

                        if (session is not null)
                        {
                            session.Metadata["routing.fallback.escalation_status"] = notifyResult.Delivered ? "delivered" : "failed";
                            if (!string.IsNullOrWhiteSpace(notifyResult.TicketId))
                                session.Metadata["routing.fallback.ticket_id"] = notifyResult.TicketId;
                            if (!string.IsNullOrWhiteSpace(notifyResult.QueueId))
                                session.Metadata["routing.fallback.queue_id"] = notifyResult.QueueId;
                            await _sessionRepo.UpdateAsync(session, ct);
                        }
                    }
                    catch (Exception notifyEx)
                    {
                        _logger.LogWarning(
                            notifyEx,
                            "Inbound guard escalation failed. Tenant={TenantId} Session={SessionId} Queue={QueueId}",
                            incomingMessage.TenantId,
                            incomingMessage.SessionId,
                            inboundGuard.EscalationTarget);

                        if (session is not null)
                        {
                            session.Metadata["routing.fallback.escalation_status"] = "failed";
                            await _sessionRepo.UpdateAsync(session, ct);
                        }
                    }
                }

                await _auditMemory.RecordAsync(new AuditEntry
                {
                    ExecutionId = inboundGuard.ExecutionId,
                    AgentId = agentKey,
                    TenantId = incomingMessage.TenantId,
                    UserId = incomingMessage.From,
                    EventType = AuditEventType.RoutingDecision,
                    CorrelationId = incomingMessage.SessionId,
                    EventJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        action = "inbound_guard_blocked",
                        reason = inboundGuard.ReasonCode,
                        channelId = incomingMessage.ChannelId,
                        sessionId = incomingMessage.SessionId,
                        escalationTarget = inboundGuard.EscalationTarget
                    })
                }, ct);

                if (!inboundGuard.ShouldSendCustomerReply)
                {
                    incomingMessage.Metadata["agentflow.delivery"] = "suppressed";
                    incomingMessage.Metadata["agentflow.visibility"] = "inbox_only";
                    await _messageRepo.UpdateAsync(incomingMessage, ct);
                    return incomingMessage;
                }

                var guardedReply = ChannelMessage.CreateOutgoing(
                    incomingMessage.TenantId,
                    incomingMessage.ChannelId,
                    incomingMessage.SessionId,
                    incomingMessage.From,
                    inboundGuard.CustomerMessage);
                guardedReply.Metadata["actor"] = "system";
                guardedReply.Metadata["actor_label"] = "Sistema";
                guardedReply.Metadata["actor_agent_id"] = agentKey;
                guardedReply.Metadata["agentflow.delivery"] = "sent";
                guardedReply.Metadata["agentflow.safe_fallback"] = "true";
                guardedReply.Metadata["agentflow.inbound_guard"] = "true";
                guardedReply.LinkExecution(inboundGuard.ExecutionId);

                var guardedSend = await SendMessageAsync(incomingMessage.ChannelId, guardedReply, ct);
                if (!guardedSend.Success)
                    _logger.LogWarning("Inbound guard reply blocked or failed: {Error}", guardedSend.Error);

                return guardedReply;
            }

            var executionRequest = await _executionRequestFactory.CreateAsync(
                incomingMessage,
                channel,
                session,
                agentKey,
                ct);

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

                        var continuation = await ChannelPostExecutionCoordinator.ContinueAsync(
                incomingMessage,
                channel,
                session,
                executionRequest,
                executionResult,
                _agentExecutor,
                _handoffExecutor,
                _handoffPolicy,
                _humanEscalationNotifier,
                _sessionRepo,
                _messageRepo,
                _logger,
                ct);

            if (string.IsNullOrWhiteSpace(continuation.FinalResponse))
            {
                await MarkInboundFailureWithoutCustomerReplyAsync(
                    incomingMessage,
                    CreateGatewayFailureResult(
                        continuation.ExecutionIdForOutgoing,
                        executionResult.AgentKey,
                        "post_execution_failed",
                        "Post-execution orchestration produced no customer-safe reply."),
                    "post_execution_failed",
                    ct);
                return incomingMessage;
            }

            if (continuation.SuppressCustomerReply)
            {
                incomingMessage.Metadata["agentflow.delivery"] = "suppressed";
                incomingMessage.Metadata["agentflow.visibility"] = "inbox_only";
                incomingMessage.Metadata["agentflow.execution_status"] = executionResult.Status.ToString();
                await _messageRepo.UpdateAsync(incomingMessage, ct);
                if (session is not null)
                {
                    UpdateSessionGuardStage(session, "accumulating");
                    await _sessionRepo.UpdateAsync(session, ct);
                }
                return incomingMessage;
            }

            var finalResponse = continuation.FinalResponse;
            var executionIdForOutgoing = continuation.ExecutionIdForOutgoing;
            var respondingAgentKey = continuation.RespondingAgentKey;

            if (session is not null &&
                string.IsNullOrWhiteSpace(session.Metadata.GetValueOrDefault("routing.fallback.state")))
            {
                UpdateSessionGuardStage(session, "classified");
                session.Metadata["routing.guard.last_classified_at"] = DateTimeOffset.UtcNow.ToString("O");
                await _sessionRepo.UpdateAsync(session, ct);
            }

            // Create outgoing message
            var customerResponse = finalResponse!;
            if (ChannelGatewayResponseInterpreter.ShouldSuppressCustomerDelivery(customerResponse))
            {
                var customerSafeResponse = await _deliveryPolicy.BuildCustomerSafeFallbackAsync(
                    incomingMessage.TenantId,
                    respondingAgentKey,
                    ct);
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
                    suppressSession.Metadata["requires_human_review"] = "true";
                    suppressSession.Metadata["reply_pending"] = "true";
                    await _sessionRepo.UpdateAsync(suppressSession, ct);
                }

                await _deliveryPolicy.RecordOutgoingAuditAsync(
                    incomingMessage,
                    executionIdForOutgoing,
                    "suppressed",
                    customerResponse,
                    true,
                    ct);

                var safeMessage = ChannelMessage.CreateOutgoing(
                    incomingMessage.TenantId,
                    incomingMessage.ChannelId,
                    incomingMessage.SessionId,
                    incomingMessage.From,
                    customerSafeResponse
                );
                safeMessage.Metadata["actor"] = "bot";
                safeMessage.Metadata["actor_agent_id"] = respondingAgentKey;
                safeMessage.Metadata["actor_label"] = string.IsNullOrWhiteSpace(respondingAgentKey)
                    ? "Agente"
                    : $"Agente {respondingAgentKey}";
                safeMessage.Metadata["agentflow.delivery"] = "sent";
                safeMessage.Metadata["agentflow.safe_fallback"] = "true";
                safeMessage.LinkExecution(executionIdForOutgoing);

                var safeSendResult = await SendMessageAsync(incomingMessage.ChannelId, safeMessage, ct);
                await _deliveryPolicy.RecordOutgoingAuditAsync(
                    incomingMessage,
                    executionIdForOutgoing,
                    safeSendResult.Success ? "sent" : "failed",
                    customerSafeResponse,
                    false,
                    ct);

                return safeMessage;
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
            await _deliveryPolicy.RecordOutgoingAuditAsync(
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
        _capabilityPolicy.EnsureSupportsAny(channel, message.Id, "text.send", "call.control", "call.outbound");

        var outboundGuardError = await EvaluateOutboundGuardAsync(message, ct);
        if (!string.IsNullOrWhiteSpace(outboundGuardError))
        {
            message.MarkFailed(outboundGuardError);
            message.Metadata["agentflow.delivery"] = "blocked";
            message.Metadata["agentflow.failure_level"] = "outbound_guard";
            message.Metadata["agentflow.error"] = outboundGuardError;
            await _messageRepo.InsertAsync(message, ct);

            var blockedSession = await _sessionRepo.GetByIdAsync(message.SessionId, message.TenantId, ct);
            if (blockedSession != null)
            {
                blockedSession.MarkReplyFailure(outboundGuardError, "outbound_guard", "Blocked");
                await _sessionRepo.UpdateAsync(blockedSession, ct);
            }

            await _auditMemory.RecordAsync(new AuditEntry
            {
                ExecutionId = message.AgentExecutionId ?? message.Id,
                AgentId = message.Metadata.GetValueOrDefault("actor_agent_id") ?? "channel-gateway",
                TenantId = message.TenantId,
                UserId = message.To ?? message.From,
                EventType = AuditEventType.RoutingDecision,
                CorrelationId = message.SessionId,
                EventJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    action = "outbound_guard_blocked",
                    reason = outboundGuardError,
                    channelId,
                    sessionId = message.SessionId
                }),
                OccurredAt = DateTimeOffset.UtcNow
            }, CancellationToken.None);

            return SendResult.Fail(outboundGuardError);
        }

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

    private async Task<InboundGuardDecision?> EvaluateInboundGuardAsync(
        ChannelMessage incomingMessage,
        ChannelDefinition channel,
        ChannelSession? session,
        string agentKey,
        CancellationToken ct)
    {
        if (incomingMessage.Direction != MessageDirection.Incoming || session is null)
            return null;

        var routingConfig = ReadInboundRoutingConfig(channel);
        var identifier = string.IsNullOrWhiteSpace(session.Identifier) ? incomingMessage.From : session.Identifier;
        var existingReputation = await _spamReputationRepo.GetAsync(incomingMessage.TenantId, channel.Id, identifier, ct);
        var currentState = session.Metadata.GetValueOrDefault("routing.fallback.state") ?? string.Empty;
        var guardStage = session.Metadata.GetValueOrDefault("routing.guard.stage") ?? "accumulating";
        var escalationTarget = routingConfig.EscalationTarget
            ?? session.Metadata.GetValueOrDefault("routing.fallback.escalation_target")
            ?? string.Empty;
        var spamEscalationTarget = routingConfig.SpamEscalationTarget
            ?? escalationTarget;

        if (existingReputation is not null &&
            existingReputation.Status is SpamReputationStatus.Suspected or SpamReputationStatus.ConfirmedSpam)
        {
            UpdateSessionGuardStage(session, "spam_review");
            session.Metadata["routing.fallback.state"] = "spam_review";
            session.Metadata["routing.fallback.turn"] = "0";
            session.Metadata["routing.fallback.reason"] = "spam_reputation_match";
            session.Metadata["requires_human_review"] = "true";
            session.Metadata["reply_pending"] = "true";
            session.Metadata["routing.spam.status"] = existingReputation.Status.ToString();
            if (!string.IsNullOrWhiteSpace(spamEscalationTarget))
                session.Metadata["routing.fallback.escalation_target"] = spamEscalationTarget;
            await _sessionRepo.UpdateAsync(session, ct);

            return new InboundGuardDecision(
                Guid.NewGuid().ToString("N"),
                "spam_reputation_match",
                string.Empty,
                spamEscalationTarget,
                !string.IsNullOrWhiteSpace(spamEscalationTarget) &&
                !string.Equals(session.Metadata.GetValueOrDefault("routing.fallback.escalation_status"), "delivered", StringComparison.OrdinalIgnoreCase),
                false);
        }

        if (string.Equals(currentState, "escalated_human", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentState, "pending_human_review", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentState, "spam_review", StringComparison.OrdinalIgnoreCase))
        {
            return new InboundGuardDecision(
                Guid.NewGuid().ToString("N"),
                string.Equals(currentState, "spam_review", StringComparison.OrdinalIgnoreCase)
                    ? "session_in_spam_review"
                    : string.Equals(currentState, "pending_human_review", StringComparison.OrdinalIgnoreCase)
                        ? "session_pending_human_review"
                    : "session_already_escalated",
                string.Empty,
                string.Equals(currentState, "spam_review", StringComparison.OrdinalIgnoreCase)
                    ? spamEscalationTarget
                    : escalationTarget,
                false,
                false);
        }

        var historyLimit = Math.Max(25, routingConfig.MaxUnclassifiedMessagesBeforeEscalation + routingConfig.HistoryWindowMessagesForClassification + 4);
        var history = await _messageRepo.GetBySessionAsync(incomingMessage.SessionId, incomingMessage.TenantId, historyLimit, ct);
        var inboundHistory = history
            .Where(x => x.Direction == MessageDirection.Incoming)
            .OrderBy(x => x.CreatedAt)
            .ToList();
        var inboundCount = inboundHistory.Count;
        var recentWindow = inboundHistory.TakeLast(routingConfig.HistoryWindowMessagesForClassification).ToList();
        var spamSignalCount = CountLowSignalMessages(recentWindow);

        session.Metadata["routing.guard.inbound_count"] = inboundCount.ToString();
        session.Metadata["routing.guard.spam_signal_count"] = spamSignalCount.ToString();
        session.Metadata["routing.guard.history_window"] = routingConfig.HistoryWindowMessagesForClassification.ToString();

        if (spamSignalCount >= routingConfig.MaxSpamSignalsBeforeSpamReview)
        {
            var reputation = existingReputation ?? ChannelSpamReputation.Create(incomingMessage.TenantId, channel.Id, identifier);
            if (reputation.Status == SpamReputationStatus.Suspected)
                reputation.MarkConfirmed("inbound_spam_guard");
            else
                reputation.MarkSuspected("inbound_spam_guard");
            await _spamReputationRepo.UpsertAsync(reputation, ct);

            UpdateSessionGuardStage(session, "spam_review");
            session.Metadata["routing.fallback.state"] = "spam_review";
            session.Metadata["routing.fallback.turn"] = "0";
            session.Metadata["routing.fallback.reason"] = "inbound_spam_guard";
            session.Metadata["requires_human_review"] = "true";
            session.Metadata["reply_pending"] = "true";
            session.Metadata["routing.spam.status"] = reputation.Status.ToString();
            if (!string.IsNullOrWhiteSpace(spamEscalationTarget))
                session.Metadata["routing.fallback.escalation_target"] = spamEscalationTarget;
            await _sessionRepo.UpdateAsync(session, ct);

            return new InboundGuardDecision(
                Guid.NewGuid().ToString("N"),
                "inbound_spam_guard",
                string.Empty,
                spamEscalationTarget,
                !string.IsNullOrWhiteSpace(spamEscalationTarget) &&
                !string.Equals(session.Metadata.GetValueOrDefault("routing.fallback.escalation_status"), "delivered", StringComparison.OrdinalIgnoreCase),
                false);
        }

        var accumulationActive = !string.Equals(guardStage, "classified", StringComparison.OrdinalIgnoreCase);
        if (!accumulationActive)
            return null;

        UpdateSessionGuardStage(session, "accumulating");
        session.Metadata["requires_human_review"] = "false";
        session.Metadata["reply_pending"] = "true";

        if (inboundCount < routingConfig.MinMessagesBeforeClassification)
        {
            session.Metadata["routing.guard.awaiting_context"] = "true";
            await _sessionRepo.UpdateAsync(session, ct);

            if (!routingConfig.SuppressRepliesWhileAccumulating)
                return null;

            return new InboundGuardDecision(
                Guid.NewGuid().ToString("N"),
                "awaiting_more_context",
                string.Empty,
                string.Empty,
                false,
                false);
        }

        if (inboundCount >= routingConfig.MaxUnclassifiedMessagesBeforeEscalation &&
            !string.IsNullOrWhiteSpace(currentState) &&
            !string.Equals(currentState, "clarifying", StringComparison.OrdinalIgnoreCase))
        {
            var fallbackState = string.IsNullOrWhiteSpace(escalationTarget)
                ? "pending_human_review"
                : "escalated_human";
            session.Metadata["routing.fallback.state"] = fallbackState;
            session.Metadata["routing.fallback.turn"] = "0";
            session.Metadata["routing.fallback.reason"] = "unclassified_threshold_reached";
            session.Metadata["requires_human_review"] = "true";
            session.Metadata["reply_pending"] = "true";
            if (!string.IsNullOrWhiteSpace(escalationTarget))
                session.Metadata["routing.fallback.escalation_target"] = escalationTarget;
            UpdateSessionGuardStage(session, fallbackState);
            await _sessionRepo.UpdateAsync(session, ct);

            return new InboundGuardDecision(
                Guid.NewGuid().ToString("N"),
                "unclassified_threshold_reached",
                string.Empty,
                escalationTarget,
                !string.IsNullOrWhiteSpace(escalationTarget) &&
                !string.Equals(session.Metadata.GetValueOrDefault("routing.fallback.escalation_status"), "delivered", StringComparison.OrdinalIgnoreCase),
                false);
        }

        await _sessionRepo.UpdateAsync(session, ct);
        return null;
    }

    private async Task<string?> EvaluateOutboundGuardAsync(ChannelMessage message, CancellationToken ct)
    {
        if (message.Direction != MessageDirection.Outgoing)
            return null;

        var runtimeSettings = _tenantRuntimeSettingsReader is null
            ? new TenantRuntimeSettings()
            : await _tenantRuntimeSettingsReader.GetAsync(message.TenantId, ct);

        var history = await _messageRepo.GetBySessionAsync(message.SessionId, message.TenantId, 25, ct);
        var now = DateTimeOffset.UtcNow;
        var recentOutgoing = history.Where(x => x.Direction == MessageDirection.Outgoing).ToList();

        var maxPerMinute = Math.Clamp(runtimeSettings.MaxConcurrentExecutions, 3, 12);
        var recentCount = recentOutgoing.Count(x => x.CreatedAt >= now.AddMinutes(-1));
        if (recentCount >= maxPerMinute)
            return "outbound_rate_limited";

        var normalizedContent = NormalizeOutboundContent(message.Content);
        if (string.IsNullOrWhiteSpace(normalizedContent))
            return null;

        var duplicateCount = recentOutgoing.Count(x =>
            x.CreatedAt >= now.AddMinutes(-10) &&
            string.Equals(NormalizeOutboundContent(x.Content), normalizedContent, StringComparison.Ordinal));

        return duplicateCount >= 2 ? "outbound_duplicate_blocked" : null;
    }

    private static string NormalizeOutboundContent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(' ', value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Trim()
            .ToLowerInvariant();
    }

    private static int CountLowSignalMessages(IReadOnlyList<ChannelMessage> history)
    {
        return history.Count(x => IsLowSignalInboundMessage(x.Content));
    }

    private static bool IsLowSignalInboundMessage(string? value)
    {
        var normalized = NormalizeOutboundContent(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return true;

        var compact = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (compact.Length <= 1)
            return true;

        var uniqueChars = compact.Distinct().Count();
        var hasRepeatedRun = compact.GroupBy(ch => ch).Any(g => g.Count() >= Math.Max(3, compact.Length - 1));
        if (compact.Length <= 4 && uniqueChars == 1)
            return true;

        if (hasRepeatedRun && compact.Length <= 6)
            return true;

        return compact.Length <= 2;
    }

    private static void UpdateSessionGuardStage(ChannelSession session, string stage)
    {
        session.Metadata["routing.guard.stage"] = stage;
        session.Metadata["routing.guard.updated_at"] = DateTimeOffset.UtcNow.ToString("O");
        if (string.Equals(stage, "classified", StringComparison.OrdinalIgnoreCase))
        {
            session.Metadata.Remove("routing.fallback.state");
            session.Metadata.Remove("routing.fallback.turn");
            session.Metadata.Remove("routing.fallback.reason");
            session.Metadata.Remove("routing.guard.awaiting_context");
        }
    }

    private static InboundRoutingConfig ReadInboundRoutingConfig(ChannelDefinition channel)
    {
        var config = channel.Config;
        var minMessages = ReadConfiguredInt(config, "MinMessagesBeforeClassification", 3, 1, 10);
        return new InboundRoutingConfig(
            minMessages,
            ReadConfiguredInt(config, "MaxUnclassifiedMessagesBeforeEscalation", 4, minMessages, 12),
            ReadConfiguredInt(config, "HistoryWindowMessagesForClassification", 3, 1, 10),
            ReadConfiguredInt(config, "MaxSpamSignalsBeforeSpamReview", 2, 1, 10),
            ReadConfiguredBool(config, "SuppressRepliesWhileAccumulating", true),
            config.GetValueOrDefault("EscalationTarget"),
            config.GetValueOrDefault("SpamEscalationTarget"));
    }

    private static int ReadConfiguredInt(
        IReadOnlyDictionary<string, string> config,
        string key,
        int fallback,
        int min,
        int max)
    {
        return int.TryParse(config.GetValueOrDefault(key), out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }

    private static bool ReadConfiguredBool(
        IReadOnlyDictionary<string, string> config,
        string key,
        bool fallback)
    {
        if (!config.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback;

        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record InboundGuardDecision(
        string ExecutionId,
        string ReasonCode,
        string CustomerMessage,
        string? EscalationTarget,
        bool NotifyHumanReview,
        bool ShouldSendCustomerReply);

    private sealed record InboundRoutingConfig(
        int MinMessagesBeforeClassification,
        int MaxUnclassifiedMessagesBeforeEscalation,
        int HistoryWindowMessagesForClassification,
        int MaxSpamSignalsBeforeSpamReview,
        bool SuppressRepliesWhileAccumulating,
        string? EscalationTarget,
        string? SpamEscalationTarget);

}



