using AgentFlow.Api.AuthProfiles;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/runtime-model-profiles")]
[Authorize]
public sealed class RuntimeModelProfilesController : ControllerBase
{
    private readonly IRuntimeModelProfileStore _store;
    private readonly ITenantContextAccessor _tenantContext;

    public RuntimeModelProfilesController(IRuntimeModelProfileStore store, ITenantContextAccessor tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public IActionResult List([FromRoute] string tenantId, [FromQuery] string? runtimeKind = null)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();
        return Ok(_store.List(tenantId, runtimeKind));
    }

    [HttpGet("{profileId}")]
    public IActionResult Get([FromRoute] string tenantId, [FromRoute] string profileId)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();
        var profile = _store.Get(tenantId, profileId);
        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpPut("{profileId}")]
    public IActionResult Upsert([FromRoute] string tenantId, [FromRoute] string profileId, [FromBody] UpsertRuntimeModelProfileRequest request)
    {
        if (!CanManageTenant(tenantId)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Name es requerido." });
        if (string.IsNullOrWhiteSpace(request.RuntimeKind))
            return BadRequest(new { message = "RuntimeKind es requerido." });
        if (request.Roles.Count == 0)
            return BadRequest(new { message = "Debe definir al menos un rol de modelo (ej. brain/stt/tts)." });

        var actor = _tenantContext.Current!.UserId;
        var profile = new RuntimeModelProfile
        {
            Id = profileId,
            TenantId = tenantId,
            Name = request.Name.Trim(),
            RuntimeKind = request.RuntimeKind.Trim(),
            Roles = new Dictionary<string, string>(request.Roles, StringComparer.OrdinalIgnoreCase),
            Metadata = request.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            IsDefault = request.IsDefault,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = actor
        };

        _store.Upsert(profile);
        return Ok(profile);
    }

    [HttpDelete("{profileId}")]
    public IActionResult Delete([FromRoute] string tenantId, [FromRoute] string profileId)
    {
        if (!CanManageTenant(tenantId)) return Forbid();
        return _store.Delete(tenantId, profileId) ? NoContent() : NotFound();
    }

    private bool CanAccessTenant(string tenantId)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(AgentFlowPermissions.AgentRead) || context.IsPlatformAdmin);
    }

    private bool CanManageTenant(string tenantId)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(AgentFlowPermissions.AgentUpdate) || context.IsPlatformAdmin);
    }
}

public sealed record UpsertRuntimeModelProfileRequest
{
    public string Name { get; init; } = string.Empty;
    public string RuntimeKind { get; init; } = "Text";
    public Dictionary<string, string> Roles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string>? Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsDefault { get; init; } = false;
}
