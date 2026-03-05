using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/intent-routing")]
[Authorize]
public sealed class IntentRoutingController : ControllerBase
{
    private readonly IIntentRoutingStore _store;
    private readonly ITenantContextAccessor _tenantContext;

    public IntentRoutingController(IIntentRoutingStore store, ITenantContextAccessor tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var rules = await _store.GetRulesAsync(tenantId, ct);
        return Ok(rules);
    }

    [HttpPost("rules")]
    public async Task<IActionResult> UpsertRule([FromRoute] string tenantId, [FromBody] UpsertIntentRuleRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var saved = await _store.UpsertRuleAsync(new IntentRoutingRule
        {
            Id = string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("N") : body.Id,
            TenantId = tenantId,
            IntentKey = body.IntentKey,
            SourceAgentId = body.SourceAgentId,
            TargetAgentId = body.TargetAgentId,
            Priority = body.Priority,
            Enabled = body.Enabled,
            Channel = body.Channel,
            ConditionsJson = body.ConditionsJson,
            HandoffPolicyJson = body.HandoffPolicyJson,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        return Ok(saved);
    }

    [HttpPut("rules/{ruleId}")]
    public async Task<IActionResult> UpdateRule([FromRoute] string tenantId, [FromRoute] string ruleId, [FromBody] UpsertIntentRuleRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var existing = await _store.GetRuleByIdAsync(tenantId, ruleId, ct);
        if (existing is null) return NotFound();

        var saved = await _store.UpsertRuleAsync(existing with
        {
            IntentKey = body.IntentKey,
            SourceAgentId = body.SourceAgentId,
            TargetAgentId = body.TargetAgentId,
            Priority = body.Priority,
            Enabled = body.Enabled,
            Channel = body.Channel,
            ConditionsJson = body.ConditionsJson,
            HandoffPolicyJson = body.HandoffPolicyJson,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        return Ok(saved);
    }

    [HttpPatch("rules/{ruleId}/enable")]
    public async Task<IActionResult> SetRuleEnabled([FromRoute] string tenantId, [FromRoute] string ruleId, [FromBody] SetEnabledRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var ok = await _store.SetRuleEnabledAsync(tenantId, ruleId, body.Enabled, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate([FromRoute] string tenantId, [FromBody] SimulateIntentRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var result = await _store.SimulateAsync(tenantId, body.SourceAgentId, body.Intent, body.Channel, ct);
        return Ok(result);
    }

    [HttpGet("agents")]
    public async Task<IActionResult> GetAgents([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var agents = await _store.GetAgentsAsync(tenantId, ct);
        return Ok(agents);
    }

    [HttpPatch("agents/{agentId}")]
    public async Task<IActionResult> UpsertAgent([FromRoute] string tenantId, [FromRoute] string agentId, [FromBody] UpsertAgentRequest body, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        if (string.Equals(body.AgentType, "subagent", StringComparison.OrdinalIgnoreCase) && body.ExternalReplyAllowed)
            return BadRequest(new { error = "subagents cannot have externalReplyAllowed=true in production" });

        var saved = await _store.UpsertAgentAsync(new AgentRegistryEntry
        {
            Id = string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("N") : body.Id,
            TenantId = tenantId,
            AgentId = agentId,
            AgentType = body.AgentType,
            Enabled = body.Enabled,
            TestModeAllowed = body.TestModeAllowed,
            ExternalReplyAllowed = body.ExternalReplyAllowed,
            Capabilities = body.Capabilities ?? Array.Empty<string>(),
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        return Ok(saved);
    }
}

public sealed record UpsertIntentRuleRequest
{
    public string? Id { get; init; }
    public required string IntentKey { get; init; }
    public required string SourceAgentId { get; init; }
    public required string TargetAgentId { get; init; }
    public required int Priority { get; init; }
    public bool Enabled { get; init; } = true;
    public string? Channel { get; init; }
    public string? ConditionsJson { get; init; }
    public string? HandoffPolicyJson { get; init; }
}

public sealed record SetEnabledRequest(bool Enabled);

public sealed record SimulateIntentRequest
{
    public required string SourceAgentId { get; init; }
    public required string Intent { get; init; }
    public string? Channel { get; init; }
}

public sealed record UpsertAgentRequest
{
    public string? Id { get; init; }
    public required string AgentType { get; init; }
    public required bool Enabled { get; init; }
    public required bool TestModeAllowed { get; init; }
    public required bool ExternalReplyAllowed { get; init; }
    public IReadOnlyList<string>? Capabilities { get; init; }
}
