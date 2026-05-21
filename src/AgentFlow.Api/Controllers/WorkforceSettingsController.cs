using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/settings/workforce")]
[Authorize]
public sealed class WorkforceSettingsController : ControllerBase
{
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IMongoCollection<WorkforcePersonDocument> _people;
    private readonly IMongoCollection<WorkforceQueueDocument> _queues;

    public WorkforceSettingsController(ITenantContextAccessor tenantContext, IMongoDatabase database)
    {
        _tenantContext = tenantContext;
        _people = database.GetCollection<WorkforcePersonDocument>("workforce_people");
        _queues = database.GetCollection<WorkforceQueueDocument>("workforce_queues");
    }

    [HttpGet("people")]
    public async Task<IActionResult> GetPeople([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!IsAuthorized(tenantId)) return Forbid();
        var items = await _people.Find(x => x.TenantId == tenantId).SortBy(x => x.DisplayName).ToListAsync(ct);
        return Ok(items.Select(MapPerson));
    }

    [HttpPost("people")]
    public async Task<IActionResult> UpsertPerson([FromRoute] string tenantId, [FromBody] UpsertWorkforcePersonRequest body, CancellationToken ct)
    {
        if (!IsAuthorized(tenantId)) return Forbid();
        if (string.IsNullOrWhiteSpace(body.DisplayName))
            return BadRequest(new { message = "displayName es requerido." });
        if (string.Equals(body.MemberType, "virtual", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(body.AgentId))
            return BadRequest(new { message = "agentId es requerido para miembros virtuales." });

        var id = string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("N") : body.Id.Trim();
        var now = DateTimeOffset.UtcNow;
        var doc = new WorkforcePersonDocument
        {
            Id = id,
            TenantId = tenantId,
            MemberType = NormalizeMemberType(body.MemberType),
            DisplayName = body.DisplayName.Trim(),
            RoleTitle = body.RoleTitle?.Trim(),
            Email = body.Email?.Trim(),
            Phone = body.Phone?.Trim(),
            AgentId = body.AgentId?.Trim(),
            OperationalRole = body.OperationalRole?.Trim(),
            Active = body.Active,
            UpdatedAt = now
        };
        await _people.ReplaceOneAsync(x => x.TenantId == tenantId && x.Id == id, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return Ok(MapPerson(doc));
    }

    [HttpGet("queues")]
    public async Task<IActionResult> GetQueues([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!IsAuthorized(tenantId)) return Forbid();
        var items = await _queues.Find(x => x.TenantId == tenantId).SortBy(x => x.Name).ToListAsync(ct);
        return Ok(items.Select(MapQueue));
    }

    [HttpPost("queues")]
    public async Task<IActionResult> UpsertQueue([FromRoute] string tenantId, [FromBody] UpsertWorkforceQueueRequest body, CancellationToken ct)
    {
        if (!IsAuthorized(tenantId)) return Forbid();
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest(new { message = "name es requerido." });

        var id = string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("N") : body.Id.Trim();
        var now = DateTimeOffset.UtcNow;
        var members = (body.Members ?? new List<WorkforceQueueMemberDto>())
            .Where(m => !string.IsNullOrWhiteSpace(m.MemberId))
            .Select(m => new WorkforceQueueMemberDocument
            {
                MemberId = m.MemberId.Trim(),
                Weight = Math.Clamp(m.Weight, 1, 100),
                Capacity = Math.Clamp(m.Capacity, 1, 500),
                Active = m.Active
            }).ToList();

        var doc = new WorkforceQueueDocument
        {
            Id = id,
            TenantId = tenantId,
            Name = body.Name.Trim(),
            Description = body.Description?.Trim(),
            AssignmentStrategy = string.IsNullOrWhiteSpace(body.AssignmentStrategy) ? "least_load" : body.AssignmentStrategy.Trim(),
            Channels = body.Channels?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new(),
            Members = members,
            Active = body.Active,
            UpdatedAt = now
        };

        await _queues.ReplaceOneAsync(x => x.TenantId == tenantId && x.Id == id, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return Ok(MapQueue(doc));
    }

    [HttpGet("resolution")]
    public async Task<IActionResult> ResolveEscalationTarget([FromRoute] string tenantId, [FromQuery] string targetId, CancellationToken ct)
    {
        if (!IsAuthorized(tenantId)) return Forbid();
        if (string.IsNullOrWhiteSpace(targetId))
            return BadRequest(new { message = "targetId es requerido." });
        var queue = await _queues.Find(x => x.TenantId == tenantId && x.Id == targetId.Trim()).FirstOrDefaultAsync(ct);
        if (queue is null) return NotFound(new { message = "No existe el equipo/cola." });

        var memberIds = queue.Members.Where(m => m.Active).Select(m => m.MemberId).Distinct().ToList();
        var people = memberIds.Count == 0
            ? new List<WorkforcePersonDocument>()
            : await _people.Find(x => x.TenantId == tenantId && memberIds.Contains(x.Id)).ToListAsync(ct);

        return Ok(new
        {
            queue = MapQueue(queue),
            members = people.Select(MapPerson).ToList()
        });
    }

    private bool IsAuthorized(string tenantId)
    {
        var ctx = _tenantContext.Current!;
        return ctx.TenantId == tenantId || ctx.IsPlatformAdmin;
    }

    private static string NormalizeMemberType(string? memberType)
        => string.Equals(memberType, "virtual", StringComparison.OrdinalIgnoreCase) ? "virtual" : "human";

    private static WorkforcePersonDto MapPerson(WorkforcePersonDocument doc) => new()
    {
        Id = doc.Id,
        MemberType = doc.MemberType,
        DisplayName = doc.DisplayName,
        RoleTitle = doc.RoleTitle,
        Email = doc.Email,
        Phone = doc.Phone,
        AgentId = doc.AgentId,
        OperationalRole = doc.OperationalRole,
        Active = doc.Active,
        UpdatedAt = doc.UpdatedAt
    };

    private static WorkforceQueueDto MapQueue(WorkforceQueueDocument doc) => new()
    {
        Id = doc.Id,
        Name = doc.Name,
        Description = doc.Description,
        AssignmentStrategy = doc.AssignmentStrategy,
        Channels = doc.Channels,
        Members = doc.Members.Select(m => new WorkforceQueueMemberDto
        {
            MemberId = m.MemberId,
            Weight = m.Weight,
            Capacity = m.Capacity,
            Active = m.Active
        }).ToList(),
        Active = doc.Active,
        UpdatedAt = doc.UpdatedAt
    };
}

public sealed record UpsertWorkforcePersonRequest
{
    public string? Id { get; init; }
    public string MemberType { get; init; } = "human";
    public string DisplayName { get; init; } = string.Empty;
    public string? RoleTitle { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? AgentId { get; init; }
    public string? OperationalRole { get; init; }
    public bool Active { get; init; } = true;
}

public sealed record UpsertWorkforceQueueRequest
{
    public string? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string AssignmentStrategy { get; init; } = "least_load";
    public List<string>? Channels { get; init; }
    public List<WorkforceQueueMemberDto>? Members { get; init; }
    public bool Active { get; init; } = true;
}

public sealed record WorkforcePersonDto
{
    public string Id { get; init; } = string.Empty;
    public string MemberType { get; init; } = "human";
    public string DisplayName { get; init; } = string.Empty;
    public string? RoleTitle { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? AgentId { get; init; }
    public string? OperationalRole { get; init; }
    public bool Active { get; init; } = true;
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record WorkforceQueueDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string AssignmentStrategy { get; init; } = "least_load";
    public List<string> Channels { get; init; } = new();
    public List<WorkforceQueueMemberDto> Members { get; init; } = new();
    public bool Active { get; init; } = true;
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record WorkforceQueueMemberDto
{
    public string MemberId { get; init; } = string.Empty;
    public int Weight { get; init; } = 1;
    public int Capacity { get; init; } = 10;
    public bool Active { get; init; } = true;
}

public sealed record WorkforcePersonDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    [BsonElement("tenant_id")]
    public string TenantId { get; set; } = string.Empty;
    [BsonElement("member_type")]
    public string MemberType { get; set; } = "human";
    [BsonElement("display_name")]
    public string DisplayName { get; set; } = string.Empty;
    [BsonElement("role_title")]
    public string? RoleTitle { get; set; }
    [BsonElement("email")]
    public string? Email { get; set; }
    [BsonElement("phone")]
    public string? Phone { get; set; }
    [BsonElement("agent_id")]
    public string? AgentId { get; set; }
    [BsonElement("operational_role")]
    public string? OperationalRole { get; set; }
    [BsonElement("active")]
    public bool Active { get; set; } = true;
    [BsonElement("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record WorkforceQueueDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    [BsonElement("tenant_id")]
    public string TenantId { get; set; } = string.Empty;
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;
    [BsonElement("description")]
    public string? Description { get; set; }
    [BsonElement("assignment_strategy")]
    public string AssignmentStrategy { get; set; } = "least_load";
    [BsonElement("channels")]
    public List<string> Channels { get; set; } = new();
    [BsonElement("members")]
    public List<WorkforceQueueMemberDocument> Members { get; set; } = new();
    [BsonElement("active")]
    public bool Active { get; set; } = true;
    [BsonElement("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record WorkforceQueueMemberDocument
{
    [BsonElement("member_id")]
    public string MemberId { get; set; } = string.Empty;
    [BsonElement("weight")]
    public int Weight { get; set; } = 1;
    [BsonElement("capacity")]
    public int Capacity { get; set; } = 10;
    [BsonElement("active")]
    public bool Active { get; set; } = true;
}
