using AgentFlow.Abstractions;
using AgentFlow.Policy;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.Policy;

/// <summary>
/// DI registration for the Policy Engine subsystem.
/// 
/// Design:
/// - CompositePolicyEngine is the single IPolicyEngine implementation
/// - PolicyEvaluators are registered as IEnumerable<IPolicyEvaluator> (DI scans all)
/// - IPolicyStore chooses InMemory (tests) or MongoDB (production) based on config
/// </summary>
public static class PolicyServiceExtensions
{
    /// <summary>
    /// Registers the Policy Engine with MongoDB persistence.
    /// Use this for production/staging.
    /// </summary>
    public static IServiceCollection AddPolicyEngine(this IServiceCollection services)
    {
        // Core evaluators (shipped with the platform)
        services.AddSingleton<IPolicyEvaluator, RegexPolicyEvaluator>();
        services.AddSingleton<IPolicyEvaluator, PromptInjectionEvaluator>();
        services.AddSingleton<IPolicyEvaluator, RateLimitPolicyEvaluator>();

        // Policy Store (Mongo for production)
        services.AddSingleton<MongoPolicyStore>();
        services.AddSingleton<IPolicyStore>(sp => sp.GetRequiredService<MongoPolicyStore>());

        // Composite engine
        services.AddSingleton<IPolicyEngine, CompositePolicyEngine>();

        return services;
    }

    /// <summary>
    /// Registers the Policy Engine with InMemory store.
    /// Use this for unit/integration tests only.
    /// </summary>
    public static IServiceCollection AddPolicyEngineInMemory(this IServiceCollection services)
    {
        services.AddSingleton<IPolicyEvaluator, RegexPolicyEvaluator>();
        services.AddSingleton<IPolicyEvaluator, PromptInjectionEvaluator>();
        services.AddSingleton<IPolicyEvaluator, RateLimitPolicyEvaluator>();

        services.AddSingleton<IPolicyStore, InMemoryPolicyStore>();
        services.AddSingleton<IPolicyEngine, CompositePolicyEngine>();

        return services;
    }
}
