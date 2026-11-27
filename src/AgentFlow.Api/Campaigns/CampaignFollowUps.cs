using System.Text.Json;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Collections;

namespace AgentFlow.Api.Campaigns;

/// <summary>
/// One follow-up step of a campaign. Triggers:
/// <c>no_response</c> (sent, no reply after <see cref="AfterHours"/>),
/// <c>outcome:&lt;code&gt;</c> (the contact ended in that outcome, <see cref="AfterHours"/> ago),
/// <c>promise_due</c> (a pending promise is due within <see cref="BeforeHours"/>),
/// <c>promise_broken</c> (the promise was not kept, <see cref="AfterHours"/> ago).
/// </summary>
public sealed record CampaignFollowUpRule
{
    public string Id { get; init; } = string.Empty;
    public string Trigger { get; init; } = string.Empty;
    public double AfterHours { get; init; }
    public double BeforeHours { get; init; }
    /// <summary>message (send <see cref="Message"/>) or handoff (flag the contact for a person).</summary>
    public string Action { get; init; } = "message";
    public string? Message { get; init; }
}

/// <summary>A campaign's follow-up policy, stored in <c>FollowupPolicyJson</c>.</summary>
public sealed record CampaignFollowUpPolicy
{
    public bool Enabled { get; init; } = true;
    /// <summary>Most follow-up messages one contact can receive from this campaign.</summary>
    public int MaxFollowUps { get; init; } = 3;
    public List<CampaignFollowUpRule> Rules { get; init; } = [];

    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static CampaignFollowUpPolicy? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var policy = JsonSerializer.Deserialize<CampaignFollowUpPolicy>(json, Json);
            return policy is { Enabled: true, Rules.Count: > 0 } ? policy : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// When and how often a customer may be messaged: a local-time window (from the campaign's
/// <c>ExecutionWindowJson</c>, in its timezone) and a daily cap per recipient across campaigns
/// (from <c>ThrottleJson</c>). Without them a campaign has no restriction.
/// </summary>
public sealed record CampaignContactPolicy
{
    public int StartHour { get; init; }
    public int EndHour { get; init; } = 24;
    /// <summary>Allowed days, 0 = Sunday … 6 = Saturday; null = every day.</summary>
    public List<int>? Days { get; init; }
    /// <summary>0 = no daily cap.</summary>
    public int MaxPerRecipientPerDay { get; init; }
    public string Timezone { get; init; } = "UTC";

    public static CampaignContactPolicy From(CampaignContract campaign)
    {
        var window = Read(campaign.ExecutionWindowJson);
        var throttle = Read(campaign.ThrottleJson);
        return new CampaignContactPolicy
        {
            StartHour = Int(window, "startHour") ?? 0,
            EndHour = Int(window, "endHour") ?? 24,
            Days = window is { } w && w.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Array
                ? d.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList()
                : null,
            MaxPerRecipientPerDay = Int(throttle, "maxPerRecipientPerDay") ?? 0,
            Timezone = string.IsNullOrWhiteSpace(campaign.Timezone) ? "UTC" : campaign.Timezone
        };
    }

    public DateTimeOffset LocalTime(DateTimeOffset utc) => ToLocal(utc, Timezone);

    public bool IsOpen(DateTimeOffset utcNow)
    {
        var local = LocalTime(utcNow);
        if (Days is { Count: > 0 } && !Days.Contains((int)local.DayOfWeek)) return false;
        return local.Hour >= StartHour && local.Hour < EndHour;
    }

    /// <summary>Next moment (UTC) the window is open, searching up to 8 days ahead.</summary>
    public DateTimeOffset NextOpen(DateTimeOffset utcNow)
    {
        if (IsOpen(utcNow)) return utcNow;
        var local = LocalTime(utcNow);
        var candidate = new DateTimeOffset(local.Year, local.Month, local.Day, Math.Clamp(StartHour, 0, 23), 0, 0, local.Offset);
        for (var i = 0; i < 9; i++)
        {
            if (candidate > local && IsOpen(candidate.ToUniversalTime())) return candidate.ToUniversalTime();
            candidate = candidate.AddDays(1);
        }
        return utcNow.AddHours(1);
    }

