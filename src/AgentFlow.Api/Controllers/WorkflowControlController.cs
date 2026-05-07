using AgentFlow.Abstractions.Workflow;
using AgentFlow.Api.Workflow;
using AgentFlow.Application.Memory;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/control/workflows")]
[Authorize]
public sealed class WorkflowControlController : ControllerBase
{
    private readonly IWorkflowStudioStore _store;
    private readonly IAuditMemory _auditMemory;
    private readonly ITenantContextAccessor _tenantContext;

    public WorkflowControlController(IWorkflowStudioStore store, IAuditMemory auditMemory, ITenantContextAccessor tenantContext)
    {
        _store = store;
        _auditMemory = auditMemory;
        _tenantContext = tenantContext;
    }

    [HttpGet("executions")]
    public async Task<IActionResult> GetExecutions([FromRoute] string tenantId, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        if (!CanRead(tenantId)) return Forbid();
        return Ok(await _store.GetExecutionsAsync(tenantId, limit, ct));
    }

    [HttpGet("executions/{executionId}/steps")]
    public async Task<IActionResult> GetExecutionSteps([FromRoute] string tenantId, [FromRoute] string executionId, CancellationToken ct = default)
    {
        if (!CanRead(tenantId)) return Forbid();
        return Ok(await _store.GetStepLogsAsync(tenantId, executionId, ct));
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics([FromRoute] string tenantId, CancellationToken ct = default)
    {
        if (!CanRead(tenantId)) return Forbid();

        var rows = await _store.GetExecutionsAsync(tenantId, 1000, ct);
        var stepLogs = new List<WorkflowExecutionStepLogContract>();
        foreach (var execution in rows.Take(300))
            stepLogs.AddRange(await _store.GetStepLogsAsync(tenantId, execution.Id, ct));

        var byStatus = rows.GroupBy(x => x.Status).ToDictionary(g => g.Key.ToString(), g => g.Count());
        var total = rows.Count;
        var completed = rows.Count(x => x.Status == WorkflowExecutionStatus.Completed);
        var failed = rows.Count(x => x.Status == WorkflowExecutionStatus.Failed);
        var activityMetrics = BuildActivityMetrics(stepLogs);

        return Ok(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            total,
            byStatus,
            successRate = total == 0 ? 0 : Math.Round(completed / (double)total, 4),
            failureRate = total == 0 ? 0 : Math.Round(failed / (double)total, 4),
            avgLatencyMs = EstimateAverageLatencyMs(rows),
            activityMetrics
        });
    }

    [HttpGet("audit")]
    public async Task<IActionResult> GetAudit([FromRoute] string tenantId, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        if (!CanRead(tenantId)) return Forbid();

        var executions = await _store.GetExecutionsAsync(tenantId, limit, ct);
        var steps = new List<WorkflowExecutionStepLogContract>();

        foreach (var execution in executions.Take(100))
        {
            steps.AddRange(await _store.GetStepLogsAsync(tenantId, execution.Id, ct));
        }

        return Ok(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            executions,
            steps = steps.OrderByDescending(x => x.StartedAt)
        });
    }

    [HttpGet("audit/events")]
    public async Task<IActionResult> GetWorkflowAuditEvents([FromRoute] string tenantId, [FromQuery] int limit = 200, [FromQuery] string? correlationId = null, CancellationToken ct = default)
    {
        if (!CanRead(tenantId)) return Forbid();
        var bounded = Math.Clamp(limit, 1, 500);
        var rows = string.IsNullOrWhiteSpace(correlationId)
            ? await _auditMemory.GetRecentAsync(tenantId, bounded * 3, ct)
            : await _auditMemory.GetByCorrelationAsync(tenantId, correlationId, bounded * 3, ct);

        var workflowEvents = rows
            .Where(x => x.EventType == AuditEventType.ConnectOperation &&
                        x.EventJson.Contains("workflow.", StringComparison.OrdinalIgnoreCase))
            .Take(bounded)
            .Select(x => new
            {
                x.Id,
                x.ExecutionId,
                WorkflowId = x.AgentId,
                Actor = x.UserId,
                x.CorrelationId,
                x.OccurredAt,
                x.EventJson
            })
            .ToList();

        return Ok(workflowEvents);
    }

    private bool CanRead(string tenantId)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(AgentFlowPermissions.AuditRead) ||
                context.HasPermission(AgentFlowPermissions.AgentRead) ||
                context.IsPlatformAdmin);
    }

    private static double EstimateAverageLatencyMs(IReadOnlyList<WorkflowExecutionContract> rows)
    {
        if (rows.Count == 0) return 0;
        return Math.Round(rows.Average(x => (x.UpdatedAt - x.CreatedAt).TotalMilliseconds), 2);
    }

    private static IReadOnlyList<object> BuildActivityMetrics(IReadOnlyList<WorkflowExecutionStepLogContract> stepLogs)
    {
        return stepLogs
            .GroupBy(x => x.ActivityType)
            .Select(g =>
            {
                var total = g.Count();
                var succeeded = g.Count(x => x.Status == WorkflowExecutionStatus.Completed);
                var failed = g.Count(x => x.Status == WorkflowExecutionStatus.Failed);
                var avgMs = g
                    .Where(x => x.CompletedAt.HasValue)
                    .Select(x => (x.CompletedAt!.Value - x.StartedAt).TotalMilliseconds)
                    .DefaultIfEmpty(0)
                    .Average();

                // Approx retry signals: same execution + same activity executed multiple times.
                var retryLike = g.GroupBy(x => new { x.ExecutionId, x.ActivityType })
                    .Sum(x => Math.Max(0, x.Count() - 1));

                return new ActivityMetricDto
                {
                    ActivityType = g.Key,
                    Total = total,
                    Succeeded = succeeded,
                    Failed = failed,
                    SuccessRate = total == 0 ? 0 : Math.Round(succeeded / (double)total, 4),
                    AvgLatencyMs = Math.Round(avgMs, 2),
                    RetryLikeCount = retryLike
                };
            })
            .OrderByDescending(x => x.Total)
            .Cast<object>()
            .ToList();
    }

    private sealed record ActivityMetricDto
    {
        public string ActivityType { get; init; } = string.Empty;
        public int Total { get; init; }
        public int Succeeded { get; init; }
        public int Failed { get; init; }
        public double SuccessRate { get; init; }
        public double AvgLatencyMs { get; init; }
        public int RetryLikeCount { get; init; }
    }
}
