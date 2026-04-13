using AgentFlow.Abstractions;
using AgentFlow.Application.Channels;
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
    private readonly ILogger<ChannelGateway> _logger;

    public ChannelGateway(
        IChannelDefinitionRepository channelRepo,
        IChannelSessionRepository sessionRepo,
        IChannelMessageRepository messageRepo,
        IAgentExecutor agentExecutor,
        IAgentHandoffExecutor handoffExecutor,
        IManagerHandoffPolicy handoffPolicy,
        IEnumerable<IChannelHandler> handlers,
        ILogger<ChannelGateway> logger)
    {
        _channelRepo = channelRepo;
        _sessionRepo = sessionRepo;
        _messageRepo = messageRepo;
        _agentExecutor = agentExecutor;
        _handoffExecutor = handoffExecutor;
        _handoffPolicy = handoffPolicy;
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

            // Execute agent
            var executionRequest = new AgentExecutionRequest
            {
                TenantId = incomingMessage.TenantId,
                AgentKey = agentKey,
                UserId = incomingMessage.From,
                UserMessage = incomingMessage.Content,
                ContextJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    ChannelType = channel.Type.ToString(),
                    ChannelId = channel.Id,
                    SessionId = incomingMessage.SessionId,
                    From = incomingMessage.From
                }),
                CorrelationId = incomingMessage.SessionId,
                ThreadId = incomingMessage.SessionId,
                Priority = ExecutionPriority.Normal
            };

            var executionResult = await _agentExecutor.ExecuteAsync(executionRequest, ct);
            incomingMessage.LinkExecution(executionResult.ExecutionId);

            var finalResponse = executionResult.FinalResponse;
            var executionIdForOutgoing = executionResult.ExecutionId;

            var handoff = TryParseHandoffDirective(finalResponse);
            if (handoff is not null && session is not null)
            {
                if (_handoffPolicy.IsAllowed(incomingMessage.TenantId, agentKey, handoff.TargetAgentId))
                {
                    var handoffResponse = await _handoffExecutor.ExecuteAsync(new AgentHandoffRequest
                    {
                        TenantId = incomingMessage.TenantId,
                        SessionId = incomingMessage.SessionId,
                        ThreadId = incomingMessage.SessionId,
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

                        session.LinkAgent(handoff.TargetAgentId);
                        await _sessionRepo.UpdateAsync(session, ct);
                    }
                    else
                    {
                        finalResponse = "I couldn't complete that request right now.";
                    }
                }
                else
                {
                    finalResponse = "That delegation target is not allowed by policy.";
                }
            }

            // Create outgoing message
            var outgoingMessage = ChannelMessage.CreateOutgoing(
                incomingMessage.TenantId,
                incomingMessage.ChannelId,
                incomingMessage.SessionId,
                incomingMessage.From,
                finalResponse ?? "Sorry, I couldn't process that."
            );

            outgoingMessage.LinkExecution(executionIdForOutgoing);

            // Send reply through channel
            var sendResult = await SendMessageAsync(incomingMessage.ChannelId, outgoingMessage, ct);
            if (!sendResult.Success)
            {
                _logger.LogError("Failed to send reply: {Error}", sendResult.Error);
            }

            return outgoingMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from channel {ChannelId}", incomingMessage.ChannelId);
            incomingMessage.MarkFailed(ex.Message);
            await _messageRepo.UpdateAsync(incomingMessage, ct);
            throw;
        }
    }

    public async Task<SendResult> SendMessageAsync(string channelId, ChannelMessage message, CancellationToken ct = default)
    {
        var channel = await _channelRepo.GetByIdAsync(channelId, message.TenantId, ct);
        if (channel == null)
            return SendResult.Fail($"Channel {channelId} not found");

        var handler = GetHandler(channel.Type);
        if (handler == null)
            return SendResult.Fail($"No handler for channel type {channel.Type}");

        await _messageRepo.InsertAsync(message, ct);
        return await handler.SendReplyAsync(message, channel, ct);
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
    private static string ResolveAgentKey(ChannelDefinition channel, ChannelSession? session)
    {
        // Sticky routing: preserve owner agent for the current session.
        if (!string.IsNullOrWhiteSpace(session?.AgentId))
            return session.AgentId!;

        return channel.Metadata?.GetValueOrDefault("DefaultAgentId") ?? "default-agent";
    }

}
