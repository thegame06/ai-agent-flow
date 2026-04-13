using AgentFlow.Abstractions;
using AgentFlow.Evaluation;
using AgentFlow.Observability;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/evaluations")]
[Authorize]
public sealed class EvaluationsController : ControllerBase
{
    private readonly IEvaluationResultStore _evaluationStore;
    private readonly ITenantContextAccessor _tenantContext;

    public EvaluationsController(IEvaluationResultStore evaluationStore, ITenantContextAccessor tenantContext)
    {
        _evaluationStore = evaluationStore;
        _tenantContext = tenantContext;
    }

    [HttpGet("executions/{executionId}")]
    public async Task<IActionResult> GetByExecutionId(string tenantId, string executionId)
    {
        var sw = Stopwatch.StartNew();
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var result = await _evaluationStore.GetByExecutionIdAsync(executionId, tenantId);
        if (result == null) return NotFound();
        sw.Stop();

        AgentFlowTelemetry.ApiEndpointLatency.Record(sw.Elapsed.TotalMilliseconds, new TagList
        {
            { "controller", "EvaluationsController" },
            { "action", "GetByExecutionId" }
        });

        return Ok(result);
    }

    [HttpGet("agents/{agentKey}")]
    public async Task<IActionResult> GetByAgent(string tenantId, string agentKey, [FromQuery] int limit = 50)
    {
        var sw = Stopwatch.StartNew();
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var results = await _evaluationStore.GetByAgentAsync(agentKey, tenantId, limit);
        sw.Stop();
        AgentFlowTelemetry.ApiEndpointLatency.Record(sw.Elapsed.TotalMilliseconds, new TagList
        {
            { "controller", "EvaluationsController" },
            { "action", "GetByAgent" }
        });
        return Ok(results);
    }

    [HttpGet("agents/{agentKey}/summary")]
    public async Task<IActionResult> GetAgentSummary(string tenantId, string agentKey, [FromQuery] string version)
    {
        var sw = Stopwatch.StartNew();
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var summary = await _evaluationStore.GetAgentSummaryAsync(agentKey, version, tenantId);
        sw.Stop();
        AgentFlowTelemetry.ApiEndpointLatency.Record(sw.Elapsed.TotalMilliseconds, new TagList
        {
            { "controller", "EvaluationsController" },
            { "action", "GetAgentSummary" }
        });
        return Ok(summary);
    }

    [HttpGet("pending-review")]
    public async Task<IActionResult> GetPendingReview(string tenantId, [FromQuery] int limit = 50)
    {
        var sw = Stopwatch.StartNew();
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var results = await _evaluationStore.GetPendingHumanReviewAsync(tenantId, limit);
        sw.Stop();
        AgentFlowTelemetry.ApiEndpointLatency.Record(sw.Elapsed.TotalMilliseconds, new TagList
        {
            { "controller", "EvaluationsController" },
            { "action", "GetPendingReview" }
        });
        return Ok(results);
    }
}
