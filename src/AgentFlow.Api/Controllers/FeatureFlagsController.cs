using AgentFlow.Evaluation;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

/// <summary>
/// API for managing feature flags in the Experimentation Layer.
/// </summary>
[ApiController]
[Route("api/v1/tenants/{tenantId}/feature-flags")]
[Authorize]
public sealed class FeatureFlagsController : ControllerBase
{
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ILogger<FeatureFlagsController> _logger;

    public FeatureFlagsController(
        IFeatureFlagService featureFlagService,
        ITenantContextAccessor tenantContext,
        ILogger<FeatureFlagsController> logger)
    {
        _featureFlagService = featureFlagService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Check if a specific feature is enabled for a given context.
    /// </summary>
    [HttpPost("{flagKey}/check")]
    [ProducesResponseType(typeof(FeatureFlagCheckResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckFeatureFlagAsync(
        [FromRoute] string tenantId,
        [FromRoute] string flagKey,
        [FromBody] FeatureFlagCheckRequest request,
        CancellationToken ct)
    {
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin)
            return Forbid();

        var context = new FeatureFlagContext
        {
            AgentId = request.AgentId,
            UserId = request.UserId ?? ctx.UserId,
            UserSegments = request.UserSegments ?? [],
            Metadata = request.Metadata ?? new Dictionary<string, string>()
        };

        var isEnabled = await _featureFlagService.IsEnabledAsync(
            tenantId, flagKey, context, ct);

        return Ok(new FeatureFlagCheckResponse
        {
            FlagKey = flagKey,
            IsEnabled = isEnabled,
            CheckedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Get all enabled features for a given context.
    /// </summary>
    [HttpPost("enabled")]
    [ProducesResponseType(typeof(EnabledFeaturesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEnabledFeaturesAsync(
        [FromRoute] string tenantId,
        [FromBody] FeatureFlagCheckRequest request,
        CancellationToken ct)
    {
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin)
            return Forbid();

        var context = new FeatureFlagContext
        {
            AgentId = request.AgentId,
            UserId = request.UserId ?? ctx.UserId,
            UserSegments = request.UserSegments ?? [],
            Metadata = request.Metadata ?? new Dictionary<string, string>()
        };

        var features = await _featureFlagService.GetEnabledFeaturesAsync(
            tenantId, context, ct);

        return Ok(new EnabledFeaturesResponse
        {
            EnabledFeatures = features,
            CheckedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Create or update a feature flag.
    /// </summary>
    [HttpPut("{flagKey}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetFeatureFlagAsync(
        [FromRoute] string tenantId,
        [FromRoute] string flagKey,
        [FromBody] FeatureFlagUpdateRequest request,
        CancellationToken ct)
    {
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin)
            return Forbid();

        var definition = new FeatureFlagDefinition
        {
            FlagKey = flagKey,
            TenantId = tenantId,
            Description = request.Description,
            IsEnabled = request.IsEnabled,
            Targeting = new FeatureFlagTargeting
            {
                AgentIds = request.Targeting?.AgentIds ?? [],
                UserSegments = request.Targeting?.UserSegments ?? [],
                RolloutPercentage = request.Targeting?.RolloutPercentage ?? 1.0
            },
            CreatedBy = ctx.UserId
        };

        var result = await _featureFlagService.SetFeatureFlagAsync(
            tenantId, definition, ct);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error!.Message });

        _logger.LogInformation(
            "Feature flag {FlagKey} updated by {UserId} in tenant {TenantId}. Enabled={IsEnabled}",
            flagKey, ctx.UserId, tenantId, request.IsEnabled);

        return Ok(new { message = "Feature flag updated successfully" });
    }
}

// --- DTOs ---

public sealed record FeatureFlagCheckRequest
{
    public string? AgentId { get; init; }
    public string? UserId { get; init; }
    public IReadOnlyList<string>? UserSegments { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record FeatureFlagCheckResponse
{
    public required string FlagKey { get; init; }
    public required bool IsEnabled { get; init; }
    public required DateTimeOffset CheckedAt { get; init; }
}

public sealed record EnabledFeaturesResponse
{
    public required IReadOnlyList<string> EnabledFeatures { get; init; }
    public required DateTimeOffset CheckedAt { get; init; }
}

public sealed record FeatureFlagUpdateRequest
{
    public required string Description { get; init; }
    public required bool IsEnabled { get; init; }
    public FeatureFlagTargetingDto? Targeting { get; init; }
}

public sealed record FeatureFlagTargetingDto
{
    public IReadOnlyList<string> AgentIds { get; init; } = [];
    public IReadOnlyList<string> UserSegments { get; init; } = [];
    public double RolloutPercentage { get; init; } = 1.0;
}
