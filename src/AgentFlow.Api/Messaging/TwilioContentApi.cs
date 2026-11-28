using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentFlow.Api.Voice;

namespace AgentFlow.Api.Messaging;

/// <summary>A Twilio Content template with its WhatsApp approval, as Twilio returns it.</summary>
public sealed record TwilioContent(
    string Sid,
    string FriendlyName,
    string Language,
    string? Body,
    IReadOnlyList<string> QuickReplies,
    IReadOnlyDictionary<string, string> Examples,
    string? ApprovalName,
    string Category,
    string Status,
    string? RejectionReason);

public sealed class TwilioApiException(string message, int status) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Twilio's Content API: the templates of an account and their WhatsApp approval.</summary>
public interface ITwilioContentApi
{
    Task<IReadOnlyList<TwilioContent>> ListAsync(TwilioAccount account, CancellationToken ct = default);
    /// <summary>Creates the content; returns its Content SID (HX…).</summary>
    Task<string> CreateAsync(TwilioAccount account, string friendlyName, string language, string body, IReadOnlyList<string> quickReplies, IReadOnlyDictionary<string, string> examples, CancellationToken ct = default);
    /// <summary>Sends it to WhatsApp for review; returns the approval status Twilio reports.</summary>
    Task<string> SubmitForWhatsAppAsync(TwilioAccount account, string contentSid, string name, string category, CancellationToken ct = default);
    Task DeleteAsync(TwilioAccount account, string contentSid, CancellationToken ct = default);
}

public sealed class TwilioContentApi : ITwilioContentApi
{
    private const string BaseUrl = "https://content.twilio.com/v1";
    private readonly IHttpClientFactory _http;

    public TwilioContentApi(IHttpClientFactory http) => _http = http;

    public async Task<IReadOnlyList<TwilioContent>> ListAsync(TwilioAccount account, CancellationToken ct = default)
    {
        var result = new List<TwilioContent>();
        string? url = $"{BaseUrl}/ContentAndApprovals?PageSize=100";
        var pages = 0;
        while (url is not null && pages++ < 50)
        {
            using var doc = await SendAsync(account, new HttpRequestMessage(HttpMethod.Get, url), ct);
            if (doc.RootElement.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array)
                foreach (var item in contents.EnumerateArray())
                    result.Add(Parse(item));
            url = doc.RootElement.TryGetProperty("meta", out var meta) ? Str(meta, "next_page_url") : null;
        }
        return result;
    }

    public async Task<string> CreateAsync(TwilioAccount account, string friendlyName, string language, string body,
        IReadOnlyList<string> quickReplies, IReadOnlyDictionary<string, string> examples, CancellationToken ct = default)
    {
        var types = new JsonObject();
        if (quickReplies.Count > 0)
            types["twilio/quick-reply"] = new JsonObject
            {
                ["body"] = body,
                ["actions"] = new JsonArray(quickReplies.Select((title, i) => (JsonNode)new JsonObject { ["title"] = title, ["id"] = $"r{i + 1}" }).ToArray())
            };
        // Plain text is always there: it is what is sent where buttons cannot be shown.
        types["twilio/text"] = new JsonObject { ["body"] = body };
        var payload = new JsonObject
        {
            ["friendly_name"] = friendlyName,
            ["language"] = language,
            ["variables"] = new JsonObject(examples.Select(e => KeyValuePair.Create(e.Key, (JsonNode?)e.Value))),
            ["types"] = types
        };
        using var doc = await SendAsync(account, Json(HttpMethod.Post, $"{BaseUrl}/Content", payload), ct);
        return Str(doc.RootElement, "sid") ?? throw new TwilioApiException("Twilio no devolvio el Content SID.", 502);
    }

    public async Task<string> SubmitForWhatsAppAsync(TwilioAccount account, string contentSid, string name, string category, CancellationToken ct = default)
    {
        var payload = new JsonObject { ["name"] = name, ["category"] = category.ToUpperInvariant() };
        using var doc = await SendAsync(account, Json(HttpMethod.Post, $"{BaseUrl}/Content/{Uri.EscapeDataString(contentSid)}/ApprovalRequests/whatsapp", payload), ct);
        return StatusOf(Str(doc.RootElement, "status"));
    }

