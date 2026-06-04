using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using MongoDB.Driver;
using Result = AgentFlow.Abstractions.Result;

namespace AgentFlow.Infrastructure.Repositories;

public sealed class MongoChannelSpamReputationRepository : IChannelSpamReputationRepository
{
    private readonly IMongoCollection<ChannelSpamReputation> _collection;

    public MongoChannelSpamReputationRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ChannelSpamReputation>("channel_spam_reputation");
        _collection.Indexes.CreateOne(new CreateIndexModel<ChannelSpamReputation>(
            Builders<ChannelSpamReputation>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.ChannelId)
                .Ascending(x => x.Identifier),
            new CreateIndexOptions
            {
                Name = "ux_channel_spam_reputation_lookup",
                Unique = true
            }));
    }

    public async Task<ChannelSpamReputation?> GetAsync(string tenantId, string channelId, string identifier, CancellationToken ct = default)
    {
        return await _collection.Find(x =>
                x.TenantId == tenantId &&
                x.ChannelId == channelId &&
                x.Identifier == identifier)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Result> UpsertAsync(ChannelSpamReputation reputation, CancellationToken ct = default)
    {
        try
        {
            await _collection.ReplaceOneAsync(
                x => x.TenantId == reputation.TenantId &&
                     x.ChannelId == reputation.ChannelId &&
                     x.Identifier == reputation.Identifier,
                reputation,
                new ReplaceOptions { IsUpsert = true },
                ct);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("ChannelSpamReputation.UpsertFailed", $"Failed to upsert reputation: {ex.Message}", ErrorCategory.Infrastructure));
        }
    }
}
