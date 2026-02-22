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
        
        // Seed some platform models (Guru approach: system-level defaults)
        registry.Register(new StubModelProvider("gpt-4o") { 
            Metadata = new ModelMetadata { DisplayName = "GPT-4o", CostPer1KTokens = 0.005, MaxContextTokens = 128000, Tier = "Primary" }
        });
        registry.Register(new StubModelProvider("gpt-4o-mini") { 
            Metadata = new ModelMetadata { DisplayName = "GPT-4o Mini", CostPer1KTokens = 0.00015, MaxContextTokens = 128000, Tier = "Fallback" }
        });
        registry.Register(new StubModelProvider("claude-3-5-sonnet") { 
            Metadata = new ModelMetadata { DisplayName = "Claude 3.5 Sonnet", CostPer1KTokens = 0.003, MaxContextTokens = 200000, Tier = "Secondary" }
        });

        services.AddSingleton<IModelRegistry>(registry);

        // Router: Singleton (stateless logic)
        services.AddSingleton<IModelRouter, ModelRouter>();

        return services;
    }
}
