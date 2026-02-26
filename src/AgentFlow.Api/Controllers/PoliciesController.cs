using AgentFlow.Abstractions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/policies")]
[Authorize]
public sealed class PoliciesController : ControllerBase
{
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IMongoCollection<PolicySetStoreDocument> _collection;

    public PoliciesController(ITenantContextAccessor tenantContext, IMongoDatabase database)
    {
        _tenantContext = tenantContext;
        _collection = database.GetCollection<PolicySetStoreDocument>("policy_sets_v2");
    }

    [HttpGet]
    public async Task<IActionResult> GetPolicies([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var sets = await _collection.Find(x => x.TenantId == tenantId).ToListAsync(ct);
        return Ok(sets.Select(MapPolicySet));
    }

    [HttpGet("{policySetId}")]
    public async Task<IActionResult> GetPolicySet([FromRoute] string tenantId, [FromRoute] string policySetId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var set = await _collection.Find(x => x.TenantId == tenantId && x.Id == policySetId).FirstOrDefaultAsync(ct);
        if (set is null) return NotFound();

        return Ok(MapPolicySet(set));
    }

    [HttpPost]
    public async Task<IActionResult> CreatePolicySet([FromRoute] string tenantId, [FromBody] CreatePolicySetRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Policy set name is required" });

        var doc = new PolicySetStoreDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Version = "1.0.0",
            IsPublished = false,
            Policies = new List<PolicyDefinition>(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = context.UserId,
            UpdatedBy = context.UserId
        };

        await _collection.InsertOneAsync(doc, cancellationToken: ct);
        return Ok(MapPolicySet(doc));
    }

    [HttpPut("{policySetId}/policies")]
    public async Task<IActionResult> UpdatePolicyRules([FromRoute] string tenantId, [FromRoute] string policySetId, [FromBody] UpdatePolicyRulesRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var set = await _collection.Find(x => x.TenantId == tenantId && x.Id == policySetId).FirstOrDefaultAsync(ct);
        if (set is null) return NotFound();
        if (set.IsPublished) return BadRequest(new { error = "Cannot update a published policy set. Create a new version." });

        set.Policies = request.Policies.Select(p => new PolicyDefinition
        {
            PolicyId = p.PolicyId,
            Description = p.Description,
            AppliesAt = p.AppliesAt,
            PolicyType = p.PolicyType,
            Action = p.Action,
            Severity = p.Severity,
            IsEnabled = p.IsEnabled,
            Config = p.Config ?? new Dictionary<string, string>(),
            TargetSegments = p.TargetSegments ?? []
        }).ToList();

        set.UpdatedAt = DateTimeOffset.UtcNow;
        set.UpdatedBy = context.UserId;

        await _collection.ReplaceOneAsync(x => x.Id == set.Id && x.TenantId == tenantId, set, cancellationToken: ct);
        return Ok(MapPolicySet(set));
    }

    [HttpPost("{policySetId}/publish")]
    public async Task<IActionResult> PublishPolicySet([FromRoute] string tenantId, [FromRoute] string policySetId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var set = await _collection.Find(x => x.TenantId == tenantId && x.Id == policySetId).FirstOrDefaultAsync(ct);
        if (set is null) return NotFound();
        if (set.IsPublished) return BadRequest(new { error = "Policy set is already published" });

        set.IsPublished = true;
        set.UpdatedAt = DateTimeOffset.UtcNow;
        set.UpdatedBy = context.UserId;

        await _collection.ReplaceOneAsync(x => x.Id == set.Id && x.TenantId == tenantId, set, cancellationToken: ct);
        return Ok(MapPolicySet(set));
    }

    private static object MapPolicySet(PolicySetStoreDocument s) => new
    {
        s.Id,
        s.Name,
        s.Description,
        s.Version,
        Status = s.IsPublished ? "Published" : "Draft",
        PolicyCount = s.Policies.Count,
        Severity = s.Policies.Count > 0 ? s.Policies.Max(p => p.Severity).ToString() : "Info",
        Policies = s.Policies,
        s.CreatedAt,
        s.UpdatedAt
    };

    private sealed class PolicySetStoreDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0.0";
        public bool IsPublished { get; set; }
        public List<PolicyDefinition> Policies { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string UpdatedBy { get; set; } = string.Empty;
    }
}

public sealed class CreatePolicySetRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class UpdatePolicyRulesRequest
{
    public List<UpdatePolicyRuleItem> Policies { get; set; } = new();
}

public sealed class UpdatePolicyRuleItem
{
    public string PolicyId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PolicyCheckpoint AppliesAt { get; set; } = PolicyCheckpoint.PreAgent;
    public string PolicyType { get; set; } = "Custom";
    public PolicyAction Action { get; set; } = PolicyAction.Allow;
    public PolicySeverity Severity { get; set; } = PolicySeverity.Info;
    public bool IsEnabled { get; set; } = true;
    public Dictionary<string, string>? Config { get; set; }
    public List<string>? TargetSegments { get; set; }
}
