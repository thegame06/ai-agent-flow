using AgentFlow.Abstractions;
using AgentFlow.TestRunner;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.TestRunner;

/// <summary>
/// DI registration for the Agent Test Runner subsystem.
/// </summary>
public static class TestRunnerServiceExtensions
{
    public static IServiceCollection AddTestRunner(this IServiceCollection services)
    {
        // TestRunner: Scoped (uses IAgentExecutor and IAgentEvaluator)
        services.AddScoped<IAgentTestRunner, AgentTestRunner>();

        return services;
    }
}
