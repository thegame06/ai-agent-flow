using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/policies")]
[Authorize]
public sealed class PoliciesController : ControllerBase
{
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IPolicyRepository _repository;

    public PoliciesController(ITenantContextAccessor tenantContext, IPolicyRepository repository)
    {
        _tenantContext = tenantContext;
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetPolicies([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var sets = await _repository.GetByTenantAsync(tenantId, ct: ct);

        return Ok(sets.Select(MapPolicySet));
    }

    [HttpGet("{policySetId}")]
    public async Task<IActionResult> GetPolicySet([FromRoute] string tenantId, [FromRoute] string policySetId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var set = await _repository.GetByIdAsync(policySetId, tenantId, ct);
        if (set is null) return NotFound();

        return Ok(MapPolicySet(set));
    }

    [HttpPost]
    public async Task<IActionResult> CreatePolicySet([FromRoute] string tenantId, [FromBody] CreatePolicySetRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var created = PolicySetDefinition.Create(
            tenantId,
            request.Name,
            request.Description ?? string.Empty,
            context.UserId);

        if (!created.IsSuccess)
            return BadRequest(new { error = created.Error?.Message ?? "Failed to create policy set" });

        var addResult = await _repository.AddAsync(created.Value!, ct);
        if (!addResult.IsSuccess)
            return BadRequest(new { error = addResult.Error?.Message ?? "Failed to persist policy set" });

        return Ok(MapPolicySet(created.Value!));
    }

    [HttpPost("{policySetId}/publish")]
    public async Task<IActionResult> PublishPolicySet([FromRoute] string tenantId, [FromRoute] string policySetId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var set = await _repository.GetByIdAsync(policySetId, tenantId, ct);
        if (set is null) return NotFound();

        var result = set.Publish(context.UserId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error?.Message ?? "Failed to publish policy set" });

        var updateResult = await _repository.UpdateAsync(set, ct);
        if (!updateResult.IsSuccess)
            return BadRequest(new { error = updateResult.Error?.Message ?? "Failed to persist publish operation" });

        return Ok(MapPolicySet(set));
    }

    private static object MapPolicySet(PolicySetDefinition s) => new
    {
        s.Id,
        s.Name,
        s.Description,
        s.Version,
        Status = s.IsPublished ? "Published" : "Draft",
        PolicyCount = s.Policies.Count,
        Severity = s.Policies.Count > 0
            ? s.Policies.Max(p => p.Severity).ToString()
            : "Info",
        s.CreatedAt,
        s.UpdatedAt
    };
}

public sealed class CreatePolicySetRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
