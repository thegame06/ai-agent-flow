using AgentFlow.Application.Memory;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Core.Engine;

public interface IChannelDeliveryPolicy
{
    Task<string> BuildCustomerSafeFallbackAsync(string tenantId, string? respondingAgentKey, CancellationToken ct);
    Task RecordOutgoingAuditAsync(
        ChannelMessage incomingMessage,
        string executionId,
        string delivery,
        string response,
        bool systemOnly,
        CancellationToken ct);
}

public sealed class ChannelDeliveryPolicy : IChannelDeliveryPolicy
{
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAuditMemory _auditMemory;

    public ChannelDeliveryPolicy(IAgentDefinitionRepository agentRepo, IAuditMemory auditMemory)
    {
        _agentRepo = agentRepo;
        _auditMemory = auditMemory;
    }

    public async Task<string> BuildCustomerSafeFallbackAsync(
        string tenantId,
        string? respondingAgentKey,
        CancellationToken ct)
    {
        const string genericFallback = "En este momento no puedo completar esta solicitud automáticamente. Te conecto con un asesor para continuar.";
        if (!string.IsNullOrWhiteSpace(respondingAgentKey))
        {
            var agent = await _agentRepo.GetByIdAsync(respondingAgentKey!, tenantId, ct);
            var configured = agent?.Session.CustomerSafeFallbackMessage?.Trim();
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;
        }

        return genericFallback;
    }

    public async Task RecordOutgoingAuditAsync(
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
}
