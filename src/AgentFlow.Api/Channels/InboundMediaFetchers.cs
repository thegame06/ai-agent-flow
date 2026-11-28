using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Voice;
using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Api.Channels;

/// <summary>Downloads a file a customer sent through one provider (the provider keeps it behind its own auth).</summary>
public interface IInboundMediaFetcher
{
    bool Handles(string provider);
    Task<(byte[] Content, string ContentType)?> FetchAsync(string tenantId, ChannelDefinition channel, InboundAttachment attachment, CancellationToken ct = default);
}

/// <summary>Twilio media URLs need the account SID and auth token (HTTP basic auth).</summary>
public sealed class TwilioMediaFetcher : IInboundMediaFetcher
{
    private readonly IHttpClientFactory _http;
    private readonly ITwilioCredentials _credentials;

    public TwilioMediaFetcher(IHttpClientFactory http, ITwilioCredentials credentials)
    {
        _http = http;
        _credentials = credentials;
    }

    public bool Handles(string provider) => provider == "twilio";

    public async Task<(byte[] Content, string ContentType)?> FetchAsync(string tenantId, ChannelDefinition channel, InboundAttachment attachment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(attachment.Url) || !attachment.Url!.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
        var account = await _credentials.GetAsync(tenantId, channel.Config.GetValueOrDefault("ConnectionId"), ct);
        if (account is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, attachment.Url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{account.AccountSid}:{account.AuthToken}")));
        using var response = await _http.CreateClient("inbound-media").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return (bytes, response.Content.Headers.ContentType?.MediaType ?? attachment.MimeType ?? "application/octet-stream");
    }
}

/// <summary>Meta gives a media id: ask the Graph API for its URL, then download it, both with the channel's token.</summary>
public sealed class MetaMediaFetcher : IInboundMediaFetcher
{
    private const string Graph = "https://graph.facebook.com/v23.0";
    private readonly IHttpClientFactory _http;
    private readonly IMetaWhatsAppAccounts _accounts;

    public MetaMediaFetcher(IHttpClientFactory http, IMetaWhatsAppAccounts accounts)
    {
        _http = http;
        _accounts = accounts;
    }

    public bool Handles(string provider) => provider == "meta";

    public async Task<(byte[] Content, string ContentType)?> FetchAsync(string tenantId, ChannelDefinition channel, InboundAttachment attachment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(attachment.ProviderMediaId)) return null;
        var token = (await _accounts.GetForChannelAsync(tenantId, channel.Id, ct))?.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) return null;
        var client = _http.CreateClient("inbound-media");

        using var lookup = new HttpRequestMessage(HttpMethod.Get, $"{Graph}/{Uri.EscapeDataString(attachment.ProviderMediaId!)}");
        lookup.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var info = await client.SendAsync(lookup, ct);
        if (!info.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await info.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("url", out var urlElement) || urlElement.GetString() is not { Length: > 0 } url) return null;
        var mime = doc.RootElement.TryGetProperty("mime_type", out var m) ? m.GetString() : attachment.MimeType;

        using var download = new HttpRequestMessage(HttpMethod.Get, url);
        download.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var file = await client.SendAsync(download, ct);
        if (!file.IsSuccessStatusCode) return null;
        return (await file.Content.ReadAsByteArrayAsync(ct), mime ?? file.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }
}
