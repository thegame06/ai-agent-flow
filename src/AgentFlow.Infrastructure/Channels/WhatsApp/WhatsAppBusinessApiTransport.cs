using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentFlow.Infrastructure.Channels.WhatsApp;

public sealed class WhatsAppBusinessApiTransport : IWhatsAppTransport
{
    private readonly WhatsAppOptions _options;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private bool _isConnected;
    private string? _activePhoneNumberId;

    public string Mode => "business";

    public WhatsAppBusinessApiTransport(WhatsAppOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task<QrAuthResult> ConnectAsync(string channelId, string? apiToken, string? phoneNumberId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiToken) || string.IsNullOrWhiteSpace(phoneNumberId))
            return QrAuthResult.Fail("Business mode requires ApiToken and PhoneNumberId");

        _options.ApiKey = apiToken;
        _activePhoneNumberId = phoneNumberId;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        var response = await _httpClient.GetAsync($"{_options.BaseUrl}/{phoneNumberId}", ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("WhatsApp Business validation failed: {StatusCode} {Body}", response.StatusCode, body);
            return QrAuthResult.Fail($"Business API validation failed: {(int)response.StatusCode}");
        }

        _isConnected = true;
        _logger.LogInformation("WhatsApp Business API connected for {PhoneNumberId}", phoneNumberId);
        return QrAuthResult.Ok("business-api-connected");
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _isConnected = false;
        _activePhoneNumberId = null;
        return Task.CompletedTask;
    }

    public Task<bool> IsConnectedAsync(CancellationToken ct = default)
        => Task.FromResult(_isConnected);

    public async Task<string> SendTextMessageAsync(string to, string content, CancellationToken ct = default)
    {
        if (!_isConnected || string.IsNullOrEmpty(_activePhoneNumberId) || string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("WhatsApp Business transport is not connected");

        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to,
            type = "text",
            text = new { body = content }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/{_activePhoneNumberId}/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"WhatsApp send failed: {(int)response.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var id = doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("WhatsApp response missing message id");

        return id;
    }
}
