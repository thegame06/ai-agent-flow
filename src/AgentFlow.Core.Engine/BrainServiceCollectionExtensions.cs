using AgentFlow.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace AgentFlow.Core.Engine;

public static class BrainServiceCollectionExtensions
{
    public static IServiceCollection AddSemanticKernelBrain(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<SemanticKernelBrain>();

        services.AddScoped<Kernel>(_ =>
        {
            var provider = configuration["SemanticKernel:Provider"] ?? "OpenAI";

            if (string.Equals(provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return Kernel.CreateBuilder()
                    .AddAzureOpenAIChatCompletion(
                        deploymentName: configuration["SemanticKernel:AzureOpenAI:DeploymentName"]!,
                        endpoint: configuration["SemanticKernel:AzureOpenAI:Endpoint"]!,
                        apiKey: configuration["SemanticKernel:AzureOpenAI:ApiKey"]!)
                    .Build();
            }

            return Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(
                    modelId: configuration["SemanticKernel:OpenAI:ModelId"] ?? "gpt-4o",
                    apiKey: configuration["SemanticKernel:OpenAI:ApiKey"]!)
                .Build();
        });

        return services;
    }

    public static IServiceCollection AddMafBrain(this IServiceCollection services)
    {
        services.AddScoped<MafBrain>();
        return services;
    }

    public static IServiceCollection AddAgentBrains(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var defaultProviderStr = configuration["AgentBrain:DefaultProvider"] ?? "SemanticKernel";
        if (!Enum.TryParse<BrainProvider>(defaultProviderStr, true, out var defaultProvider))
        {
            defaultProvider = BrainProvider.SemanticKernel;
        }

        services
            .AddSemanticKernelBrain(configuration)
            .AddMafBrain();

        services.AddScoped<IAgentBrain>(sp =>
        {
            return defaultProvider switch
            {
                BrainProvider.MicrosoftAgentFramework => sp.GetRequiredService<MafBrain>(),
                _ => sp.GetRequiredService<SemanticKernelBrain>()
            };
        });

        return services;
    }
}
