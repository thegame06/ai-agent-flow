using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace AgentFlow.Events;

// =========================================================================
// IN-PROCESS EVENT TRANSPORT (Phase 2 default — replace with NATS/RabbitMQ in Phase 3)
// =========================================================================

/// <summary>
/// In-process event bus for Phase 2. 
/// Suitable for single-node deployment.
/// Replace with NATS or RabbitMQ transport for multi-node Worker pool.
/// </summary>
public sealed class InProcessEventTransport : IAgentEventTransport, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, List<Func<AgentEvent, Task>>> _handlers = new();
    private readonly ILogger<InProcessEventTransport> _logger;

    public InProcessEventTransport(ILogger<InProcessEventTransport> logger)
    {
        _logger = logger;
    }

    public async Task PublishAsync(AgentEvent @event, CancellationToken ct = default)
    {
        _logger.LogDebug("Event published: {EventType} for agent '{AgentKey}' (tenant={TenantId})",
            @event.EventType, @event.AgentKey, @event.TenantId);

        var key = $"{@event.TenantId}:{@event.AgentKey}";
        var globalKey = $"*:{@event.AgentKey}";

        var handlers = new List<Func<AgentEvent, Task>>();

        if (_handlers.TryGetValue(key, out var specific))
            handlers.AddRange(specific);
        if (_handlers.TryGetValue(globalKey, out var global))
            handlers.AddRange(global);

        foreach (var handler in handlers)
        {
            try
            {
                await handler(@event);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event handler failed for {EventType}", @event.EventType);
            }
        }
    }

    public Task<IAsyncDisposable> SubscribeAsync(
        string agentKey,
        Func<AgentEvent, Task> handler,
        CancellationToken ct = default)
    {
        var key = $"*:{agentKey}";
        _handlers.AddOrUpdate(key,
            _ => [handler],
            (_, existing) => { existing.Add(handler); return existing; });

        _logger.LogDebug("Subscribed to events for agent '{AgentKey}'", agentKey);

        return Task.FromResult<IAsyncDisposable>(
            new Subscription(() => Unsubscribe(key, handler)));
    }

    private void Unsubscribe(string key, Func<AgentEvent, Task> handler)
    {
        if (_handlers.TryGetValue(key, out var handlers))
            handlers.Remove(handler);
    }

    public ValueTask DisposeAsync()
    {
        _handlers.Clear();
        return ValueTask.CompletedTask;
    }

    private sealed class Subscription(Action unsubscribe) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            unsubscribe();
            return ValueTask.CompletedTask;
        }
    }
}

// =========================================================================
// CONVERSATIONAL EVENT SOURCE (HTTP/WebSocket backed)
// =========================================================================

/// <summary>
/// Produces AgentEvents from an in-memory channel.
/// In production: replace channel with message queue subscription.
/// </summary>
public sealed class ConversationalEventSource : IAgentEventSource
{
    private readonly System.Threading.Channels.Channel<AgentEvent> _channel =
        System.Threading.Channels.Channel.CreateUnbounded<AgentEvent>();

    public string SourceType => "conversational";

    public async IAsyncEnumerable<AgentEvent> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var @event in _channel.Reader.ReadAllAsync(ct))
            yield return @event;
    }

    public async Task EnqueueAsync(AgentEvent @event, CancellationToken ct = default) =>
        await _channel.Writer.WriteAsync(@event, ct);
}

// =========================================================================
// CRON EVENT SOURCE
// =========================================================================

/// <summary>
/// Emits scheduled agent trigger events based on cron expressions.
/// Uses simple polling — replace with Quartz.NET or similar in Phase 3.
/// </summary>
public sealed class CronEventSource : IAgentEventSource
{
    public string SourceType => "cron";
    private readonly IReadOnlyList<CronTrigger> _triggers;

    public CronEventSource(IReadOnlyList<CronTrigger> triggers)
    {
        _triggers = triggers;
    }

    public async IAsyncEnumerable<AgentEvent> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var trigger in _triggers)
            {
                if (trigger.ShouldFire(now))
                {
                    yield return new AgentEvent
                    {
                        EventType = "scheduled.trigger",
                        TenantId = trigger.TenantId,
                        AgentKey = trigger.AgentKey,
                        Payload = """{"source": "cron"}""",
                        CorrelationId = Guid.NewGuid().ToString("N")
                    };
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(30), ct); // Check every 30s
        }
    }
}

public sealed record CronTrigger
{
    public required string TenantId { get; init; }
    public required string AgentKey { get; init; }
    public required string CronExpression { get; init; }
    private DateTimeOffset _lastFired = DateTimeOffset.MinValue;

    public bool ShouldFire(DateTimeOffset now)
    {
        // Simplified: for Phase 2 use interval-based. Replace with NCrontab in Phase 3.
        return false; // Placeholder
    }
}
