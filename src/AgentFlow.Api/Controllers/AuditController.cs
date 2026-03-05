using AgentFlow.Application.Memory;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/audit")]
[Authorize]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditMemory _auditMemory;
    private readonly ITenantContextAccessor _tenantContext;

    public AuditController(IAuditMemory auditMemory, ITenantContextAccessor tenantContext)
    {
        _auditMemory = auditMemory;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAuditLogs(
        [FromRoute] string tenantId,
        [FromQuery] int limit = 100,
        [FromQuery] string? correlationId = null,
        [FromQuery] string? action = null,
        CancellationToken ct = default)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var boundedLimit = Math.Clamp(limit, 1, 500);

        IReadOnlyList<AuditEntry> logs = !string.IsNullOrWhiteSpace(correlationId)
            ? await _auditMemory.GetByCorrelationAsync(tenantId, correlationId, boundedLimit, ct)
            : await _auditMemory.GetRecentAsync(tenantId, boundedLimit, ct);

        if (!string.IsNullOrWhiteSpace(action))
        {
            logs = logs
                .Where(x => string.Equals(x.EventType.ToString(), action, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return Ok(logs.Select(l => new {
            l.Id,
            l.OccurredAt,
            Actor = l.UserId,
            Action = l.EventType.ToString(),
            Resource = l.AgentId,
            Severity = GetSeverity(l.EventType),
            l.CorrelationId,
            l.ExecutionId,
            l.EventJson,
            Ip = "internal" // In a real app, this would be captured in the entry
        }));
    }

    private static string GetSeverity(AuditEventType type) => type switch
    {
        AuditEventType.ExecutionFailed => "error",
        AuditEventType.SecurityViolation => "critical",
        AuditEventType.ToolFailed => "warning",
        AuditEventType.ExecutionCancelled => "warning",
        _ => "info"
    };
}
