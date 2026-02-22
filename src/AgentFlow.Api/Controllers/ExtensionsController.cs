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

    /// <summary>
    /// List all registered tools in the current process.
    /// </summary>
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
            t.InputSchemaJson,
            t.OutputSchemaJson
        });

        return Ok(tools);
    }

    /// <summary>
    /// Standalone invocation of a tool for debugging (LangChain/Elsa style).
    /// </summary>
    [HttpPost("tools/{name}/invoke")]
    public async Task<IActionResult> InvokeToolAsync(
        string name,
        [FromBody] ToolInvokeRequest body,
        CancellationToken ct)
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

    /// <summary>
    /// List all extensions and their health status.
    /// </summary>
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalogAsync(CancellationToken ct)
    {
        var extensions = _registry.GetAllExtensions();
        var results = new List<object>();

        foreach (var ext in extensions)
        {
            var health = await ext.CheckHealthAsync(ct);
            results.Add(new
            {
                ext.ExtensionId,
                ext.Version,
                Health = health.IsHealthy ? "Healthy" : "Unhealthy",
                health.Message,
                health.CheckedAt
            });
        }

        return Ok(results);
    }
}

public sealed record ToolInvokeRequest
{
    public required string InputJson { get; init; }
}
