using AgentFlow.Abstractions;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentFlow.DSL;

// =========================================================================
// DSL PARSER
// =========================================================================

public interface IDslParser
{
    Result<AgentDefinitionDsl> Parse(string json);
    string Serialize(AgentDefinitionDsl definition);
}

public sealed class JsonDslParser : IDslParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public Result<AgentDefinitionDsl> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result<AgentDefinitionDsl>.Failure(
                Error.Validation("json", "DSL content cannot be empty."));

        try
        {
            var definition = JsonSerializer.Deserialize<AgentDefinitionDsl>(json, Options);
            if (definition is null)
                return Result<AgentDefinitionDsl>.Failure(
                    Error.Validation("json", "DSL deserialized to null. Check JSON structure."));

            return Result<AgentDefinitionDsl>.Success(definition);
        }
        catch (JsonException ex)
        {
            return Result<AgentDefinitionDsl>.Failure(
                Error.Validation("json", $"Invalid JSON: {ex.Message} at path '{ex.Path}'"));
        }
    }

    public string Serialize(AgentDefinitionDsl definition) =>
        JsonSerializer.Serialize(definition, Options);
}

// =========================================================================
// DSL VALIDATOR
// =========================================================================

public interface IDslValidator
{
    Task<DslValidationResult> ValidateAsync(AgentDefinitionDsl definition, DslValidationContext context, CancellationToken ct = default);
}

public sealed record DslValidationContext
{
    public required string TenantId { get; init; }
    public required string? CurrentPublishedVersion { get; init; }  // null if first version
    public IReadOnlyList<string> RegisteredToolNames { get; init; } = [];
    public IReadOnlyList<string> AvailableModelIds { get; init; } = [];
    public IReadOnlyList<string> PublishedPolicySetIds { get; init; } = [];
    public IReadOnlyList<string> PublishedPromptProfileIds { get; init; } = [];
    public bool IsProductionDeploy { get; init; } = false;
}

public sealed record DslValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<DslValidationError> Errors { get; init; } = [];
    public IReadOnlyList<DslValidationWarning> Warnings { get; init; } = [];

    public static DslValidationResult Valid() =>
        new() { IsValid = true };

    public static DslValidationResult Invalid(IReadOnlyList<DslValidationError> errors) =>
        new() { IsValid = false, Errors = errors };
}

public sealed record DslValidationError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Field { get; init; }
}

