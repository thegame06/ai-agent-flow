using AgentFlow.Abstractions;
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

        // Map domain aggregates to DTOs for the UI
        return Ok(sets.Select(s => new {
            s.Id,
            s.Name,
            s.Description,
            s.Version,
            Status = s.IsPublished ? "Published" : "Draft",
            PolicyCount = s.Policies.Count,
            Severity = s.Policies.Count > 0 
                ? s.Policies.Max(p => p.Severity).ToString() 
                : "Info",
            s.CreatedAt
        }));
    }
}