    /// <summary>Start of the recipient's current local day, in UTC (for the daily cap).</summary>
    public DateTimeOffset LocalDayStartUtc(DateTimeOffset utcNow)
    {
        var local = LocalTime(utcNow);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset).ToUniversalTime();
    }

    public static DateTimeOffset ToLocal(DateTimeOffset utc, string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone)) return utc;
        try { return TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(timezone)); }
        catch (Exception) { return utc; }
    }

    private static JsonElement? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    private static int? Int(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } e) return null;
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.TryGetInt32(out var v))
                return v;
        return null;
    }
}

/// <summary>Decides which follow-up rules are due for a contact. Pure: no I/O, easy to test.</summary>
public static class CampaignFollowUpPlanner
{
    // Customers who said this must not be chased again.
    private static readonly HashSet<string> Terminal = new(StringComparer.OrdinalIgnoreCase)
    {
        CampaignOutcomes.OptOut, CampaignOutcomes.WrongNumber, CampaignOutcomes.AlreadyPaid, CampaignOutcomes.Dispute
    };

    public static IReadOnlyList<CampaignFollowUpRule> Plan(
        CampaignContactExecutionContract contact,
        CampaignFollowUpPolicy? policy,
        DateTimeOffset now,
        CollectionPromiseDocument? promise = null)
    {
        if (policy is null || !policy.Enabled) return [];
        if (contact.Status is CampaignContactExecutionStatus.Failed or CampaignContactExecutionStatus.Skipped or CampaignContactExecutionStatus.Queued)
            return [];
        if (contact.Outcome is not null && Terminal.Contains(contact.Outcome)) return [];

        var done = (contact.FollowUps ?? []).Select(f => f.RuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var messagesSent = (contact.FollowUps ?? []).Count(f => f.Action == "message" && f.Status == "sent");

        var due = new List<CampaignFollowUpRule>();
        foreach (var rule in policy.Rules.Where(r => !string.IsNullOrWhiteSpace(r.Id) && !done.Contains(r.Id)))
        {
            if (rule.Action == "message" && messagesSent >= policy.MaxFollowUps) continue;
            if (IsDue(rule, contact, now, promise)) due.Add(rule);
        }

        // At most one message per contact per pass, so a customer never gets two at once.
        var firstMessage = due.FirstOrDefault(r => r.Action == "message");
        return due.Where(r => r.Action != "message" || r == firstMessage).ToList();
    }

    private static bool IsDue(CampaignFollowUpRule rule, CampaignContactExecutionContract c, DateTimeOffset now, CollectionPromiseDocument? promise)
    {
        var trigger = rule.Trigger.Trim();
        if (trigger.Equals("no_response", StringComparison.OrdinalIgnoreCase))
            return c.RepliedAt is null
                && (c.Outcome is null || c.Outcome == CampaignOutcomes.NoResponse)
                && now >= c.CreatedAt.AddHours(rule.AfterHours);

        if (trigger.StartsWith("outcome:", StringComparison.OrdinalIgnoreCase))
        {
            var code = trigger["outcome:".Length..];
            return string.Equals(c.Outcome, code, StringComparison.OrdinalIgnoreCase)
                && now >= (c.OutcomeAt ?? c.UpdatedAt).AddHours(rule.AfterHours);
        }

        if (trigger.Equals("promise_due", StringComparison.OrdinalIgnoreCase))
            return promise is { Status: CollectionPromiseStatus.Pending }
                && now >= promise.PromisedDate.AddHours(-rule.BeforeHours)
                && now < promise.PromisedDate.AddDays(1);

        if (trigger.Equals("promise_broken", StringComparison.OrdinalIgnoreCase))
            return promise is { Status: CollectionPromiseStatus.Broken }
                && now >= (promise.ResolvedAt ?? promise.PromisedDate).AddHours(rule.AfterHours);

        return false;
    }
}
