using AgentFlow.Abstractions;
using AgentFlow.ModelRouting;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.ModelRouting;

/// <summary>
/// DI registration for the Model Routing subsystem.
/// </summary>
public static class ModelRoutingServiceExtensions
{
    public static IServiceCollection AddModelRouting(this IServiceCollection services)
    {
        // Registry: Singleton (shared across all requests)
        var registry = new InMemoryModelRegistry();

        services.AddSingleton<IModelRegistry>(registry);

        // Router: Singleton (stateless logic)
        services.AddSingleton<IModelRouter, ModelRouter>();

        return services;
    }
}
