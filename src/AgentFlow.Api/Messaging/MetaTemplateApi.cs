using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentFlow.Api.Connect;

namespace AgentFlow.Api.Messaging;

/// <summary>A template as Meta returns it.</summary>
public sealed record MetaTemplate(
    string Id,
    string Name,
    string Language,
    string Status,
    string Category,
    string? ParameterFormat,
    string? QualityScore,
    string? RejectedReason,
    JsonElement Components);

public sealed class MetaApiException(string message, int? statusCode = null, string? metaMessage = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
    /// <summary>Meta's error message (error.error_user_msg or error.message), for the person to read.</summary>
    public string? MetaMessage { get; } = metaMessage;
}

/// <summary>The WhatsApp Business Management API calls for message templates.</summary>
public interface IMetaTemplateApi
{
    Task<IReadOnlyList<MetaTemplate>> ListAsync(MetaWhatsAppAccount account, CancellationToken ct = default);
    /// <returns>Meta's template id and initial status.</returns>
    Task<(string Id, string Status, string? Category)> CreateAsync(MetaWhatsAppAccount account, JsonObject request, CancellationToken ct = default);
    Task EditAsync(MetaWhatsAppAccount account, string metaTemplateId, JsonObject request, CancellationToken ct = default);
    Task DeleteAsync(MetaWhatsAppAccount account, string name, string? metaTemplateId, CancellationToken ct = default);
}

public sealed class MetaTemplateApi : IMetaTemplateApi
{
    private const string Fields = "id,name,language,status,category,components,quality_score,rejected_reason,parameter_format";
    private readonly IHttpClientFactory _http;

    public MetaTemplateApi(IHttpClientFactory http) => _http = http;

    public async Task<IReadOnlyList<MetaTemplate>> ListAsync(MetaWhatsAppAccount account, CancellationToken ct = default)
    {
        var waba = Require(account);
        var templates = new List<MetaTemplate>();
        string? url = $"{account.BaseUrl}/{waba}/message_templates?fields={Fields}&limit=100";
        var pages = 0;
        while (url is not null && pages++ < 50)
        {
            using var doc = await SendAsync(account, new HttpRequestMessage(HttpMethod.Get, url), ct);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var t in data.EnumerateArray())
                    templates.Add(new MetaTemplate(
                        Str(t, "id") ?? string.Empty,
                        Str(t, "name") ?? string.Empty,
                        Str(t, "language") ?? "es",
                        Str(t, "status")?.ToUpperInvariant() ?? WhatsAppTemplateStatuses.Pending,
                        Str(t, "category")?.ToUpperInvariant() ?? "UTILITY",
                        Str(t, "parameter_format")?.ToUpperInvariant(),
                        t.TryGetProperty("quality_score", out var q) ? Str(q, "score") : null,
                        Str(t, "rejected_reason"),
                        t.TryGetProperty("components", out var c) ? c.Clone() : default));
            url = doc.RootElement.TryGetProperty("paging", out var paging) ? Str(paging, "next") : null;
        }
        return templates;
    }

    public async Task<(string Id, string Status, string? Category)> CreateAsync(MetaWhatsAppAccount account, JsonObject request, CancellationToken ct = default)
    {
        var waba = Require(account);
        using var doc = await SendAsync(account, JsonRequest(HttpMethod.Post, $"{account.BaseUrl}/{waba}/message_templates", request), ct);
        return (Str(doc.RootElement, "id") ?? string.Empty,
            Str(doc.RootElement, "status")?.ToUpperInvariant() ?? WhatsAppTemplateStatuses.Pending,
            Str(doc.RootElement, "category")?.ToUpperInvariant());
    }

    public async Task EditAsync(MetaWhatsAppAccount account, string metaTemplateId, JsonObject request, CancellationToken ct = default)
    {
        Require(account);
        using var _ = await SendAsync(account, JsonRequest(HttpMethod.Post, $"{account.BaseUrl}/{metaTemplateId}", request), ct);
    }

    public async Task DeleteAsync(MetaWhatsAppAccount account, string name, string? metaTemplateId, CancellationToken ct = default)
    {
        var waba = Require(account);
        // With hsm_id only that language is deleted; with the name alone, every language of it.
        var url = $"{account.BaseUrl}/{waba}/message_templates?name={Uri.EscapeDataString(name)}" +
                  (string.IsNullOrWhiteSpace(metaTemplateId) ? string.Empty : $"&hsm_id={Uri.EscapeDataString(metaTemplateId)}");
        using var _ = await SendAsync(account, new HttpRequestMessage(HttpMethod.Delete, url), ct);
    }

    private static string Require(MetaWhatsAppAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.AccessToken))
            throw new MetaApiException("La conexion de Meta no tiene access token.");
        if (string.IsNullOrWhiteSpace(account.BusinessAccountId))
            throw new MetaApiException("La conexion de Meta no tiene el id de la cuenta de WhatsApp Business (businessAccountId).");
        return account.BusinessAccountId!;
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string url, JsonObject body)
        => new(method, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private async Task<JsonDocument> SendAsync(MetaWhatsAppAccount account, HttpRequestMessage request, CancellationToken ct)
    {
        using var client = _http.CreateClient("meta-graph");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            string? metaMessage = null;
            try
            {
                using var error = JsonDocument.Parse(body);
                if (error.RootElement.TryGetProperty("error", out var e))
                    metaMessage = Str(e, "error_user_msg") ?? Str(e, "message");
            }
            catch (JsonException)
            {
            }
            throw new MetaApiException($"Meta respondio {(int)response.StatusCode}: {metaMessage ?? body}", (int)response.StatusCode, metaMessage);
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
