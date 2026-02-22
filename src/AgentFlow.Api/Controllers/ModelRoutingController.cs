using AgentFlow.Abstractions;
using AgentFlow.ModelRouting;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/model-routing")]
[Authorize]
public sealed class ModelRoutingController : ControllerBase
{
    private readonly IModelRegistry _registry;
    private readonly ITenantContextAccessor _tenantContext;

    public ModelRoutingController(IModelRegistry registry, ITenantContextAccessor tenantContext)
    {
        _registry = registry;
        _tenantContext = tenantContext;
    }

    [HttpGet("models")]
    public IActionResult GetAvailableModels()
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var models = _registry.GetAvailableModelIds()
            .Select(id => _registry.GetProvider(id))
            .Where(p => p != null)
            .Select(p => new
            {
                p!.ModelId,
                p.ProviderId,
                p.Metadata.DisplayName,
                p.Metadata.CostPer1KTokens,
                p.Metadata.MaxContextTokens,
                p.Metadata.Tier,
                Status = "Active" // Default for registered models
            });

        return Ok(models);
    }

    [HttpGet("models/healthy")]
    public async Task<IActionResult> GetHealthyModels()
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var models = await _registry.GetHealthyModelIdsAsync();
        return Ok(models);
    }
}
