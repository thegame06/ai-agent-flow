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

    public static readonly Counter<long> HandoffHops =
        _meter.CreateCounter<long>("agentflow.handoff.hops", "hops", "Total manager->subagent handoff hops");

    public static readonly Histogram<double> HandoffHopLatency =
        _meter.CreateHistogram<double>("agentflow.handoff.latency_ms", "ms", "Latency per manager->subagent handoff hop");

    public static readonly Counter<long> ExecutionOutcomes =
        _meter.CreateCounter<long>("agentflow.executions.outcomes", "executions", "Execution outcomes by status/segment/variant/brain");

    public static readonly Histogram<double> ExecutionLatencyBySegment =
        _meter.CreateHistogram<double>("agentflow.executions.latency_by_segment_ms", "ms", "Execution latency by segment/variant/brain");

    public static readonly Counter<long> EvaluationComparisons =
        _meter.CreateCounter<long>("agentflow.evaluations.comparisons", "comparisons", "Champion/challenger and SK/MAF evaluation comparison events");

    public static readonly Histogram<double> EvaluationScoreDelta =
        _meter.CreateHistogram<double>("agentflow.evaluations.score_delta", "ratio", "Absolute score delta for champion/challenger or SK/MAF pairs");

    public static readonly Histogram<double> TokenCostPerExecution =
        _meter.CreateHistogram<double>("agentflow.tokens.cost_per_execution_usd", "usd", "Estimated token cost per execution");

    public static readonly Histogram<double> TokenCostPer1K =
        _meter.CreateHistogram<double>("agentflow.tokens.cost_per_1k_usd", "usd", "Estimated USD cost per 1K tokens");

    public static readonly Counter<long> CanaryAssignments =
        _meter.CreateCounter<long>("agentflow.canary.assignments", "assignments", "Canary/champion routing assignments by segment");

    public static readonly Counter<long> FeatureFlagChecks =
        _meter.CreateCounter<long>("agentflow.feature_flags.checks", "checks", "Feature flag checks by flag and segment");

    public static readonly Counter<long> SegmentRoutingDecisions =
        _meter.CreateCounter<long>("agentflow.segment_routing.decisions", "decisions", "Segment routing decisions");

    public static readonly Counter<long> McpToolFailures =
        _meter.CreateCounter<long>("agentflow.tools.mcp.failures", "failures", "Failures for MCP-backed tool calls");

    public static readonly Histogram<double> ApiEndpointLatency =
        _meter.CreateHistogram<double>("agentflow.api.endpoint.latency_ms", "ms", "Endpoint latency by controller/action");

    public static readonly Counter<long> ExecutionEvents =
        _meter.CreateCounter<long>("agentflow.execution.events", "events", "Unified execution events by event_type/severity/policy");

    public static readonly Histogram<double> ExecutionEventLatency =
        _meter.CreateHistogram<double>("agentflow.execution.event.latency_ms", "ms", "Unified event latency");

    public static readonly Counter<long> ExecutionRetries =
        _meter.CreateCounter<long>("agentflow.execution.retries", "retries", "Execution retries by tool_name");

    public static readonly Counter<long> ExecutionDenials =
        _meter.CreateCounter<long>("agentflow.execution.denials", "denials", "Execution denials by policy decision");

    public static readonly Histogram<double> ExecutionCostEstimate =
        _meter.CreateHistogram<double>("agentflow.execution.cost_estimate_usd", "usd", "Estimated cost by flow/correlation id");
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
