using AgentFlow.Abstractions;
using AgentFlow.Security;
using AgentFlow.Api.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/settings")]
[Authorize]
public sealed class TenantSettingsController : ControllerBase
{
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ITenantRuntimeSettingsService _service;

    public TenantSettingsController(ITenantContextAccessor tenantContext, ITenantRuntimeSettingsService service)
    {
        _tenantContext = tenantContext;
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var settings = await _service.GetAsync(tenantId, ct);
        return Ok(settings);
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromRoute] string tenantId, [FromBody] SaveTenantSettingsRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var saved = await _service.SaveAsync(tenantId, new TenantRuntimeSettings
        {
            TenantName = request.TenantName,
            DefaultApiVersion = request.DefaultApiVersion,
            EnforceRbac = request.EnforceRbac,
            PromptInjectionGuard = request.PromptInjectionGuard,
            SandboxDangerousTools = request.SandboxDangerousTools,
            AuditLogging = request.AuditLogging,
            MaxStepsPerExecution = request.MaxStepsPerExecution,
            TimeoutPerStepSeconds = request.TimeoutPerStepSeconds,
            MaxTokensPerExecution = request.MaxTokensPerExecution,
            MaxConcurrentExecutions = request.MaxConcurrentExecutions,
            OtlpExport = request.OtlpExport,
            OtlpEndpoint = request.OtlpEndpoint,
            ExecutionReplay = request.ExecutionReplay,
            LlmDecisionLogging = request.LlmDecisionLogging
        }, context.UserId, ct);

        return Ok(saved);
    }
}

public sealed class SaveTenantSettingsRequest
{
    public string TenantName { get; set; } = "Tenant";
    public string DefaultApiVersion { get; set; } = "v1";
    public bool EnforceRbac { get; set; } = true;
    public bool PromptInjectionGuard { get; set; } = true;
    public bool SandboxDangerousTools { get; set; } = true;
    public bool AuditLogging { get; set; } = true;

    public int MaxStepsPerExecution { get; set; } = 25;
    public int TimeoutPerStepSeconds { get; set; } = 30;
    public int MaxTokensPerExecution { get; set; } = 100000;
    public int MaxConcurrentExecutions { get; set; } = 10;

    public bool OtlpExport { get; set; } = true;
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
    public bool ExecutionReplay { get; set; } = true;
    public bool LlmDecisionLogging { get; set; } = true;
}
