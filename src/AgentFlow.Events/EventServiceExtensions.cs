using AgentFlow.Abstractions;
using AgentFlow.Events;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.Events;

/// <summary>
/// DI registration for the Event Transport subsystem.
/// </summary>
public static class EventServiceExtensions
{
    public static IServiceCollection AddEventTransport(this IServiceCollection services)
    {
        // Transport: Singleton (shared in-process bus)
        services.AddSingleton<IAgentEventTransport, InProcessEventTransport>();

        // Sources: Conversational is common for API
        services.AddSingleton<ConversationalEventSource>();
        services.AddSingleton<IAgentEventSource>(sp => sp.GetRequiredService<ConversationalEventSource>());

        return services;
    }
}