public sealed record DslValidationWarning
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Validates all 10 design-time invariants defined in the DSL Engine rules.
/// </summary>
public sealed class AgentDefinitionValidator : IDslValidator
{
    private static readonly Regex KeyPattern = new(@"^[a-z][a-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex SemverPattern = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);
    private static readonly string[] ValidModes = ["deterministic", "hybrid", "autonomous"];
    private static readonly string[] ValidStrategies = ["static", "task-based", "policy-based", "fallback-chain"];

    public Task<DslValidationResult> ValidateAsync(
        AgentDefinitionDsl definition,
        DslValidationContext context,
        CancellationToken ct = default)
    {
        var errors = new List<DslValidationError>();
        var warnings = new List<DslValidationWarning>();
        var agent = definition.Agent;

        // ── Rule 1: key is snake_case, unique per tenant (uniqueness checked by caller)
        if (!KeyPattern.IsMatch(agent.Key))
            errors.Add(new() { Code = "DSL001", Field = "agent.key",
                Message = $"Key '{agent.Key}' must be snake_case (lowercase letters, digits, underscores, starting with a letter)." });

        // ── Rule 2: version is semver and > current published version
        if (!SemverPattern.IsMatch(agent.Version))
        {
            errors.Add(new() { Code = "DSL002", Field = "agent.version",
                Message = $"Version '{agent.Version}' must follow semver (MAJOR.MINOR.PATCH)." });
        }
        else if (context.CurrentPublishedVersion is not null)
        {
            if (!IsVersionGreater(agent.Version, context.CurrentPublishedVersion))
                errors.Add(new() { Code = "DSL003", Field = "agent.version",
                    Message = $"Version '{agent.Version}' must be greater than current published version '{context.CurrentPublishedVersion}'. No downgrade allowed." });
        }

        // ── Rule 3: runtime mode is valid
        if (!ValidModes.Contains(agent.Runtime.Mode))
            errors.Add(new() { Code = "DSL004", Field = "agent.runtime.mode",
                Message = $"Mode '{agent.Runtime.Mode}' is invalid. Valid: {string.Join(", ", ValidModes)}." });

        // ── Rule 4: deterministic mode requires temperature 0.0
        if (agent.Runtime.Mode == "deterministic" && agent.Runtime.Temperature != 0.0)
            errors.Add(new() { Code = "DSL005", Field = "agent.runtime.temperature",
                Message = "Deterministic mode requires temperature: 0.0. Override not allowed." });

        // ── Rule 5: autonomous mode in production is blocked
        if (agent.Runtime.Mode == "autonomous" && context.IsProductionDeploy)
            errors.Add(new() { Code = "DSL006", Field = "agent.runtime.mode",
                Message = "Mode 'autonomous' is not allowed for production deployments in regulated environments." });

        // ── Rule 6: authorized tools ⊇ tools in flow steps
        var flowTools = agent.Flows
            .SelectMany(f => f.Steps.Select(s => s.Tool))
            .Distinct()
            .ToHashSet();

        var unauthorizedTools = flowTools.Except(agent.AuthorizedTools).ToList();
        foreach (var tool in unauthorizedTools)
            errors.Add(new() { Code = "DSL007", Field = "agent.flows[*].steps[*].tool",
                Message = $"Tool '{tool}' is used in a flow step but is not in 'authorizedTools'." });

        // ── Rule 7: all authorized tools exist in ToolRegistry
        var unregisteredTools = agent.AuthorizedTools.Except(context.RegisteredToolNames).ToList();
        foreach (var tool in unregisteredTools)
            errors.Add(new() { Code = "DSL008", Field = "agent.authorizedTools",
                Message = $"Tool '{tool}' is not registered in the ToolRegistry or is not Active." });

        // ── Rule 8: policySetId references published PolicySet
        if (!string.IsNullOrEmpty(agent.Policies.PolicySetId) &&
            !context.PublishedPolicySetIds.Contains(agent.Policies.PolicySetId))
            errors.Add(new() { Code = "DSL009", Field = "agent.policies.policySetId",
                Message = $"PolicySet '{agent.Policies.PolicySetId}' is not published." });

        // ── Rule 9: promptProfile references published PromptProfile
        if (!string.IsNullOrEmpty(agent.PromptProfile) &&
            !context.PublishedPromptProfileIds.Contains(agent.PromptProfile))
            errors.Add(new() { Code = "DSL010", Field = "agent.promptProfile",
                Message = $"PromptProfile '{agent.PromptProfile}' is not published." });

        // ── Rule 10: modelRouting.default and fallbackChain models exist
        if (!string.IsNullOrEmpty(agent.ModelRouting.Default) &&
            context.AvailableModelIds.Count > 0 &&
            !context.AvailableModelIds.Contains(agent.ModelRouting.Default))
            errors.Add(new() { Code = "DSL011", Field = "agent.modelRouting.default",
                Message = $"Model '{agent.ModelRouting.Default}' is not available in IModelRegistry." });

        var unavailableModels = agent.ModelRouting.FallbackChain
            .Where(m => context.AvailableModelIds.Count > 0 && !context.AvailableModelIds.Contains(m))
            .ToList();
        foreach (var model in unavailableModels)
            errors.Add(new() { Code = "DSL012", Field = "agent.modelRouting.fallbackChain",
                Message = $"Fallback model '{model}' is not available." });

        // ── Rule 11: production deploy requires test suite
        if (context.IsProductionDeploy && agent.TestSuite.TestCases.Count == 0)
            errors.Add(new() { Code = "DSL013", Field = "agent.testSuite.testCases",
                Message = "At least 1 test case is required for production deployment." });

        // ── Rule 12: model routing strategy valid
        if (!ValidStrategies.Contains(agent.ModelRouting.Strategy))
            errors.Add(new() { Code = "DSL014", Field = "agent.modelRouting.strategy",
                Message = $"Strategy '{agent.ModelRouting.Strategy}' is invalid. Valid: {string.Join(", ", ValidStrategies)}." });

        // ── Warnings
        if (agent.Runtime.Temperature > 0.7 && agent.Runtime.Mode != "autonomous")
            warnings.Add(new() { Code = "DSLW001",
                Message = $"Temperature {agent.Runtime.Temperature} is high for mode '{agent.Runtime.Mode}'. Consider lowering for consistency." });

        if (agent.Policies.MaxSteps > 20)
            warnings.Add(new() { Code = "DSLW002",
                Message = $"maxSteps={agent.Policies.MaxSteps} exceeds recommended maximum of 20. Document justification." });

        if (agent.ModelRouting.FallbackChain.Count == 0)
            warnings.Add(new() { Code = "DSLW003",
                Message = "No fallback chain defined. Single model failure will cause total agent failure." });

        if (agent.Policies.AllowParallelTools)
            warnings.Add(new() { Code = "DSLW004",
                Message = "allowParallelTools=true requires idempotency analysis of all authorized tools." });

        var result = errors.Count > 0
            ? DslValidationResult.Invalid(errors)
            : new DslValidationResult { IsValid = true, Warnings = warnings };

        return Task.FromResult(result);
    }

    /// <summary>Compares semver strings. Returns true if candidate > current.</summary>
    private static bool IsVersionGreater(string candidate, string current)
    {
        var c = ParseSemver(candidate);
        var p = ParseSemver(current);
        if (c is null || p is null) return false;
        var cv = c.Value;
        var pv = p.Value;
        if (cv.Major != pv.Major) return cv.Major > pv.Major;
        if (cv.Minor != pv.Minor) return cv.Minor > pv.Minor;
        return cv.Patch > pv.Patch;
    }

    private static (int Major, int Minor, int Patch)? ParseSemver(string version)
    {
        var parts = version.Split('.');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], out var major)) return null;
        if (!int.TryParse(parts[1], out var minor)) return null;
        if (!int.TryParse(parts[2], out var patch)) return null;
        return (major, minor, patch);
    }
}

