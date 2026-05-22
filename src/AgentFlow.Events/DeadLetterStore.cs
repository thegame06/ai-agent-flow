using System.Collections.Concurrent;
using System.Text.Json;
using AgentFlow.Abstractions;
using StackExchange.Redis;

namespace AgentFlow.Events;

public sealed record DeadLetterEnvelope
{
    public required string Id { get; init; }
    public required string AgentKey { get; init; }
    public required string Reason { get; init; }
    public required AgentEvent Event { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public bool Replayed { get; set; }
    public DateTimeOffset? ReplayedAt { get; set; }
}

public interface IDeadLetterStore
{
    void Add(DeadLetterEnvelope envelope);
    IReadOnlyList<DeadLetterEnvelope> List(int limit = 200);
    DeadLetterEnvelope? Get(string id);
    bool MarkReplayed(string id);
}

public sealed class InMemoryDeadLetterStore : IDeadLetterStore
{
    private readonly ConcurrentDictionary<string, DeadLetterEnvelope> _items = new(StringComparer.OrdinalIgnoreCase);

    public void Add(DeadLetterEnvelope envelope)
    {
        _items[envelope.Id] = envelope;
    }

    public IReadOnlyList<DeadLetterEnvelope> List(int limit = 200)
    {
        var bounded = Math.Clamp(limit, 1, 5000);
        return _items.Values
            .OrderByDescending(x => x.OccurredAt)
            .Take(bounded)
            .ToList();
    }

    public DeadLetterEnvelope? Get(string id)
    {
        return _items.TryGetValue(id, out var item) ? item : null;
    }

    public bool MarkReplayed(string id)
    {
        if (!_items.TryGetValue(id, out var item))
            return false;

        item.Replayed = true;
        item.ReplayedAt = DateTimeOffset.UtcNow;
        return true;
    }
}

public sealed class RedisDeadLetterStore : IDeadLetterStore
{
    private readonly IDatabase _db;
    private readonly int _retentionHours;
    private const string IndexKey = "agentflow:deadletters:index";
    private const string KeyPrefix = "agentflow:deadletters:item:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RedisDeadLetterStore(IConnectionMultiplexer redis, int retentionHours = 72)
    {
        _db = redis.GetDatabase();
        _retentionHours = Math.Clamp(retentionHours, 1, 24 * 30);
    }

    public void Add(DeadLetterEnvelope envelope)
    {
        var key = ItemKey(envelope.Id);
        var payload = JsonSerializer.Serialize(envelope, JsonOptions);
        var now = DateTimeOffset.UtcNow;
        var expiry = TimeSpan.FromHours(_retentionHours);

        _db.StringSet(key, payload, expiry);
        _db.SortedSetAdd(IndexKey, envelope.Id, now.ToUnixTimeSeconds());
        _db.KeyExpire(IndexKey, expiry);
    }

    public IReadOnlyList<DeadLetterEnvelope> List(int limit = 200)
    {
        var bounded = Math.Clamp(limit, 1, 5000);
        var ids = _db.SortedSetRangeByRank(IndexKey, -bounded, -1, Order.Descending);
        if (ids.Length == 0) return [];

        var result = new List<DeadLetterEnvelope>(ids.Length);
        foreach (var id in ids)
        {
            if (!id.HasValue) continue;
            var raw = _db.StringGet(ItemKey(id.ToString()));
            if (!raw.HasValue) continue;
            var parsed = JsonSerializer.Deserialize<DeadLetterEnvelope>(raw!, JsonOptions);
            if (parsed is not null) result.Add(parsed);
        }
        return result;
    }

    public DeadLetterEnvelope? Get(string id)
    {
        var raw = _db.StringGet(ItemKey(id));
        return raw.HasValue ? JsonSerializer.Deserialize<DeadLetterEnvelope>(raw!, JsonOptions) : null;
    }

    public bool MarkReplayed(string id)
    {
        var current = Get(id);
        if (current is null) return false;

        var updated = current with { Replayed = true, ReplayedAt = DateTimeOffset.UtcNow };
        _db.StringSet(ItemKey(id), JsonSerializer.Serialize(updated, JsonOptions), TimeSpan.FromHours(_retentionHours));
        return true;
    }

    private static string ItemKey(string id) => $"{KeyPrefix}{id}";
}
