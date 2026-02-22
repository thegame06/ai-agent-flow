using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AgentFlow.Observability;

/// <summary>
/// AgentFlow observability setup.
/// 
/// Design decisions:
/// - ActivitySource per subsystem for granular tracing
/// - Meter for metrics (execution counts, latency, token usage, cost estimates)
/// - Every agent step = one trace span
/// - LLM decisions are recorded as span events (not just logs)
/// - Replay: executions can be reconstructed from span data
/// </summary>
public static class AgentFlowTelemetry
{
    public const string ServiceName = "AgentFlow";
    public const string Version = "1.0.0";

    // Activity sources for distributed tracing
    public static readonly ActivitySource EngineSource = new($"{ServiceName}.Engine", Version);
    public static readonly ActivitySource BrainSource = new($"{ServiceName}.Brain", Version);
    public static readonly ActivitySource ToolSource = new($"{ServiceName}.Tools", Version);
    public static readonly ActivitySource SecuritySource = new($"{ServiceName}.Security", Version);

    // Meters for metrics
    private static readonly Meter _meter = new($"{ServiceName}", Version);

    // Metrics
    public static readonly Counter<long> ExecutionsStarted =
        _meter.CreateCounter<long>("agentflow.executions.started", "executions", "Total executions started");

    public static readonly Counter<long> ExecutionsCompleted =
        _meter.CreateCounter<long>("agentflow.executions.completed", "executions", "Total executions completed");

    public static readonly Counter<long> ExecutionsFailed =
        _meter.CreateCounter<long>("agentflow.executions.failed", "executions", "Total executions failed");

    public static readonly Histogram<double> ExecutionDuration =
        _meter.CreateHistogram<double>("agentflow.executions.duration_ms", "ms", "Execution duration");

    public static readonly Counter<long> TokensUsed =
        _meter.CreateCounter<long>("agentflow.tokens.used", "tokens", "LLM tokens consumed");

    public static readonly Counter<long> ToolInvocations =
        _meter.CreateCounter<long>("agentflow.tools.invocations", "calls", "Tool invocation count");

    public static readonly Counter<long> ToolFailures =
        _meter.CreateCounter<long>("agentflow.tools.failures", "failures", "Tool failure count");

    public static readonly Histogram<double> ToolDuration =
        _meter.CreateHistogram<double>("agentflow.tools.duration_ms", "ms", "Tool execution duration");

    public static readonly Counter<long> SecurityViolations =
        _meter.CreateCounter<long>("agentflow.security.violations", "violations", "Security violation count");

    public static readonly Histogram<double> LlmLatency =
        _meter.CreateHistogram<double>("agentflow.brain.latency_ms", "ms", "LLM call latency");
}

/// <summary>
/// Instrumented agent execution tracer.
/// One Activity per major step. LLM rationale as span event (searchable in OTel backends).
/// </summary>
public static class ExecutionTracing
{
    public static Activity? StartExecution(string executionId, string agentId, string tenantId)
    {
        var activity = AgentFlowTelemetry.EngineSource.StartActivity(
            "AgentExecution",
            ActivityKind.Server);

        activity?.SetTag("agentflow.execution_id", executionId);
        activity?.SetTag("agentflow.agent_id", agentId);
        activity?.SetTag("agentflow.tenant_id", tenantId);

        return activity;
    }

    public static Activity? StartThinkStep(string executionId, int iteration)
    {
        return AgentFlowTelemetry.BrainSource.StartActivity("Think")
            ?.SetTag("agentflow.execution_id", executionId)
            ?.SetTag("agentflow.iteration", iteration);
    }

    public static void RecordThinkDecision(Activity? activity, string decision, string rationale)
    {
        activity?.AddEvent(new ActivityEvent("ThinkDecision", tags: new ActivityTagsCollection
        {
            { "decision", decision },
            { "rationale_preview", rationale[..Math.Min(200, rationale.Length)] } // First 200 chars
        }));
    }

    public static Activity? StartToolExecution(string toolName, string executionId)
    {
        return AgentFlowTelemetry.ToolSource.StartActivity("ToolExecution")
            ?.SetTag("agentflow.tool_name", toolName)
            ?.SetTag("agentflow.execution_id", executionId);
    }

    public static void RecordSecurityViolation(string violationType, string tenantId, string userId)
    {
        AgentFlowTelemetry.SecurityViolations.Add(1,
            new TagList
            {
                { "violation_type", violationType },
                { "tenant_id", tenantId },
                { "user_id", userId }
            });

        using var span = AgentFlowTelemetry.SecuritySource.StartActivity("SecurityViolation");
        span?.SetTag("violation_type", violationType);
        span?.SetTag("tenant_id", tenantId);
        span?.SetStatus(ActivityStatusCode.Error, violationType);
    }
}

public static class ObservabilityExtensions
{
    public static IServiceCollection AddAgentFlowObservability(
        this IServiceCollection services,
        string otlpEndpoint = "http://localhost:4317")
    {
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService(AgentFlowTelemetry.ServiceName, serviceVersion: AgentFlowTelemetry.Version))
                    .AddSource(AgentFlowTelemetry.EngineSource.Name)
                    .AddSource(AgentFlowTelemetry.BrainSource.Name)
                    .AddSource(AgentFlowTelemetry.ToolSource.Name)
                    .AddSource(AgentFlowTelemetry.SecuritySource.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(builder =>
            {
                builder
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService(AgentFlowTelemetry.ServiceName))
                    .AddMeter(AgentFlowTelemetry.ServiceName)
                    .AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }
}