    public async Task DeleteAsync(TwilioAccount account, string contentSid, CancellationToken ct = default)
    {
        using var _ = await SendAsync(account, new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/Content/{Uri.EscapeDataString(contentSid)}"), ct);
    }

    /// <summary>One item of ContentAndApprovals (or of Content): its text, buttons, examples and WhatsApp approval.</summary>
    public static TwilioContent Parse(JsonElement item)
    {
        string? body = null;
        var replies = new List<string>();
        if (item.TryGetProperty("types", out var types) && types.ValueKind == JsonValueKind.Object)
        {
            // Prefer the richest type that has a body; plain text otherwise.
            foreach (var type in types.EnumerateObject().OrderBy(t => t.Name == "twilio/text" ? 1 : 0))
            {
                if (type.Value.ValueKind != JsonValueKind.Object) continue;
                body ??= Str(type.Value, "body");
                if (replies.Count == 0 && type.Name == "twilio/quick-reply" && type.Value.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
                    replies.AddRange(actions.EnumerateArray().Select(a => Str(a, "title")).Where(t => !string.IsNullOrWhiteSpace(t))!);
            }
        }

        var examples = new Dictionary<string, string>();
        if (item.TryGetProperty("variables", out var vars) && vars.ValueKind == JsonValueKind.Object)
            foreach (var v in vars.EnumerateObject())
                if (v.Value.ValueKind == JsonValueKind.String) examples[v.Name] = v.Value.GetString()!;

        // "approval_requests" is the WhatsApp approval object (some responses nest it under "whatsapp").
        JsonElement approval = default;
        if (item.TryGetProperty("approval_requests", out var ar) && ar.ValueKind == JsonValueKind.Object)
            approval = ar.TryGetProperty("whatsapp", out var wa) && wa.ValueKind == JsonValueKind.Object ? wa : ar;
        var hasApproval = approval.ValueKind == JsonValueKind.Object;

        return new TwilioContent(
            Str(item, "sid") ?? string.Empty,
            Str(item, "friendly_name") ?? string.Empty,
            Str(item, "language") ?? "es",
            body,
            replies,
            examples,
            hasApproval ? Str(approval, "name") : null,
            (hasApproval ? Str(approval, "category") : null)?.ToUpperInvariant() ?? "UTILITY",
            StatusOf(hasApproval ? Str(approval, "status") : null),
            hasApproval && Str(approval, "rejection_reason") is { Length: > 0 } reason ? reason : null);
    }

    /// <summary>Twilio's approval status in the catalog's words: received counts as pending; never submitted is UNSUBMITTED.</summary>
    public static string StatusOf(string? twilioStatus) => twilioStatus?.Trim().ToLowerInvariant() switch
    {
        "approved" => WhatsAppTemplateStatuses.Approved,
        "pending" or "received" => WhatsAppTemplateStatuses.Pending,
        "rejected" => WhatsAppTemplateStatuses.Rejected,
        "paused" => WhatsAppTemplateStatuses.Paused,
        "disabled" => WhatsAppTemplateStatuses.Disabled,
        _ => WhatsAppTemplateStatuses.Unsubmitted
    };

    private async Task<JsonDocument> SendAsync(TwilioAccount account, HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{account.AccountSid}:{account.AuthToken}")));
        using var client = _http.CreateClient("twilio-content");
        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            string? message = null;
            try { using var err = JsonDocument.Parse(text); message = Str(err.RootElement, "message"); } catch (JsonException) { }
            throw new TwilioApiException(message ?? $"Twilio respondio {(int)response.StatusCode}.", (int)response.StatusCode);
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private static HttpRequestMessage Json(HttpMethod method, string url, JsonNode payload)
        => new(method, url) { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") };

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
