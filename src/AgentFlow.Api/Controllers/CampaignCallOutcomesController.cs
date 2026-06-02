using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tenants/{tenantId}")]
public sealed class CampaignCallOutcomesController : ControllerBase
{
    private readonly ICampaignStore _store;
    private readonly ITenantContextAccessor _tenantContext;

    public CampaignCallOutcomesController(ICampaignStore store, ITenantContextAccessor tenantContext)
    {
        _store = store;
        _tenantContext = tenantContext;
    }

    [HttpGet("campaign-runs/{runId}/call-outcomes")]
    public async Task<IActionResult> ListByRun([FromRoute] string tenantId, [FromRoute] string runId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        return Ok(await _store.GetCallOutcomesByRunAsync(tenantId, runId, ct));
    }

    [HttpGet("campaign-contact-executions/{contactExecutionId}/call-outcome")]
    public async Task<IActionResult> GetByContact([FromRoute] string tenantId, [FromRoute] string contactExecutionId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var outcome = await _store.GetCallOutcomeByContactAsync(tenantId, contactExecutionId, ct);
        return outcome is null ? NotFound() : Ok(outcome);
    }

    [HttpGet("campaign-call-outcomes/{outcomeId}")]
    public async Task<IActionResult> Get([FromRoute] string tenantId, [FromRoute] string outcomeId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();
        var outcome = await _store.GetCallOutcomeAsync(tenantId, outcomeId, ct);
        return outcome is null ? NotFound() : Ok(outcome);
    }

    [HttpPost("campaign-contact-executions/{contactExecutionId}/call-outcome")]
    public async Task<IActionResult> Create([FromRoute] string tenantId, [FromRoute] string contactExecutionId, [FromBody] UpsertCampaignCallOutcomeRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var contacts = await _store.GetContactExecutionsAsync(tenantId, request.RunId ?? string.Empty, ct);
        var contact = contacts.FirstOrDefault(x => x.Id == contactExecutionId);
        if (contact is null) return BadRequest(new { message = "Contact execution was not found in the provided run." });

        var existing = await _store.GetCallOutcomeByContactAsync(tenantId, contactExecutionId, ct);
        if (existing is not null) return Conflict(new { message = "Call outcome already exists for this contact execution.", outcomeId = existing.Id });

        var now = DateTimeOffset.UtcNow;
        var outcome = await _store.CreateCallOutcomeAsync(new CampaignCallOutcomeContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            CampaignId = contact.CampaignId,
            RunId = contact.RunId,
            ContactExecutionId = contactExecutionId,
            PlaybookId = request.PlaybookId,
            CallId = request.CallId,
            Status = request.Status ?? CampaignCallOutcomeStatus.Queued,
            StartedAt = request.StartedAt ?? now,
            EndedAt = request.EndedAt,
            TranscriptJson = request.TranscriptJson,
            AnswersJson = request.AnswersJson ?? "{}",
            Summary = request.Summary?.Trim() ?? string.Empty,
            Sentiment = request.Sentiment?.Trim() ?? string.Empty,
            NextAction = request.NextAction?.Trim() ?? string.Empty,
            LinkedPartyId = request.LinkedPartyId ?? contact.PartyId,
            LinkedSaleId = request.LinkedSaleId,
            LinkedInvoiceId = request.LinkedInvoiceId,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);

        await _store.UpdateContactExecutionAsync(contact with
        {
            CallId = outcome.CallId ?? contact.CallId,
            CallOutcomeId = outcome.Id,
            UpdatedAt = now
        }, ct);

        return CreatedAtAction(nameof(Get), new { tenantId, outcomeId = outcome.Id }, outcome);
    }

    [HttpPut("campaign-call-outcomes/{outcomeId}")]
    public async Task<IActionResult> Update([FromRoute] string tenantId, [FromRoute] string outcomeId, [FromBody] UpsertCampaignCallOutcomeRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();
        var existing = await _store.GetCallOutcomeAsync(tenantId, outcomeId, ct);
        if (existing is null) return NotFound();

        var updated = await _store.UpdateCallOutcomeAsync(existing with
        {
            PlaybookId = request.PlaybookId ?? existing.PlaybookId,
            CallId = request.CallId ?? existing.CallId,
            Status = request.Status ?? existing.Status,
            StartedAt = request.StartedAt ?? existing.StartedAt,
            EndedAt = request.EndedAt ?? existing.EndedAt,
            TranscriptJson = request.TranscriptJson ?? existing.TranscriptJson,
            AnswersJson = request.AnswersJson ?? existing.AnswersJson,
            Summary = request.Summary?.Trim() ?? existing.Summary,
            Sentiment = request.Sentiment?.Trim() ?? existing.Sentiment,
            NextAction = request.NextAction?.Trim() ?? existing.NextAction,
            LinkedPartyId = request.LinkedPartyId ?? existing.LinkedPartyId,
            LinkedSaleId = request.LinkedSaleId ?? existing.LinkedSaleId,
            LinkedInvoiceId = request.LinkedInvoiceId ?? existing.LinkedInvoiceId,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        if (updated is null) return NotFound();
        return Ok(updated);
    }

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }
}

public sealed record UpsertCampaignCallOutcomeRequest
{
    public string? RunId { get; init; }
    public string? PlaybookId { get; init; }
    public string? CallId { get; init; }
    public CampaignCallOutcomeStatus? Status { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public string? TranscriptJson { get; init; }
    public string? AnswersJson { get; init; }
    public string? Summary { get; init; }
    public string? Sentiment { get; init; }
    public string? NextAction { get; init; }
    public string? LinkedPartyId { get; init; }
    public string? LinkedSaleId { get; init; }
    public string? LinkedInvoiceId { get; init; }
}
