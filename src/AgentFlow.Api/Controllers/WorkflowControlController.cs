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
    public async Task<IActionResult> GetMetrics(
        [FromRoute] string tenantId,
        [FromQuery] string? window = null,
        CancellationToken ct = default)
    {
        if (!CanRead(tenantId)) return Forbid();

        var windowStart = ResolveWindowStart(window);
        var rows = await _store.GetExecutionsAsync(tenantId, 1000, ct);
        if (windowStart.HasValue)
        {
            rows = rows
                .Where(x => x.CreatedAt >= windowStart.Value)
                .ToList();
        }
        var stepLogs = new List<WorkflowExecutionStepLogContract>();
        foreach (var execution in rows.Take(300))
            stepLogs.AddRange(await _store.GetStepLogsAsync(tenantId, execution.Id, ct));

        var byStatus = rows.GroupBy(x => x.Status).ToDictionary(g => g.Key.ToString(), g => g.Count());
        var total = rows.Count;
        var completed = rows.Count(x => x.Status == WorkflowExecutionStatus.Completed);
        var failed = rows.Count(x => x.Status == WorkflowExecutionStatus.Failed);
        var activityMetrics = BuildActivityMetrics(stepLogs);
        var continuitySignals = await BuildContinuitySignalsAsync(_auditMemory, tenantId, windowStart, ct);

        return Ok(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            total,
            byStatus,
            successRate = total == 0 ? 0 : Math.Round(completed / (double)total, 4),
            failureRate = total == 0 ? 0 : Math.Round(failed / (double)total, 4),
            avgLatencyMs = EstimateAverageLatencyMs(rows),
            window = NormalizeMetricsWindow(window),
            windowStart,
            activityMetrics,
            continuitySignals
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

    [HttpGet("metrics/provider-resolution-series")]
    public async Task<IActionResult> GetProviderResolutionSeries(
        [FromRoute] string tenantId,
        [FromQuery] string? window = null,
        CancellationToken ct = default)
    {
        if (!CanRead(tenantId)) return Forbid();

        var normalizedWindow = NormalizeMetricsWindow(window);
        var windowStart = ResolveWindowStart(normalizedWindow) ?? DateTimeOffset.UtcNow.AddHours(-24);
        var rows = await _auditMemory.GetRecentAsync(tenantId, 5000, ct);
        var scoped = rows
            .Where(x => x.OccurredAt >= windowStart)
            .OrderBy(x => x.OccurredAt)
            .ToList();

        var granularity = normalizedWindow == "24h" ? "hour" : "day";
        var buckets = BuildProviderResolutionSeries(scoped, granularity);

        return Ok(new
        {
            window = normalizedWindow,
            windowStart,
            granularity,
            buckets
        });
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

    private static async Task<object> BuildContinuitySignalsAsync(
        IAuditMemory auditMemory,
        string tenantId,
        DateTimeOffset? windowStart,
        CancellationToken ct)
    {
        var rows = await auditMemory.GetRecentAsync(tenantId, 3000, ct);
        if (windowStart.HasValue)
        {
            rows = rows
                .Where(x => x.OccurredAt >= windowStart.Value)
                .ToList();
        }
        var actions = ExtractWorkflowActions(rows);

        var loopDetected = actions.Count(x => string.Equals(x, "fallback.loop_detected", StringComparison.OrdinalIgnoreCase));
        var repromptBlocked = actions.Count(x => string.Equals(x, "conversation.guardrail.slot_reprompt_blocked", StringComparison.OrdinalIgnoreCase));
        var contextWiring = actions.Count(x => string.Equals(x, "workflow.ai_agent.context_wiring", StringComparison.OrdinalIgnoreCase));
        var escalated = actions.Count(x => string.Equals(x, "fallback.escalated_human", StringComparison.OrdinalIgnoreCase));
        var continuityWindow = Math.Max(1, contextWiring);

        return new
        {
            windowSize = rows.Count,
            loopDetected,
            repromptBlocked,
            contextWiring,
            escalatedHuman = escalated,
            providerResolutionByRole = BuildProviderResolutionSignals(rows),
            rates = new
            {
                loopPerContext = Math.Round(loopDetected / (double)continuityWindow, 4),
                escalationPerContext = Math.Round(escalated / (double)continuityWindow, 4),
                repromptBlockedPerContext = Math.Round(repromptBlocked / (double)continuityWindow, 4)
            }
        };
    }

    private static string NormalizeMetricsWindow(string? window)
    {
        var normalized = window?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "24h" => "24h",
            "7d" => "7d",
            "30d" => "30d",
            _ => "24h"
        };
    }

    private static DateTimeOffset? ResolveWindowStart(string? window)
    {
        var normalized = NormalizeMetricsWindow(window);
        var now = DateTimeOffset.UtcNow;
        return normalized switch
        {
            "24h" => now.AddHours(-24),
            "7d" => now.AddDays(-7),
            "30d" => now.AddDays(-30),
            _ => now.AddHours(-24)
        };
    }

    private static List<string> ExtractWorkflowActions(IReadOnlyList<AuditEntry> rows)
    {
        var result = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.EventJson))
                continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(row.EventJson);
                if (!doc.RootElement.TryGetProperty("action", out var actionElement))
                    continue;
                var action = actionElement.GetString();
                if (!string.IsNullOrWhiteSpace(action))
                    result.Add(action);
            }
            catch
            {
                // ignore malformed audit payload
            }
        }

        return result;
    }

    private static object BuildProviderResolutionSignals(IReadOnlyList<AuditEntry> rows)
    {
        var buckets = new Dictionary<string, ProviderResolutionBucket>(StringComparer.OrdinalIgnoreCase)
        {
            ["stt"] = new(),
            ["tts"] = new(),
            ["callControl"] = new(),
            ["reasoning"] = new()
        };

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.EventJson))
                continue;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(row.EventJson);
                var root = doc.RootElement;
                var action = root.TryGetProperty("action", out var actionEl)
                    ? actionEl.GetString()
                    : null;

                if (string.Equals(action, "voice.stt.provider.selected", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyDecision(buckets["stt"], ReadNestedString(root, "details", "decision"));
                    TrackProvider(buckets["stt"], ReadNestedString(root, "details", "provider"));
                    continue;
                }

                if (string.Equals(action, "voice.tts.provider.selected", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyDecision(buckets["tts"], ReadNestedString(root, "details", "decision"));
                    TrackProvider(buckets["tts"], ReadNestedString(root, "details", "provider"));
                    continue;
                }

                if (string.Equals(action, "voice.playback.delivered", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyDecision(buckets["callControl"], ReadNestedString(root, "details", "decision"));
                    TrackProvider(buckets["callControl"], ReadNestedString(root, "details", "provider"));
                    continue;
                }

                if (string.Equals(action, "voice.playback.failed", StringComparison.OrdinalIgnoreCase))
                {
                    buckets["callControl"].Failed++;
                    continue;
                }

                if (row.EventType == AuditEventType.ExecutionStarted &&
                    root.TryGetProperty("providerRouting", out var providerRouting) &&
                    providerRouting.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (providerRouting.TryGetProperty("preferredProviders", out var preferredProviders) &&
                        preferredProviders.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        TrackProvider(buckets["callControl"], ReadProperty(preferredProviders, "callControl"));
                        TrackProvider(buckets["stt"], ReadProperty(preferredProviders, "stt"));
                        TrackProvider(buckets["tts"], ReadProperty(preferredProviders, "tts"));
                        TrackProvider(buckets["reasoning"], ReadProperty(preferredProviders, "reasoning"));
                    }
                }
            }
            catch
            {
                // ignore malformed audit payload
            }
        }

        return new
        {
            stt = ToRoleSignal(buckets["stt"]),
            tts = ToRoleSignal(buckets["tts"]),
            callControl = ToRoleSignal(buckets["callControl"]),
            reasoning = ToRoleSignal(buckets["reasoning"])
        };
    }

    private static void ApplyDecision(ProviderResolutionBucket bucket, string? decision)
    {
        if (string.IsNullOrWhiteSpace(decision))
            return;

        if (decision.Equals("primary", StringComparison.OrdinalIgnoreCase))
            bucket.Primary++;
        else if (decision.Equals("fallback", StringComparison.OrdinalIgnoreCase))
            bucket.Fallback++;
        else if (decision.Equals("failed", StringComparison.OrdinalIgnoreCase))
            bucket.Failed++;
    }

    private static void TrackProvider(ProviderResolutionBucket bucket, string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return;
        if (bucket.Providers.Add(provider!))
            bucket.ProviderList.Add(provider!);
    }

    private static string? ReadNestedString(System.Text.Json.JsonElement root, string parent, string child)
    {
        if (!root.TryGetProperty(parent, out var parentEl) || parentEl.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;
        return ReadProperty(parentEl, child);
    }

    private static string? ReadProperty(System.Text.Json.JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var valueEl) || valueEl.ValueKind != System.Text.Json.JsonValueKind.String)
            return null;
        var value = valueEl.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static object ToRoleSignal(ProviderResolutionBucket bucket) => new
    {
        primary = bucket.Primary,
        fallback = bucket.Fallback,
        failed = bucket.Failed,
        providers = bucket.ProviderList.Take(5).ToArray()
    };

    private static IReadOnlyList<object> BuildProviderResolutionSeries(IReadOnlyList<AuditEntry> rows, string granularity)
    {
        var roleCountersByBucket = new SortedDictionary<DateTimeOffset, Dictionary<string, ProviderResolutionBucket>>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.EventJson))
                continue;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(row.EventJson);
                var root = doc.RootElement;
                var action = root.TryGetProperty("action", out var actionEl)
                    ? actionEl.GetString()
                    : null;

                var role = action switch
                {
                    "voice.stt.provider.selected" => "stt",
                    "voice.tts.provider.selected" => "tts",
                    "voice.playback.delivered" => "callControl",
                    "voice.playback.failed" => "callControl",
                    _ => null
                };

                if (role is null)
                    continue;

                var bucketTs = granularity.Equals("hour", StringComparison.OrdinalIgnoreCase)
                    ? new DateTimeOffset(row.OccurredAt.Year, row.OccurredAt.Month, row.OccurredAt.Day, row.OccurredAt.Hour, 0, 0, TimeSpan.Zero)
                    : new DateTimeOffset(row.OccurredAt.Year, row.OccurredAt.Month, row.OccurredAt.Day, 0, 0, 0, TimeSpan.Zero);

                if (!roleCountersByBucket.TryGetValue(bucketTs, out var roleMap))
                {
                    roleMap = new Dictionary<string, ProviderResolutionBucket>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["stt"] = new(),
                        ["tts"] = new(),
                        ["callControl"] = new(),
                        ["reasoning"] = new()
                    };
                    roleCountersByBucket[bucketTs] = roleMap;
                }

                if (action == "voice.playback.failed")
                {
                    roleMap[role].Failed++;
                    continue;
                }

                var decision = ReadNestedString(root, "details", "decision");
                ApplyDecision(roleMap[role], decision);
                TrackProvider(roleMap[role], ReadNestedString(root, "details", "provider"));
            }
            catch
            {
                // ignore malformed audit payload
            }
        }

        return roleCountersByBucket
            .Select(kvp => new
            {
                bucket = kvp.Key,
                roles = new
                {
                    stt = ToRoleSignal(kvp.Value["stt"]),
                    tts = ToRoleSignal(kvp.Value["tts"]),
                    callControl = ToRoleSignal(kvp.Value["callControl"]),
                    reasoning = ToRoleSignal(kvp.Value["reasoning"])
                }
            })
            .Cast<object>()
            .ToList();
    }

    private sealed class ProviderResolutionBucket
    {
        public int Primary { get; set; }
        public int Fallback { get; set; }
        public int Failed { get; set; }
        public HashSet<string> Providers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ProviderList { get; } = new();
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
