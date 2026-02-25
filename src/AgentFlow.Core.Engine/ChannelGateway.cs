using AgentFlow.Abstractions;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
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
    private readonly ILogger<ChannelGateway> _logger;

    public ChannelGateway(
        IChannelDefinitionRepository channelRepo,
        IChannelSessionRepository sessionRepo,
        IChannelMessageRepository messageRepo,
        IAgentExecutor agentExecutor,
        IEnumerable<IChannelHandler> handlers,
        ILogger<ChannelGateway> logger)
    {
        _channelRepo = channelRepo;
        _sessionRepo = sessionRepo;
        _messageRepo = messageRepo;
        _agentExecutor = agentExecutor;
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
            // Execute agent
            var executionRequest = new AgentExecutionRequest
            {
                TenantId = incomingMessage.TenantId,
                AgentKey = channel.Metadata?.GetValueOrDefault("DefaultAgentId") ?? "default-agent",
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

            // Create outgoing message
            var outgoingMessage = ChannelMessage.CreateOutgoing(
                incomingMessage.TenantId,
                incomingMessage.ChannelId,
                incomingMessage.SessionId,
                incomingMessage.From,
                executionResult.FinalResponse ?? "Sorry, I couldn't process that."
            );

            outgoingMessage.LinkExecution(executionResult.ExecutionId);

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
}
