using AgentFlow.Domain.Aggregates;
using Result = AgentFlow.Abstractions.Result;

namespace AgentFlow.Domain.Repositories;

public interface IChannelDefinitionRepository
{
    Task<ChannelDefinition?> GetByIdAsync(string channelId, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelDefinition>> GetAllAsync(string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelDefinition>> GetByTypeAsync(ChannelType type, string tenantId, CancellationToken ct = default);
    Task<ChannelDefinition?> GetByNameAsync(string name, string tenantId, CancellationToken ct = default);
    Task<Result> InsertAsync(ChannelDefinition channel, CancellationToken ct = default);
    Task<Result> UpdateAsync(ChannelDefinition channel, CancellationToken ct = default);
    Task<Result> DeleteAsync(string channelId, string tenantId, CancellationToken ct = default);
}

public interface IChannelSessionRepository
{
    Task<ChannelSession?> GetByIdAsync(string sessionId, string tenantId, CancellationToken ct = default);
    Task<ChannelSession?> GetByChannelAndIdentifierAsync(string channelId, string identifier, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelSession>> GetActiveByChannelAsync(string channelId, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelSession>> GetActiveByUserAsync(string userIdentifier, string tenantId, CancellationToken ct = default);
    Task<Result> InsertAsync(ChannelSession session, CancellationToken ct = default);
    Task<Result> UpdateAsync(ChannelSession session, CancellationToken ct = default);
    Task<Result> DeleteAsync(string sessionId, string tenantId, CancellationToken ct = default);
    Task<int> GetActiveCountAsync(string tenantId, CancellationToken ct = default);
}

public interface IChannelMessageRepository
{
    Task<ChannelMessage?> GetByIdAsync(string messageId, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelMessage>> GetBySessionAsync(string sessionId, string tenantId, int limit = 50, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelMessage>> GetByChannelAsync(string channelId, string tenantId, int limit = 50, CancellationToken ct = default);
    Task<Result> InsertAsync(ChannelMessage message, CancellationToken ct = default);
    Task<Result> UpdateAsync(ChannelMessage message, CancellationToken ct = default);
    Task<Result> DeleteAsync(string messageId, string tenantId, CancellationToken ct = default);
}
