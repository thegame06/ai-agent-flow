using AgentFlow.Abstractions;
using AgentFlow.ModelRouting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.ModelRouting;

/// <summary>
/// DI registration for the Model Routing subsystem.
/// </summary>
public static class ModelRoutingServiceExtensions
{
    public static IServiceCollection AddModelRouting(this IServiceCollection services, IConfiguration? configuration = null)
    {
        // Registry: Singleton (shared across all requests)
        var registry = new InMemoryModelRegistry();

        services.AddSingleton<IModelRegistry>(registry);
        if (configuration is not null)
            services.Configure<ModelRoleOrchestrationOptions>(configuration.GetSection(ModelRoleOrchestrationOptions.SectionName));
        else
            services.Configure<ModelRoleOrchestrationOptions>(_ => { });

        // Router: Singleton (stateless logic)
        services.AddSingleton<IModelRouter, ModelRouter>();
        services.AddSingleton<IModelRoleOrchestrator, ModelRoleOrchestrator>();

        return services;
    }
}
