using System.Collections.Concurrent;
using System.Text.Json;
using AgentFlow.Abstractions;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentFlow.Events;

public sealed class ServiceBusAgentEventTransport : IAgentEventTransport, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly EventTransportOptions _options;
    private readonly ILogger<ServiceBusAgentEventTransport> _logger;
    private readonly ConcurrentDictionary<string, ServiceBusProcessor> _processors = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<AgentEvent, Task>> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public ServiceBusAgentEventTransport(
        IOptions<EventTransportOptions> options,
        ILogger<ServiceBusAgentEventTransport> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("EventTransport:ConnectionString is required for AzureServiceBus provider.");

        _client = new ServiceBusClient(_options.ConnectionString);
        _sender = _client.CreateSender(_options.TopicName);
    }

    public async Task PublishAsync(AgentEvent @event, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(@event);
        var msg = new ServiceBusMessage(payload)
        {
            MessageId = @event.EventId,
            CorrelationId = @event.CorrelationId
        };

        msg.ApplicationProperties["agentKey"] = @event.AgentKey;
        msg.ApplicationProperties["tenantId"] = @event.TenantId;
        msg.ApplicationProperties["eventType"] = @event.EventType;

        await _sender.SendMessageAsync(msg, ct);
    }

    public async Task<IAsyncDisposable> SubscribeAsync(
        string agentKey,
        Func<AgentEvent, Task> handler,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentKey))
            throw new ArgumentException("Agent key is required.", nameof(agentKey));

        var subscription = $"{_options.SubscriptionPrefix}-{Sanitize(agentKey)}";
        await EnsureSubscriptionAsync(subscription, agentKey, ct);
        var processorKey = $"{_options.TopicName}:{subscription}";
        _handlers[processorKey] = handler;

        var processor = _processors.GetOrAdd(processorKey, _ =>
        {
            var p = _client.CreateProcessor(_options.TopicName, subscription, new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 8
            });
            p.ProcessMessageAsync += args => HandleMessageAsync(processorKey, args);
            p.ProcessErrorAsync += args =>
            {
                _logger.LogWarning(args.Exception, "ServiceBus processor error. Entity={EntityPath}", args.EntityPath);
                return Task.CompletedTask;
            };
            return p;
        });

        await processor.StartProcessingAsync(ct);
        return new Subscription(async () =>
        {
            _handlers.TryRemove(processorKey, out _);
            if (_processors.TryRemove(processorKey, out var existing))
            {
                await existing.StopProcessingAsync(CancellationToken.None);
                await existing.DisposeAsync();
            }
        });
    }

    private async Task HandleMessageAsync(string processorKey, ProcessMessageEventArgs args)
    {
        if (!_handlers.TryGetValue(processorKey, out var handler))
        {
            await args.CompleteMessageAsync(args.Message);
            return;
        }

        try
        {
            var body = args.Message.Body.ToString();
            var evt = JsonSerializer.Deserialize<AgentEvent>(body);
            if (evt is null)
            {
                await args.DeadLetterMessageAsync(args.Message, "invalid_payload", "Failed to deserialize AgentEvent.");
                return;
            }

            await handler(evt);
            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed handling ServiceBus event message.");
            await args.AbandonMessageAsync(args.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var processor in _processors.Values)
        {
            await processor.DisposeAsync();
        }

        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }

    private static string Sanitize(string raw)
    {
        var chars = raw.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private async Task EnsureSubscriptionAsync(string subscription, string agentKey, CancellationToken ct)
    {
        var admin = new ServiceBusAdministrationClient(_options.ConnectionString);
        if (!await admin.SubscriptionExistsAsync(_options.TopicName, subscription, ct))
        {
            await admin.CreateSubscriptionAsync(new CreateSubscriptionOptions(_options.TopicName, subscription), ct);
        }

        var rules = admin.GetRulesAsync(_options.TopicName, subscription, ct);
        var hasRule = false;
        await foreach (var rule in rules)
        {
            if (string.Equals(rule.Name, "agent-key-filter", StringComparison.OrdinalIgnoreCase))
            {
                hasRule = true;
                break;
            }
        }

        if (!hasRule)
        {
            try
            {
                await admin.DeleteRuleAsync(_options.TopicName, subscription, RuleProperties.DefaultRuleName, ct);
            }
            catch
            {
                // ignore if default rule does not exist
            }

            var filter = new CorrelationRuleFilter
            {
                ApplicationProperties =
                {
                    ["agentKey"] = agentKey
                }
            };
            await admin.CreateRuleAsync(_options.TopicName, subscription, new CreateRuleOptions("agent-key-filter", filter), ct);
        }
    }

    private sealed class Subscription(Func<Task> onDispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(onDispose());
    }
}
