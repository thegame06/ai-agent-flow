using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tenants/{tenantId}/campaign-call-playbooks")]
public sealed class CampaignCallPlaybooksController : ControllerBase
{
    private readonly ICampaignStore _store;
    private readonly ITenantContextAccessor _tenantContext;

    public CampaignCallPlaybooksController(ICampaignStore store, ITenantContextAccessor tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _store.GetPlaybooksAsync(tenantId, ct));
    }

    [HttpGet("{playbookId}")]
    public async Task<IActionResult> Get([FromRoute] string tenantId, [FromRoute] string playbookId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var playbook = await _store.GetPlaybookAsync(tenantId, playbookId, ct);
        return playbook is null ? NotFound() : Ok(playbook);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromRoute] string tenantId, [FromBody] UpsertCampaignCallPlaybookRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Name is required." });

        var now = DateTimeOffset.UtcNow;
        var playbook = await _store.UpsertPlaybookAsync(new CampaignCallPlaybookContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Purpose = request.Purpose?.Trim() ?? string.Empty,
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? "voice" : request.Channel.Trim(),
            OpeningScript = request.OpeningScript?.Trim() ?? string.Empty,
            QuestionsJson = request.QuestionsJson ?? "[]",
            AnswerSchemaJson = request.AnswerSchemaJson ?? "{}",
            CompletionRulesJson = request.CompletionRulesJson,
            FallbackRulesJson = request.FallbackRulesJson,
            HandoffRulesJson = request.HandoffRulesJson,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = _tenantContext.Current!.UserId
        }, ct);

        return CreatedAtAction(nameof(Get), new { tenantId, playbookId = playbook.Id }, playbook);
    }

    [HttpPut("{playbookId}")]
    public async Task<IActionResult> Update([FromRoute] string tenantId, [FromRoute] string playbookId, [FromBody] UpsertCampaignCallPlaybookRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var existing = await _store.GetPlaybookAsync(tenantId, playbookId, ct);
        if (existing is null) return NotFound();

        var updated = await _store.UpsertPlaybookAsync(existing with
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? existing.Name : request.Name.Trim(),
            Description = request.Description?.Trim() ?? existing.Description,
            Purpose = request.Purpose?.Trim() ?? existing.Purpose,
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? existing.Channel : request.Channel.Trim(),
            OpeningScript = request.OpeningScript?.Trim() ?? existing.OpeningScript,
            QuestionsJson = request.QuestionsJson ?? existing.QuestionsJson,
            AnswerSchemaJson = request.AnswerSchemaJson ?? existing.AnswerSchemaJson,
            CompletionRulesJson = request.CompletionRulesJson ?? existing.CompletionRulesJson,
            FallbackRulesJson = request.FallbackRulesJson ?? existing.FallbackRulesJson,
            HandoffRulesJson = request.HandoffRulesJson ?? existing.HandoffRulesJson,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = _tenantContext.Current!.UserId
        }, ct);

        return Ok(updated);
    }

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }
}

public sealed record UpsertCampaignCallPlaybookRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Purpose { get; init; }
    public string? Channel { get; init; }
    public string? OpeningScript { get; init; }
    public string? QuestionsJson { get; init; }
    public string? AnswerSchemaJson { get; init; }
    public string? CompletionRulesJson { get; init; }
    public string? FallbackRulesJson { get; init; }
    public string? HandoffRulesJson { get; init; }
}