// =========================================================================
// DSL LIFECYCLE STATE MACHINE
// =========================================================================

public enum AgentDslStatus
{
    Draft,
    Validating,
    TestPassed,
    Published,
    Deprecated,
    Archived
}

/// <summary>
/// Controls valid state transitions for AgentDefinition lifecycle.
/// Rule: Published agents are IMMUTABLE. Any change = new version.
/// </summary>
public static class AgentDslLifecycle
{
    private static readonly Dictionary<AgentDslStatus, AgentDslStatus[]> ValidTransitions = new()
    {
        [AgentDslStatus.Draft]       = [AgentDslStatus.Validating, AgentDslStatus.Archived],
        [AgentDslStatus.Validating]  = [AgentDslStatus.TestPassed, AgentDslStatus.Draft],
        [AgentDslStatus.TestPassed]  = [AgentDslStatus.Published, AgentDslStatus.Draft],
        [AgentDslStatus.Published]   = [AgentDslStatus.Deprecated],
        [AgentDslStatus.Deprecated]  = [AgentDslStatus.Archived],
        [AgentDslStatus.Archived]    = []
    };

    public static Result CanTransitionTo(AgentDslStatus current, AgentDslStatus target)
    {
        if (!ValidTransitions.TryGetValue(current, out var allowed))
            return Result.Failure(Error.Validation("status", $"Unknown status: {current}"));

        if (!allowed.Contains(target))
            return Result.Failure(Error.Validation("status",
                $"Cannot transition from '{current}' to '{target}'. " +
                $"Allowed transitions from '{current}': [{string.Join(", ", allowed)}]."));

        return Result.Success();
    }

    public static bool IsExecutable(AgentDslStatus status) =>
        status == AgentDslStatus.Published;

    public static bool IsImmutable(AgentDslStatus status) =>
        status is AgentDslStatus.Published or AgentDslStatus.Deprecated or AgentDslStatus.Archived;
}
