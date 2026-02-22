using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentFlow.DSL;

// =========================================================================
// AGENT DEFINITION DSL — Full Schema
// =========================================================================

/// <summary>
/// Represents a fully parsed AgentDefinition from DSL (JSON or YAML).
/// This is the source of truth for agent behavior.
/// Once Published: IMMUTABLE.
/// </summary>
public sealed class AgentDefinitionDsl
{
    [JsonPropertyName("agent")]
    public required AgentConfigDsl Agent { get; init; }
}

public sealed class AgentConfigDsl
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("runtime")]
    public RuntimeConfigDsl Runtime { get; init; } = new();

    [JsonPropertyName("modelRouting")]
    public ModelRoutingConfigDsl ModelRouting { get; init; } = new();

    [JsonPropertyName("promptProfile")]
    public string PromptProfile { get; init; } = string.Empty;

    [JsonPropertyName("authorizedTools")]
    public IReadOnlyList<string> AuthorizedTools { get; init; } = [];

    [JsonPropertyName("flows")]
    public IReadOnlyList<FlowDsl> Flows { get; init; } = [];

    [JsonPropertyName("policies")]
    public PoliciesDsl Policies { get; init; } = new();

    [JsonPropertyName("evaluation")]
    public EvaluationConfigDsl Evaluation { get; init; } = new();

    [JsonPropertyName("experiment")]
    public ExperimentConfigDsl Experiment { get; init; } = new();

    [JsonPropertyName("testSuite")]
    public TestSuiteDsl TestSuite { get; init; } = new();
}

public sealed class RuntimeConfigDsl
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "hybrid"; // deterministic | hybrid | autonomous

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 0.2;

    [JsonPropertyName("allowOverride")]
    public bool AllowOverride { get; init; } = false;

    [JsonPropertyName("maxIterations")]
    public int MaxIterations { get; init; } = 6;

    [JsonPropertyName("maxExecutionSeconds")]
    public int MaxExecutionSeconds { get; init; } = 120;

    [JsonPropertyName("toolCallTimeoutSeconds")]
    public int ToolCallTimeoutSeconds { get; init; } = 30;
}

public sealed class ModelRoutingConfigDsl
{
    [JsonPropertyName("strategy")]
    public string Strategy { get; init; } = "static"; // static | task-based | policy-based | fallback-chain

    [JsonPropertyName("default")]
    public string Default { get; init; } = string.Empty;

    [JsonPropertyName("fallbackChain")]
    public IReadOnlyList<string> FallbackChain { get; init; } = [];

    [JsonPropertyName("routingRules")]
    public IReadOnlyList<RoutingRuleDsl> RoutingRules { get; init; } = [];
}

public sealed class RoutingRuleDsl
{
    [JsonPropertyName("taskType")]
    public required string TaskType { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }
}

public sealed class FlowDsl
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("trigger")]
    public required TriggerDsl Trigger { get; init; }

    [JsonPropertyName("steps")]
    public IReadOnlyList<StepDsl> Steps { get; init; } = [];

    [JsonPropertyName("guardrails")]
    public GuardrailsDsl Guardrails { get; init; } = new();
}

public sealed class TriggerDsl
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "intent"; // intent | event | scheduled | manual

    [JsonPropertyName("value")]
    public string Value { get; init; } = "*"; // intent name or "*" for catch-all
}

public sealed class StepDsl
{
    [JsonPropertyName("tool")]
    public required string Tool { get; init; }

    [JsonPropertyName("required")]
    public bool Required { get; init; } = true;

    [JsonPropertyName("policyGroup")]
    public string? PolicyGroup { get; init; }

    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs { get; init; }

    [JsonPropertyName("retries")]
    public int Retries { get; init; } = 0;

    [JsonPropertyName("onError")]
    public string OnError { get; init; } = "halt"; // halt | skip | fallback
}

public sealed class GuardrailsDsl
{
    [JsonPropertyName("failOnToolError")]
    public bool FailOnToolError { get; init; } = true;

    [JsonPropertyName("requireAllSteps")]
    public bool RequireAllSteps { get; init; } = false;
}

public sealed class PoliciesDsl
{
    [JsonPropertyName("policySetId")]
    public string PolicySetId { get; init; } = string.Empty;

    [JsonPropertyName("maxSteps")]
    public int MaxSteps { get; init; } = 6;

    [JsonPropertyName("requireToolValidation")]
    public bool RequireToolValidation { get; init; } = true;

    [JsonPropertyName("allowParallelTools")]
    public bool AllowParallelTools { get; init; } = false;

    [JsonPropertyName("humanReviewOnEscalation")]
    public bool HumanReviewOnEscalation { get; init; } = true;
}

public sealed class EvaluationConfigDsl
{
    [JsonPropertyName("enableQualityScoring")]
    public bool EnableQualityScoring { get; init; } = false;

    [JsonPropertyName("qualityThreshold")]
    public double QualityThreshold { get; init; } = 0.80;

    [JsonPropertyName("requireHumanReviewOnScoreBelow")]
    public double RequireHumanReviewOnScoreBelow { get; init; } = 0.70;

    [JsonPropertyName("enableHallucinationDetection")]
    public bool EnableHallucinationDetection { get; init; } = false;

    [JsonPropertyName("evaluatorId")]
    public string? EvaluatorId { get; init; }

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "observing"; // observing | blocking
}

public sealed class ExperimentConfigDsl
{
    [JsonPropertyName("featureFlags")]
    public IReadOnlyList<string> FeatureFlags { get; init; } = [];

    [JsonPropertyName("canaryWeight")]
    public double CanaryWeight { get; init; } = 0.0;

    [JsonPropertyName("shadowEvalEnabled")]
    public bool ShadowEvalEnabled { get; init; } = false;
}

public sealed class TestSuiteDsl
{
    [JsonPropertyName("testCases")]
    public IReadOnlyList<TestCaseDsl> TestCases { get; init; } = [];
}

public sealed class TestCaseDsl
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("input")]
    public required string Input { get; init; }

    [JsonPropertyName("expectedIntent")]
    public string? ExpectedIntent { get; init; }

    [JsonPropertyName("expectedTools")]
    public IReadOnlyList<string> ExpectedTools { get; init; } = [];

    [JsonPropertyName("expectedOutputContains")]
    public IReadOnlyList<string> ExpectedOutputContains { get; init; } = [];

    [JsonPropertyName("expectedOutputNotContains")]
    public IReadOnlyList<string> ExpectedOutputNotContains { get; init; } = [];

    [JsonPropertyName("maxDurationMs")]
    public int MaxDurationMs { get; init; } = 10_000;

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];
}
