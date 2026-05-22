using AgentFlow.Abstractions;
using AgentFlow.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.Events;

/// <summary>
/// DI registration for the Event Transport subsystem.
/// </summary>
public static class EventServiceExtensions
{
    public static IServiceCollection AddEventTransport(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var options = new EventTransportOptions();
        if (configuration is not null)
        {
            services.Configure<EventTransportOptions>(configuration.GetSection(EventTransportOptions.SectionName));
            configuration.GetSection(EventTransportOptions.SectionName).Bind(options);
        }
        else
        {
            services.Configure<EventTransportOptions>(_ => { });
        }

        var deadLetterProvider = options.DeadLetterStoreProvider;
        if (string.Equals(deadLetterProvider, "Redis", StringComparison.OrdinalIgnoreCase) && configuration is not null)
        {
            var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
            services.AddSingleton<IDeadLetterStore>(_ =>
            {
                var redis = StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString);
                return new RedisDeadLetterStore(redis, options.DeadLetterRetentionHours);
            });
        }
        else
        {
            services.AddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();
        }

        if (string.Equals(options.Provider, "AzureServiceBus", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IAgentEventTransport, ServiceBusAgentEventTransport>();
        else if (string.Equals(options.Provider, "Nats", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IAgentEventTransport, NatsAgentEventTransport>();
        else
            services.AddSingleton<IAgentEventTransport, InProcessEventTransport>();

        // Sources: Conversational is common for API
        services.AddSingleton<ConversationalEventSource>();
        services.AddSingleton<IAgentEventSource>(sp => sp.GetRequiredService<ConversationalEventSource>());

        return services;
    }
}
