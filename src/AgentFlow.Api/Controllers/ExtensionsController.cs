using AgentFlow.Abstractions;
using AgentFlow.Extensions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/extensions")]
[Authorize]
public sealed class ExtensionsController : ControllerBase
{
    private readonly IExtensionRegistry _registry;
    private readonly IToolInvoker _invoker;
    private readonly ITenantContextAccessor _tenantContext;

    public ExtensionsController(
        IExtensionRegistry registry,
        IToolInvoker invoker,
        ITenantContextAccessor tenantContext)
    {
        _registry = registry;
        _invoker = invoker;
        _tenantContext = tenantContext;
    }

    [HttpGet("tools")]
    public IActionResult GetTools()
    {
        var tools = _registry.GetTools().Select(t => new
        {
            t.ExtensionId,
            t.Name,
            t.Version,
            t.Description,
            RiskLevel = t.RiskLevel.ToString(),
            t.RequiredPermissions,
            t.InputSchemaJson,
            t.OutputSchemaJson
        });

        return Ok(tools);
    }

    [HttpPost("tools/{name}/invoke")]
    public async Task<IActionResult> InvokeToolAsync(string name, [FromBody] ToolInvokeRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;

        var result = await _invoker.InvokeAsync(name, body.InputJson, new ToolExecutionContext
        {
            TenantId = context.TenantId,
            UserId = context.UserId,
            ExecutionId = $"ui-debug-{Guid.NewGuid():N}",
            StepId = "0",
            CorrelationId = "ui-debug",
            InputJson = body.InputJson
        }, ct);

        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalogAsync([FromQuery] string? q, CancellationToken ct)
    {
        var catalog = await _registry.BrowseCatalogAsync(q, ct);
        return Ok(catalog);
    }

    [HttpGet("sources")]
    public async Task<IActionResult> GetSourcesAsync(CancellationToken ct)
    {
        var sources = await _registry.GetSourcesAsync(ct);
        return Ok(sources);
    }

    [HttpPost("catalog/register")]
    public async Task<IActionResult> RegisterPackageAsync([FromBody] ExtensionPackageRegistrationRequest request, CancellationToken ct)
    {
        var result = await _registry.RegisterPackageAsync(request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("tenants/{tenantId}/install")]
    public async Task<IActionResult> InstallForTenantAsync(string tenantId, [FromBody] TenantInstallRequest request, CancellationToken ct)
    {
        var result = await _registry.SetTenantInstallStateAsync(tenantId, request.ExtensionId, installed: true, enabled: request.EnableAfterInstall, ct);
        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    [HttpPost("tenants/{tenantId}/uninstall")]
    public async Task<IActionResult> UninstallForTenantAsync(string tenantId, [FromBody] TenantInstallRequest request, CancellationToken ct)
    {
        var result = await _registry.SetTenantInstallStateAsync(tenantId, request.ExtensionId, installed: false, enabled: false, ct);
        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    [HttpPost("tenants/{tenantId}/enable")]
    public async Task<IActionResult> EnableForTenantAsync(string tenantId, [FromBody] TenantToggleRequest request, CancellationToken ct)
    {
        var result = await _registry.SetTenantEnabledStateAsync(tenantId, request.ExtensionId, enabled: true, ct);
        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    [HttpPost("tenants/{tenantId}/disable")]
    public async Task<IActionResult> DisableForTenantAsync(string tenantId, [FromBody] TenantToggleRequest request, CancellationToken ct)
    {
        var result = await _registry.SetTenantEnabledStateAsync(tenantId, request.ExtensionId, enabled: false, ct);
        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    [HttpGet("tenants/{tenantId}/states")]
    public async Task<IActionResult> GetTenantStatesAsync(string tenantId, CancellationToken ct)
    {
        var states = await _registry.GetTenantExtensionStatesAsync(tenantId, ct);
        return Ok(states);
    }

    [HttpPut("tenants/{tenantId}/allowlist")]
    public async Task<IActionResult> PutAllowlistAsync(string tenantId, [FromBody] TenantAllowlistRequest request, CancellationToken ct)
    {
        var result = await _registry.SetTenantAllowlistAsync(tenantId, request.ExtensionIds, ct);
        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    [HttpGet("tenants/{tenantId}/allowlist")]
    public async Task<IActionResult> GetAllowlistAsync(string tenantId, CancellationToken ct)
    {
        var allowlist = await _registry.GetTenantAllowlistAsync(tenantId, ct);
        return Ok(allowlist);
    }

    [HttpPost("catalog/{extensionId}/quarantine")]
    public async Task<IActionResult> QuarantineAsync(string extensionId, [FromBody] QuarantineRequest request, CancellationToken ct)
    {
        var result = await _registry.QuarantineExtensionAsync(extensionId, request.Reason, ct);
        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }
}

public sealed record ToolInvokeRequest
{
    public required string InputJson { get; init; }
}

public sealed record TenantInstallRequest
{
    public required string ExtensionId { get; init; }
    public bool EnableAfterInstall { get; init; } = true;
}

public sealed record TenantToggleRequest
{
    public required string ExtensionId { get; init; }
}

public sealed record TenantAllowlistRequest
{
    public IReadOnlyList<string> ExtensionIds { get; init; } = [];
}

public sealed record QuarantineRequest
{
    public required string Reason { get; init; }
}
