using AgentFlow.Abstractions;
using AgentFlow.DSL;
using AgentFlow.Extensions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

/// <summary>
/// DSL Engine API — Parse, validate, and manage agent definitions via DSL.
/// 
/// This controller is the HTTP interface to the DSL Engine.
/// It does NOT directly create agents — it validates DSL definitions
/// and returns structured results. The AgentsController handles persistence.
/// 
/// Endpoints:
/// - POST /validate   → Parse + validate a DSL JSON without saving
/// - POST /parse      → Parse-only (syntax check, no semantic validation)
/// - POST /compare    → Compare two DSL versions and detect changes
/// - GET  /schema     → Returns the expected DSL schema structure
/// </summary>
[ApiController]
[Route("api/v1/tenants/{tenantId}/dsl")]
[Authorize]
public sealed class DslController : ControllerBase
{
    private readonly IDslOrchestrator _orchestrator;
    private readonly IExtensionRegistry _extensionRegistry;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ILogger<DslController> _logger;

    public DslController(
        IDslOrchestrator orchestrator,
        IExtensionRegistry extensionRegistry,
        ITenantContextAccessor tenantContext,
        ILogger<DslController> logger)
    {
        _orchestrator = orchestrator;
        _extensionRegistry = extensionRegistry;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────
    // POST /api/v1/tenants/{tenantId}/dsl/validate
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parse and validate a DSL definition.
    /// Returns structured validation results including errors and warnings.
    /// Does NOT persist anything.
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(DslValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateAsync(
        [FromRoute] string tenantId,
        [FromBody] DslValidateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DslJson))
            return BadRequest(new { error = "DSL JSON content is required." });

        // Build validation context from current state
        var registeredTools = _extensionRegistry
            .GetTools()
            .Select(t => t.Name)
            .ToList();

        var context = new DslValidationContext
        {
            TenantId = tenantId,
            CurrentPublishedVersion = request.CurrentPublishedVersion,
            RegisteredToolNames = registeredTools,
            AvailableModelIds = [],  // TODO: populate from IModelRegistry when available
            PublishedPolicySetIds = [], // TODO: populate from IPolicyStore
            PublishedPromptProfileIds = [],
            IsProductionDeploy = request.IsProductionDeploy
        };

        var result = await _orchestrator.ParseAndValidateAsync(request.DslJson, context, ct);

        if (!result.IsSuccess)
        {
            return Ok(new DslValidationResponse
            {
                IsValid = false,
                Errors = [new DslValidationErrorDto
                {
                    Code = "PARSE_ERROR",
                    Message = result.Error!.Message
                }]
            });
        }

        var validated = result.Value!;
        return Ok(new DslValidationResponse
        {
            IsValid = validated.Validation.IsValid,
            AgentKey = validated.Definition.Agent.Key,
            AgentVersion = validated.Definition.Agent.Version,
            RuntimeMode = validated.Definition.Agent.Runtime.Mode,
            Errors = validated.Validation.Errors.Select(e => new DslValidationErrorDto
            {
                Code = e.Code,
                Message = e.Message,
                Field = e.Field
            }).ToList(),
            Warnings = validated.Validation.Warnings.Select(w => new DslValidationWarningDto
            {
                Code = w.Code,
                Message = w.Message
            }).ToList()
        });
    }

