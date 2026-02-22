using AgentFlow.Abstractions;

namespace AgentFlow.DSL;

// =========================================================================
// DSL ORCHESTRATOR — Single entry point for DSL operations
// =========================================================================

/// <summary>
/// Orchestrates DSL operations: Parse → Validate → Lifecycle.
/// Used by API controllers and the Runtime to interact with the DSL layer.
/// 
/// Design decision:
/// - Parsing and validation are separated (single responsibility)
/// - Validation context assembly is the orchestrator's job (not the controller's)
/// - Lifecycle transitions are validated BEFORE persistence
/// </summary>
public interface IDslOrchestrator
{
    /// <summary>Parse raw JSON into a typed DSL object.</summary>
    Result<AgentDefinitionDsl> Parse(string json);

    /// <summary>Validate a parsed DSL against design-time invariants.</summary>
    Task<DslValidationResult> ValidateAsync(
        AgentDefinitionDsl definition,
        DslValidationContext context,
        CancellationToken ct = default);

    /// <summary>Parse + Validate in a single call (most common usage).</summary>
    Task<Result<ValidatedDsl>> ParseAndValidateAsync(
        string json,
        DslValidationContext context,
        CancellationToken ct = default);

    /// <summary>Check if a lifecycle transition is valid.</summary>
    Result CanTransition(AgentDslStatus current, AgentDslStatus target);

    /// <summary>Compare two versions and validate upgrade path.</summary>
    DslVersionComparison CompareVersions(string candidateVersion, string? currentVersion);
}

/// <summary>
/// A validated DSL paired with its validation result.
/// This is the output of a successful Parse + Validate operation.
/// </summary>
public sealed record ValidatedDsl
{
    public required AgentDefinitionDsl Definition { get; init; }
    public required DslValidationResult Validation { get; init; }
}

public sealed class DslOrchestrator : IDslOrchestrator
{
    private readonly IDslParser _parser;
    private readonly IDslValidator _validator;

    public DslOrchestrator(IDslParser parser, IDslValidator validator)
    {
        _parser = parser;
        _validator = validator;
    }

    public Result<AgentDefinitionDsl> Parse(string json)
        => _parser.Parse(json);

    public Task<DslValidationResult> ValidateAsync(
        AgentDefinitionDsl definition,
        DslValidationContext context,
        CancellationToken ct = default)
        => _validator.ValidateAsync(definition, context, ct);

    public async Task<Result<ValidatedDsl>> ParseAndValidateAsync(
        string json,
        DslValidationContext context,
        CancellationToken ct = default)
    {
        var parseResult = _parser.Parse(json);
        if (!parseResult.IsSuccess)
            return Result<ValidatedDsl>.Failure(parseResult.Error!);

        var validation = await _validator.ValidateAsync(parseResult.Value!, context, ct);

        if (!validation.IsValid)
        {
            var errorMsg = string.Join("; ", validation.Errors.Select(e => $"[{e.Code}] {e.Message}"));
            return Result<ValidatedDsl>.Failure(
                Error.Validation("dsl", $"DSL validation failed: {errorMsg}"));
        }

        return Result<ValidatedDsl>.Success(new ValidatedDsl
        {
            Definition = parseResult.Value!,
            Validation = validation
        });
    }

    public Result CanTransition(AgentDslStatus current, AgentDslStatus target)
        => AgentDslLifecycle.CanTransitionTo(current, target);

    public DslVersionComparison CompareVersions(string candidateVersion, string? currentVersion)
        => DslVersioningService.Compare(candidateVersion, currentVersion);
}

// =========================================================================
// DSL VERSIONING SERVICE
// =========================================================================

/// <summary>
/// Semver comparison and upgrade path validation.
/// 
/// Rules:
/// - MAJOR: change in flows, authorized tools, runtime mode → breaking
/// - MINOR: change in evaluation config, experiments, policies → non-breaking  
/// - PATCH: prompt typo fix, temperature tweak → safe
/// - Downgrade is ALWAYS rejected
/// - Same version is ALWAYS rejected (must increment)
/// </summary>
public static class DslVersioningService
{
    public static DslVersionComparison Compare(string candidateVersion, string? currentVersion)
    {
        if (currentVersion is null)
        {
            return new DslVersionComparison
            {
                IsValid = true,
                IsFirstVersion = true,
                Candidate = ParseSemver(candidateVersion) ?? new SemverTuple(0, 0, 0),
                Current = null,
                UpgradeType = VersionUpgradeType.Initial
            };
        }

        var candidate = ParseSemver(candidateVersion);
        var current = ParseSemver(currentVersion);

        if (candidate is null)
        {
            return new DslVersionComparison
            {
                IsValid = false,
                ErrorMessage = $"Candidate version '{candidateVersion}' is not valid semver."
            };
        }

        if (current is null)
        {
            return new DslVersionComparison
            {
                IsValid = false,
                ErrorMessage = $"Current version '{currentVersion}' is not valid semver."
            };
        }

        var c = candidate.Value;
        var p = current.Value;

        // Same version
        if (c.Major == p.Major && c.Minor == p.Minor && c.Patch == p.Patch)
        {
            return new DslVersionComparison
            {
                IsValid = false,
                Candidate = c,
                Current = p,
                ErrorMessage = $"Version '{candidateVersion}' is the same as current. Must increment."
            };
        }

        // Downgrade
        bool isDowngrade = c.Major < p.Major ||
                           (c.Major == p.Major && c.Minor < p.Minor) ||
                           (c.Major == p.Major && c.Minor == p.Minor && c.Patch < p.Patch);

        if (isDowngrade)
        {
            return new DslVersionComparison
            {
                IsValid = false,
                Candidate = c,
                Current = p,
                ErrorMessage = $"Version downgrade from '{currentVersion}' to '{candidateVersion}' is not allowed."
            };
        }

        var upgradeType = c.Major > p.Major
            ? VersionUpgradeType.Major
            : c.Minor > p.Minor
                ? VersionUpgradeType.Minor
                : VersionUpgradeType.Patch;

        return new DslVersionComparison
        {
            IsValid = true,
            Candidate = c,
            Current = p,
            UpgradeType = upgradeType
        };
    }

