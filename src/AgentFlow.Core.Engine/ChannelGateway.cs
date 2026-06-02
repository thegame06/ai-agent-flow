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

            var finalResponse = continuation.FinalResponse;
            var executionIdForOutgoing = continuation.ExecutionIdForOutgoing;
            var respondingAgentKey = continuation.RespondingAgentKey;

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

}



