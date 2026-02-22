using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.Prompting;

public static class PromptServiceExtensions
{
    public static IServiceCollection AddPromptEngine(this IServiceCollection services)
    {
        services.AddSingleton<IPromptRenderer, PromptRenderer>();
        // Note: Store registration is usually handled at the Infrastructure/API level
        // because it depends on MongoDB/Persistence.
        
        return services;
    }
}
