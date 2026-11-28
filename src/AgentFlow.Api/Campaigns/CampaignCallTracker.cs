using AgentFlow.Abstractions.Connect;

namespace AgentFlow.Api.Campaigns;

/// <summary>What a Twilio call status means for the campaign contact it was placed for.</summary>
public sealed record CallStatusEffect(
    CampaignContactExecutionStatus? ContactStatus,
    CampaignCallOutcomeStatus? OutcomeStatus,
    string? Reason,
    bool Unreachable,
    bool Ended);

public interface ICampaignCallTracker
{
    /// <summary>
    /// Applies a call status (and, with answering-machine detection, who answered) to the campaign
    /// contact and call outcome of that call. Calls that no campaign placed are ignored.
    /// </summary>
    Task ApplyAsync(string tenantId, string callSid, string status, string? answeredBy, string? durationSeconds, CancellationToken ct = default);
}

/// <summary>
/// Keeps a call campaign's results true to what happened on the phone: answered, voicemail, busy,
/// no answer or failed (before, the contact stayed "sent" and the outcome "in progress").
/// </summary>
public sealed class CampaignCallTracker : ICampaignCallTracker
{
    private readonly ICampaignStore _store;
    private readonly ICampaignOutcomeService? _outcomes;
    private readonly ILogger<CampaignCallTracker> _logger;

    public CampaignCallTracker(ICampaignStore store, ILogger<CampaignCallTracker> logger, ICampaignOutcomeService? outcomes = null)
    {
        _store = store;
        _outcomes = outcomes;
        _logger = logger;
    }

    /// <summary>Twilio's AnsweredBy: machine_start, machine_end_beep/silence/other and fax are not a person.</summary>
    public static bool IsMachine(string? answeredBy)
        => answeredBy is not null &&
           (answeredBy.StartsWith("machine", StringComparison.OrdinalIgnoreCase) || string.Equals(answeredBy, "fax", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Twilio statuses → contact and outcome. Answered by a person = read; voicemail = delivered
    /// (a notice is left there); busy, no answer, failed or canceled = not reached.
    /// <paramref name="wasVoicemail"/> keeps a voicemail detected earlier from becoming "completed".
    /// </summary>
    public static CallStatusEffect Map(string status, string? answeredBy, bool wasVoicemail = false)
    {
        var machine = wasVoicemail || IsMachine(answeredBy);
        return status.Trim().ToLowerInvariant() switch
        {
            "queued" or "initiated" or "ringing" => new(null, null, null, false, false),
            "in-progress" or "answered" => machine
                ? new(CampaignContactExecutionStatus.Delivered, CampaignCallOutcomeStatus.Voicemail, "Contestó el buzón de voz.", false, false)
                : new(CampaignContactExecutionStatus.Connected, CampaignCallOutcomeStatus.InProgress, null, false, false),
            "completed" => machine
                ? new(CampaignContactExecutionStatus.Delivered, CampaignCallOutcomeStatus.Voicemail, "Contestó el buzón de voz.", false, true)
                : new(CampaignContactExecutionStatus.Read, CampaignCallOutcomeStatus.Completed, null, false, true),
            "busy" => new(CampaignContactExecutionStatus.Failed, CampaignCallOutcomeStatus.NoAnswer, "Ocupado.", true, true),
            "no-answer" => new(CampaignContactExecutionStatus.Failed, CampaignCallOutcomeStatus.NoAnswer, "No contestó.", true, true),
            "failed" => new(CampaignContactExecutionStatus.Failed, CampaignCallOutcomeStatus.Failed, "La llamada no se pudo completar.", true, true),
            "canceled" => new(CampaignContactExecutionStatus.Failed, CampaignCallOutcomeStatus.Failed, "La llamada se canceló.", true, true),
            _ => new(null, null, null, false, false)
        };
    }

    public async Task ApplyAsync(string tenantId, string callSid, string status, string? answeredBy, string? durationSeconds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callSid)) return;
        try
        {
            var contact = await _store.FindContactByCallIdAsync(tenantId, callSid, ct);
            if (contact is null) return;

            var outcome = string.IsNullOrWhiteSpace(contact.CallOutcomeId)
                ? await _store.GetCallOutcomeByContactAsync(tenantId, contact.Id, ct)
                : await _store.GetCallOutcomeAsync(tenantId, contact.CallOutcomeId!, ct);
            var effect = Map(status, answeredBy, outcome?.Status == CampaignCallOutcomeStatus.Voicemail);
            if (effect.ContactStatus is null) return;

            // A later, weaker status (Twilio may send them out of order) must not undo a final one.
            if (!IsFinal(contact.Status) || effect.Ended)
            {
                await _store.UpdateContactExecutionAsync(contact with
                {
                    Status = effect.ContactStatus.Value,
                    SkipReason = effect.ContactStatus == CampaignContactExecutionStatus.Failed ? effect.Reason : contact.SkipReason,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, ct);
            }

            if (outcome is not null && effect.OutcomeStatus is { } outcomeStatus)
            {
                var duration = int.TryParse(durationSeconds, out var seconds) ? $" Duración: {seconds} s." : string.Empty;
                await _store.UpdateCallOutcomeAsync(outcome with
                {
                    Status = outcomeStatus,
                    CallId = outcome.CallId ?? callSid,
                    EndedAt = effect.Ended ? DateTimeOffset.UtcNow : outcome.EndedAt,
                    Summary = effect.Reason is null ? outcome.Summary + duration : effect.Reason + duration,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, ct);
            }

            // Not reaching the customer is the contact's result unless something better is already known.
            if (effect.Unreachable && contact.Outcome is null && _outcomes is not null)
                await _outcomes.RecordAsync(tenantId, contact.Id,
                    new CampaignOutcomeRequest(CampaignOutcomes.Unreachable, CampaignOutcomes.Sources.System, "twilio", effect.Reason), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply call status {Status} of {CallSid} to its campaign contact", status, callSid);
        }
    }

    private static bool IsFinal(CampaignContactExecutionStatus status)
        => status is CampaignContactExecutionStatus.Read or CampaignContactExecutionStatus.Failed or CampaignContactExecutionStatus.Delivered;
}
