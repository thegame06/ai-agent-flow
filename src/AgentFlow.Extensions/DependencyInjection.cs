using AgentFlow.Abstractions;
using AgentFlow.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentFlow.Extensions;

public static class ExtensionDependencyInjection
{
    public static IServiceCollection AddAgentFlowExtensions(this IServiceCollection services)
    {
        services.TryAddSingleton<IExtensionRegistry, ExtensionRegistry>();
        services.TryAddSingleton<IToolInvoker, ToolInvoker>();
        
        // Register default tools (using Abstractions.IToolPlugin interface)
        // NOTE: We use the direct registration approach to avoid recursion
        services.TryAddSingleton<IToolPlugin, AgentFlow.Extensions.Tools.CalculatorTool>();
        services.TryAddSingleton<AgentFlow.Extensions.Tools.CalculatorTool>();
        
        services.TryAddSingleton<IToolPlugin, AgentFlow.Extensions.Tools.RiskTesterTool>();
        services.TryAddSingleton<AgentFlow.Extensions.Tools.RiskTesterTool>();

        
        services.TryAddSingleton<IToolPlugin, AgentFlow.Extensions.Plugins.CrmConnectorPlugin>();
        services.TryAddSingleton<IToolPlugin, AgentFlow.Extensions.Plugins.ErpConnectorPlugin>();
        services.TryAddSingleton<IToolPlugin, AgentFlow.Extensions.Plugins.QueueConnectorPlugin>();
        services.TryAddSingleton<IToolPlugin, AgentFlow.Extensions.Plugins.RagRetrieverPlugin>();

        // NOTE: Loan Officer demo plugins (BureauAPI, FinancialModel, EmailNotification)
        // use the new ToolSDK.IToolPlugin interface and are registered separately in
        // the integration tests - they don't use the legacy Abstractions.IToolPlugin.

        return services;
    }

    /// <summary>
    /// Registers a tool in the DI container and also in the AgentFlow extension registry.
    /// This enables both dependency injection and discovery by the agent engine / UI.
    /// </summary>
    public static IServiceCollection AddAgentFlowTool<T>(this IServiceCollection services) 
        where T : class, IToolPlugin
    {
        // Ensure core extension services are registered (idempotent with TryAdd)
        services.TryAddSingleton<IExtensionRegistry, ExtensionRegistry>();
        services.TryAddSingleton<IToolInvoker, ToolInvoker>();
        
        // Register as the interface so the engine can find all tools
        services.TryAddSingleton<IToolPlugin, T>();
        
        // Also register as its own type for direct injection/debugging
        services.TryAddSingleton<T>();

        // We need an initializer or a way to populate the registry.
        // For now, we'll use a simple approach where the registry can resolve from SP
        // or we manually add it. Let's use a startup hosted service or similar later.
        
        return services;
    }
}
