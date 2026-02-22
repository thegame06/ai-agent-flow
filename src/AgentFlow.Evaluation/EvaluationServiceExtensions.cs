using AgentFlow.Abstractions;
using AgentFlow.Evaluation;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.Evaluation;

/// <summary>
/// DI registration for the Evaluation Engine subsystem.
/// 
/// Wiring:
/// - AgentEvaluator is the main orchestrator
/// - HallucinationDetector, PolicyComplianceChecker, ToolUsageAccuracyCalculator are static (no DI needed)
/// - LLM Judge Brain is optional (null = disable quality scoring)
/// - EvaluationResultStore handles persistence
/// </summary>
public static class EvaluationServiceExtensions
{
    /// <summary>
    /// Registers the Evaluation Engine with MongoDB persistence.
    /// Call AFTER AddMongoDB() has been called.
    /// </summary>
    public static IServiceCollection AddEvaluationEngine(this IServiceCollection services)
    {
        // Evaluator: scoped (one per request, uses scoped services)
        services.AddScoped<IAgentEvaluator>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AgentEvaluator>>();
            // Judge model is null by default — quality scoring disabled until configured
            // In production: inject a separate IAgentBrain instance pointed at a different model
            return new AgentEvaluator(logger, judgeModel: null);
        });

        // Evaluation Result Store (MongoDB)
        services.AddSingleton<IEvaluationResultStore, MongoEvaluationResultStore>();

        // Canary Routing Service (Experimentation Layer)
        services.AddSingleton<ICanaryRoutingService, CanaryRoutingService>();

        // Feature Flag Service (Experimentation Layer)
        services.AddSingleton<IFeatureFlagService, InMemoryFeatureFlagService>();

        // Segment Routing Service (Experimentation Layer)
        services.AddSingleton<ISegmentRoutingService, InMemorySegmentRoutingService>();

        // Background Auto-Evaluator
        services.AddHostedService<EvaluationBackgroundWorker>();

        return services;
    }

    /// <summary>
    /// Registers the Evaluation Engine with an LLM Judge model for quality scoring.
    /// Use this when you have a dedicated judge model configured.
    /// </summary>
    public static IServiceCollection AddEvaluationEngine(
        this IServiceCollection services,
        Func<IServiceProvider, IAgentBrain> judgeModelFactory)
    {
        services.AddScoped<IAgentEvaluator>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AgentEvaluator>>();
            var judge = judgeModelFactory(sp);
            return new AgentEvaluator(logger, judge);
        });

        services.AddSingleton<IEvaluationResultStore, MongoEvaluationResultStore>();

        // Canary Routing Service (Experimentation Layer)
        services.AddSingleton<ICanaryRoutingService, CanaryRoutingService>();

        // Feature Flag Service (Experimentation Layer)
        services.AddSingleton<IFeatureFlagService, InMemoryFeatureFlagService>();

        // Segment Routing Service (Experimentation Layer)
        services.AddSingleton<ISegmentRoutingService, InMemorySegmentRoutingService>();

        return services;
    }
}
