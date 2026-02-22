using AgentFlow.DSL;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.DSL;

/// <summary>
/// DI registration for the DSL Engine subsystem.
/// Registers Parser, Validator, Orchestrator, and Versioning services.
/// </summary>
public static class DslServiceExtensions
{
    public static IServiceCollection AddDslEngine(this IServiceCollection services)
    {
        // Parser: stateless, singleton-safe
        services.AddSingleton<IDslParser, JsonDslParser>();

        // Validator: stateless, singleton-safe
        services.AddSingleton<IDslValidator, AgentDefinitionValidator>();

        // Orchestrator: composes parser + validator, singleton-safe
        services.AddSingleton<IDslOrchestrator, DslOrchestrator>();

        return services;
    }
}