    /// <summary>
    /// Detects what actually changed between two DSL definitions
    /// to validate the upgrade type is correct (e.g., major bump for breaking changes).
    /// </summary>
    public static DslChangeDetection DetectChanges(AgentDefinitionDsl candidate, AgentDefinitionDsl current)
    {
        var changes = new List<DslChange>();
        var a = candidate.Agent;
        var b = current.Agent;

        // Breaking changes (require MAJOR bump)
        if (a.Runtime.Mode != b.Runtime.Mode)
            changes.Add(new DslChange { Field = "agent.runtime.mode", Type = ChangeType.Breaking,
                Description = $"Mode changed from '{b.Runtime.Mode}' to '{a.Runtime.Mode}'" });

        if (!a.AuthorizedTools.SequenceEqual(b.AuthorizedTools))
            changes.Add(new DslChange { Field = "agent.authorizedTools", Type = ChangeType.Breaking,
                Description = "Authorized tools changed" });

        if (a.Flows.Count != b.Flows.Count)
            changes.Add(new DslChange { Field = "agent.flows", Type = ChangeType.Breaking,
                Description = $"Flow count changed from {b.Flows.Count} to {a.Flows.Count}" });

        // Non-breaking changes (require MINOR bump minimum)
        if (a.Policies.PolicySetId != b.Policies.PolicySetId)
            changes.Add(new DslChange { Field = "agent.policies.policySetId", Type = ChangeType.NonBreaking,
                Description = "Policy set changed" });

        if (a.ModelRouting.Strategy != b.ModelRouting.Strategy)
            changes.Add(new DslChange { Field = "agent.modelRouting.strategy", Type = ChangeType.NonBreaking,
                Description = "Model routing strategy changed" });

        // Safe changes (PATCH is enough)
        if (a.Runtime.Temperature != b.Runtime.Temperature)
            changes.Add(new DslChange { Field = "agent.runtime.temperature", Type = ChangeType.Safe,
                Description = $"Temperature changed from {b.Runtime.Temperature} to {a.Runtime.Temperature}" });

        if (a.Role != b.Role)
            changes.Add(new DslChange { Field = "agent.role", Type = ChangeType.Safe,
                Description = "Role/prompt changed" });

        var requiredMinimum = changes.Any(c => c.Type == ChangeType.Breaking)
            ? VersionUpgradeType.Major
            : changes.Any(c => c.Type == ChangeType.NonBreaking)
                ? VersionUpgradeType.Minor
                : VersionUpgradeType.Patch;

        return new DslChangeDetection
        {
            Changes = changes,
            RequiredMinimumUpgrade = requiredMinimum
        };
    }

    private static SemverTuple? ParseSemver(string version)
    {
        var parts = version.Split('.');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], out var major)) return null;
        if (!int.TryParse(parts[1], out var minor)) return null;
        if (!int.TryParse(parts[2], out var patch)) return null;
        return new SemverTuple(major, minor, patch);
    }
}

// =========================================================================
// VERSION COMPARISON TYPES
// =========================================================================

public record struct SemverTuple(int Major, int Minor, int Patch)
{
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public sealed record DslVersionComparison
{
    public bool IsValid { get; init; }
    public bool IsFirstVersion { get; init; }
    public SemverTuple? Candidate { get; init; }
    public SemverTuple? Current { get; init; }
    public VersionUpgradeType UpgradeType { get; init; }
    public string? ErrorMessage { get; init; }
}

public enum VersionUpgradeType { Initial, Patch, Minor, Major }

public sealed record DslChangeDetection
{
    public IReadOnlyList<DslChange> Changes { get; init; } = [];
    public VersionUpgradeType RequiredMinimumUpgrade { get; init; }
    public bool HasBreakingChanges => Changes.Any(c => c.Type == ChangeType.Breaking);
}

public sealed record DslChange
{
    public required string Field { get; init; }
    public required ChangeType Type { get; init; }
    public required string Description { get; init; }
}

public enum ChangeType { Safe, NonBreaking, Breaking }
