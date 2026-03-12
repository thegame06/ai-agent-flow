using AgentFlow.Abstractions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/mcp")]
[Authorize]
public sealed class TenantMcpController : ControllerBase
{
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ITenantMcpSettingsStore _store;

    public TenantMcpController(ITenantContextAccessor tenantContext, ITenantMcpSettingsStore store)
    {
        _tenantContext = tenantContext;
        _store = store;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var settings = await _store.GetAsync(tenantId, ct);
        return Ok(settings);
    }

    [HttpPost("enable")]
    public async Task<IActionResult> Enable([FromRoute] string tenantId, [FromBody] EnableTenantMcpRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var current = await _store.GetAsync(tenantId, ct);
        var updated = await _store.SaveAsync(current with
        {
            TenantId = tenantId,
            Enabled = true,
            Runtime = "MicrosoftAgentFramework",
            TimeoutSeconds = request.TimeoutSeconds ?? current.TimeoutSeconds,
            RetryCount = request.RetryCount ?? current.RetryCount,
            AllowedServers = request.AllowedServers?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ?? current.AllowedServers,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = context.UserId
        }, ct);

        return Ok(updated);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromRoute] string tenantId, [FromBody] SaveTenantMcpSettingsRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var runtime = string.IsNullOrWhiteSpace(request.Runtime)
            ? "MicrosoftAgentFramework"
            : request.Runtime;

        if (!string.Equals(runtime, "MicrosoftAgentFramework", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only MicrosoftAgentFramework is supported as MCP runtime." });

        var updated = await _store.SaveAsync(new TenantMcpSettings
        {
            TenantId = tenantId,
            Enabled = request.Enabled,
            Runtime = runtime,
            TimeoutSeconds = Math.Clamp(request.TimeoutSeconds, 5, 120),
            RetryCount = Math.Clamp(request.RetryCount, 0, 3),
            AllowedServers = (request.AllowedServers ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = context.UserId
        }, ct);

        return Ok(updated);
    }
}

public sealed class EnableTenantMcpRequest
{
    public string[]? AllowedServers { get; set; }
    public int? TimeoutSeconds { get; set; }
    public int? RetryCount { get; set; }
}

public sealed class SaveTenantMcpSettingsRequest
{
    public bool Enabled { get; set; }
    public string Runtime { get; set; } = "MicrosoftAgentFramework";
    public int TimeoutSeconds { get; set; } = 20;
    public int RetryCount { get; set; } = 1;
    public string[]? AllowedServers { get; set; }
}
