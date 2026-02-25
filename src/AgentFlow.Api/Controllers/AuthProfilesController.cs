using AgentFlow.Api.AuthProfiles;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/auth-profiles")]
[Authorize]
public sealed class AuthProfilesController : ControllerBase
{
    private readonly IAuthProfilesStore _store;
    private readonly ITenantContextAccessor _tenantContext;

    public AuthProfilesController(IAuthProfilesStore store, ITenantContextAccessor tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public IActionResult List([FromRoute] string tenantId, [FromQuery] string? provider = null)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        return Ok(_store.List(tenantId, provider));
    }

    [HttpPost]
    public IActionResult Upsert([FromRoute] string tenantId, [FromBody] UpsertProviderAuthProfileRequest request)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Provider))
            return BadRequest(new { message = "provider is required." });

        if (string.IsNullOrWhiteSpace(request.ProfileId))
            return BadRequest(new { message = "profileId is required." });

        if (request.AuthType.Equals("api_key", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.Secret)
            && _store.Get(tenantId, request.ProfileId) is null)
        {
            return BadRequest(new { message = "secret is required when creating a new api_key profile." });
        }

        var profile = _store.Upsert(tenantId, request);
        return Ok(profile);
    }

    [HttpPost("{profileId}/test")]
    public IActionResult Test([FromRoute] string tenantId, [FromRoute] string profileId)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var profile = _store.Get(tenantId, profileId);
        if (profile is null) return NotFound(new { message = "Profile not found." });

        // MVP: lightweight validation, real provider test can be added with IModelProvider credentials wiring.
        var isExpired = profile.ExpiresAt is not null && profile.ExpiresAt <= DateTimeOffset.UtcNow;

        return Ok(new
        {
            profile.ProfileId,
            profile.Provider,
            Healthy = !isExpired,
            CheckedAt = DateTimeOffset.UtcNow,
            Reason = isExpired ? "Token/profile expired" : "Profile is available"
        });
    }

    [HttpDelete("{profileId}")]
    public IActionResult Delete([FromRoute] string tenantId, [FromRoute] string profileId)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var removed = _store.Delete(tenantId, profileId);
        if (!removed) return NotFound(new { message = "Profile not found." });

        return NoContent();
    }
}
