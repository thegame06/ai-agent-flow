using AgentFlow.Abstractions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/checkpoints")]
[Authorize]
public sealed class CheckpointsController : ControllerBase
{
    private readonly ICheckpointStore _checkpointStore;
    private readonly IAgentExecutor _executor;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ILogger<CheckpointsController> _logger;

    public CheckpointsController(
        ICheckpointStore checkpointStore,
        IAgentExecutor executor,
        ITenantContextAccessor tenantContext,
        ILogger<CheckpointsController> logger)
    {
        _checkpointStore = checkpointStore;
        _executor = executor;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetPending(string tenantId, [FromQuery] int limit = 50)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        try
        {
            var pending = await _checkpointStore.GetPendingAsync(tenantId, limit);
            return Ok(pending);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load pending checkpoints for tenant {TenantId}", tenantId);
            return StatusCode(500, new { message = "Failed to load checkpoints.", error = ex.Message });
        }
    }

    [HttpGet("{executionId}")]
    public async Task<IActionResult> GetByExecution(string tenantId, string executionId)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var checkpoint = await _checkpointStore.GetAsync(executionId, tenantId);
        if (checkpoint == null) return NotFound();

        return Ok(checkpoint);
    }

    [HttpPost("{executionId}/decide")]
    public async Task<IActionResult> Decide(string tenantId, string executionId, [FromBody] CheckpointDecisionRequest body)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var decision = new CheckpointDecision
        {
            CheckpointId = body.CheckpointId,
            Approved = body.Approved,
            Feedback = body.Feedback,
            ModifiedInputJson = body.ModifiedInputJson,
            ApprovedBy = context.UserId
        };

        var result = await _executor.ResumeAsync(executionId, tenantId, decision);
        
        return Ok(result);
    }
}

public sealed record CheckpointDecisionRequest
{
    public required string CheckpointId { get; init; }
    public required bool Approved { get; init; }
    public string? Feedback { get; init; }
    public string? ModifiedInputJson { get; init; }
}