    // ─────────────────────────────────────────────────────────
    // POST /api/v1/tenants/{tenantId}/dsl/parse
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parse-only: checks JSON syntax and structure.
    /// Does NOT run semantic validation.
    /// </summary>
    [HttpPost("parse")]
    [ProducesResponseType(typeof(DslParseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Parse(
        [FromRoute] string tenantId,
        [FromBody] DslParseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DslJson))
            return BadRequest(new { error = "DSL JSON content is required." });

        var result = _orchestrator.Parse(request.DslJson);

        if (!result.IsSuccess)
        {
            return Ok(new DslParseResponse
            {
                IsValid = false,
                ErrorMessage = result.Error!.Message
            });
        }

        var def = result.Value!;
        return Ok(new DslParseResponse
        {
            IsValid = true,
            AgentKey = def.Agent.Key,
            AgentVersion = def.Agent.Version,
            RuntimeMode = def.Agent.Runtime.Mode,
            ToolCount = def.Agent.AuthorizedTools.Count,
            FlowCount = def.Agent.Flows.Count,
            TestCaseCount = def.Agent.TestSuite.TestCases.Count
        });
    }

    // ─────────────────────────────────────────────────────────
    // POST /api/v1/tenants/{tenantId}/dsl/compare
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compare two DSL versions and detect changes.
    /// Returns change detection results and required minimum version bump.
    /// </summary>
    [HttpPost("compare")]
    [ProducesResponseType(typeof(DslCompareResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Compare(
        [FromRoute] string tenantId,
        [FromBody] DslCompareRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateDslJson) ||
            string.IsNullOrWhiteSpace(request.CurrentDslJson))
            return BadRequest(new { error = "Both candidate and current DSL JSON are required." });

        var candidateResult = _orchestrator.Parse(request.CandidateDslJson);
        if (!candidateResult.IsSuccess)
            return BadRequest(new { error = $"Candidate DSL parse failed: {candidateResult.Error!.Message}" });

        var currentResult = _orchestrator.Parse(request.CurrentDslJson);
        if (!currentResult.IsSuccess)
            return BadRequest(new { error = $"Current DSL parse failed: {currentResult.Error!.Message}" });

        var candidate = candidateResult.Value!;
        var current = currentResult.Value!;

        var versionComparison = _orchestrator.CompareVersions(
            candidate.Agent.Version,
            current.Agent.Version);

        var changeDetection = DslVersioningService.DetectChanges(candidate, current);

        return Ok(new DslCompareResponse
        {
            VersionComparison = new VersionComparisonDto
            {
                IsValid = versionComparison.IsValid,
                CandidateVersion = candidate.Agent.Version,
                CurrentVersion = current.Agent.Version,
                UpgradeType = versionComparison.UpgradeType.ToString(),
                ErrorMessage = versionComparison.ErrorMessage
            },
            ChangeDetection = new ChangeDetectionDto
            {
                HasBreakingChanges = changeDetection.HasBreakingChanges,
                RequiredMinimumUpgrade = changeDetection.RequiredMinimumUpgrade.ToString(),
                Changes = changeDetection.Changes.Select(c => new ChangeDto
                {
                    Field = c.Field,
                    Type = c.Type.ToString(),
                    Description = c.Description
                }).ToList()
            }
        });
    }

    // ─────────────────────────────────────────────────────────
    // GET /api/v1/tenants/{tenantId}/dsl/lifecycle/transitions
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Get valid lifecycle transitions from a given status.
    /// </summary>
    [HttpGet("lifecycle/transitions")]
    [ProducesResponseType(typeof(LifecycleTransitionsResponse), StatusCodes.Status200OK)]
    public IActionResult GetTransitions(
        [FromQuery] string fromStatus)
    {
        if (!Enum.TryParse<AgentDslStatus>(fromStatus, ignoreCase: true, out var status))
            return BadRequest(new { error = $"Invalid status: '{fromStatus}'" });

        // Try all possible transitions
        var validTargets = Enum.GetValues<AgentDslStatus>()
            .Where(target => _orchestrator.CanTransition(status, target).IsSuccess)
            .Select(t => t.ToString())
            .ToList();

        return Ok(new LifecycleTransitionsResponse
        {
            CurrentStatus = status.ToString(),
            IsExecutable = AgentDslLifecycle.IsExecutable(status),
            IsImmutable = AgentDslLifecycle.IsImmutable(status),
            ValidTransitions = validTargets
        });
    }
}

// =========================================================================
// REQUEST/RESPONSE DTOs
// =========================================================================

public sealed record DslValidateRequest
{
    public string DslJson { get; init; } = string.Empty;
    public string? CurrentPublishedVersion { get; init; }
    public bool IsProductionDeploy { get; init; }
}

public sealed record DslValidationResponse
{
    public bool IsValid { get; init; }
    public string? AgentKey { get; init; }
    public string? AgentVersion { get; init; }
    public string? RuntimeMode { get; init; }
    public IReadOnlyList<DslValidationErrorDto> Errors { get; init; } = [];
    public IReadOnlyList<DslValidationWarningDto> Warnings { get; init; } = [];
}

public sealed record DslValidationErrorDto
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Field { get; init; }
}

public sealed record DslValidationWarningDto
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}

public sealed record DslParseRequest
{
    public string DslJson { get; init; } = string.Empty;
}

public sealed record DslParseResponse
{
    public bool IsValid { get; init; }
    public string? AgentKey { get; init; }
    public string? AgentVersion { get; init; }
    public string? RuntimeMode { get; init; }
    public int ToolCount { get; init; }
    public int FlowCount { get; init; }
    public int TestCaseCount { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record DslCompareRequest
{
    public string CandidateDslJson { get; init; } = string.Empty;
    public string CurrentDslJson { get; init; } = string.Empty;
}

public sealed record DslCompareResponse
{
    public required VersionComparisonDto VersionComparison { get; init; }
    public required ChangeDetectionDto ChangeDetection { get; init; }
}

public sealed record VersionComparisonDto
{
    public bool IsValid { get; init; }
    public string CandidateVersion { get; init; } = string.Empty;
    public string CurrentVersion { get; init; } = string.Empty;
    public string UpgradeType { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}

public sealed record ChangeDetectionDto
{
    public bool HasBreakingChanges { get; init; }
    public string RequiredMinimumUpgrade { get; init; } = string.Empty;
    public IReadOnlyList<ChangeDto> Changes { get; init; } = [];
}

public sealed record ChangeDto
{
    public required string Field { get; init; }
    public required string Type { get; init; }
    public required string Description { get; init; }
}

public sealed record LifecycleTransitionsResponse
{
    public string CurrentStatus { get; init; } = string.Empty;
    public bool IsExecutable { get; init; }
    public bool IsImmutable { get; init; }
    public IReadOnlyList<string> ValidTransitions { get; init; } = [];
}
