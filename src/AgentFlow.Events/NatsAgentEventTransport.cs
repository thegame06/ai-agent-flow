using System.Text;
using System.Text.Json;
using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client;

namespace AgentFlow.Events;

public sealed class NatsAgentEventTransport : IAgentEventTransport, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly EventTransportOptions _options;
    private readonly IDeadLetterStore _deadLetterStore;
    private readonly ILogger<NatsAgentEventTransport> _logger;

    public NatsAgentEventTransport(
        IOptions<EventTransportOptions> options,
        IDeadLetterStore deadLetterStore,
        ILogger<NatsAgentEventTransport> logger)
    {
        _options = options.Value;
        _deadLetterStore = deadLetterStore;
        _logger = logger;

        var cf = new ConnectionFactory();
        _connection = cf.CreateConnection(_options.NatsUrl);
    }

    public Task PublishAsync(AgentEvent @event, CancellationToken ct = default)
    {
        var subject = BuildSubject(@event.AgentKey);
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event));
        _connection.Publish(subject, payload);
        _connection.Flush();
        return Task.CompletedTask;
    }

    public Task<IAsyncDisposable> SubscribeAsync(
        string agentKey,
        Func<AgentEvent, Task> handler,
        CancellationToken ct = default)
    {
        var subject = BuildSubscriptionSubject(agentKey);
        var subscription = _connection.SubscribeAsync(subject);
        subscription.MessageHandler += async (_, args) =>
        {
            AgentEvent? evt = null;
            try
            {
                var json = Encoding.UTF8.GetString(args.Message.Data);
                evt = JsonSerializer.Deserialize<AgentEvent>(json);
                if (evt is null)
                    return;

                await HandleWithRetryAsync(evt, agentKey, handler, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed handling NATS message on subject {Subject}", subject);
                if (evt is not null)
                    await PublishDeadLetterAsync(evt, agentKey, "deserialization_or_unknown_error", ex, ct);
            }
        };
        subscription.Start();

        return Task.FromResult<IAsyncDisposable>(new Subscription(subscription));
    }

    public ValueTask DisposeAsync()
    {
        _connection.Drain();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }

    private string BuildSubject(string agentKey) =>
        $"{NormalizePrefix(_options.NatsSubjectPrefix)}.{Sanitize(agentKey)}";

    private string BuildSubscriptionSubject(string agentKey)
    {
        var prefix = NormalizePrefix(_options.NatsSubjectPrefix);

        // Allow background workers to subscribe to all agents.
        if (agentKey == "*" || agentKey == ">")
            return $"{prefix}.>";

        return $"{prefix}.{Sanitize(agentKey)}";
    }

    private string BuildDeadLetterSubject(string agentKey) =>
        $"{NormalizePrefix(_options.NatsSubjectPrefix)}.{Sanitize(_options.NatsDeadLetterSuffix)}.{Sanitize(agentKey)}";

    private static string NormalizePrefix(string prefix)
    {
        var normalized = (prefix ?? string.Empty).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(normalized) ? "agentflow.events" : normalized;
    }

    private async Task HandleWithRetryAsync(
        AgentEvent evt,
        string agentKey,
        Func<AgentEvent, Task> handler,
        CancellationToken ct)
    {
        var maxAttempts = Math.Clamp(_options.DeliveryMaxAttempts, 1, 10);
        var baseBackoffMs = Math.Max(0, _options.DeliveryBaseBackoffMs);
        Exception? last = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var deliveryAttemptEvent = evt with
                {
                    Headers = new Dictionary<string, string>(evt.Headers)
                    {
                        ["deliveryAttempt"] = attempt.ToString()
                    }
                };
                await handler(deliveryAttemptEvent);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                last = ex;
                if (attempt < maxAttempts)
                {
                    var delay = TimeSpan.FromMilliseconds(baseBackoffMs * attempt);
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, ct);
                }
            }
        }

        await PublishDeadLetterAsync(evt, agentKey, "max_retries_exhausted", last, ct);
    }

    private Task PublishDeadLetterAsync(
        AgentEvent evt,
        string agentKey,
        string reason,
        Exception? ex,
        CancellationToken ct)
    {
        try
        {
            var deadLetterEvent = evt with
            {
                Headers = new Dictionary<string, string>(evt.Headers)
                {
                    ["deadLetterReason"] = reason,
                    ["deadLetterAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["deadLetterError"] = ex?.Message ?? string.Empty
                }
            };

            var subject = BuildDeadLetterSubject(agentKey);
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(deadLetterEvent));
            _connection.Publish(subject, payload);
            _connection.Flush();
            _deadLetterStore.Add(new DeadLetterEnvelope
            {
                Id = Guid.NewGuid().ToString("N"),
                AgentKey = agentKey,
                Reason = reason,
                Event = deadLetterEvent,
                OccurredAt = DateTimeOffset.UtcNow
            });

            _logger.LogError(ex,
                "Published dead-letter event. Subject={Subject} EventId={EventId} Tenant={TenantId} Reason={Reason}",
                subject,
                evt.EventId,
                evt.TenantId,
                reason);
        }
        catch (Exception publishEx)
        {
            _logger.LogCritical(publishEx,
                "Failed to publish dead-letter event. EventId={EventId} Tenant={TenantId}",
                evt.EventId,
                evt.TenantId);
        }

        return Task.CompletedTask;
    }

    private static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "unknown";

        var chars = raw.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private sealed class Subscription(IAsyncSubscription subscription) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            subscription.Unsubscribe();
            subscription.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
