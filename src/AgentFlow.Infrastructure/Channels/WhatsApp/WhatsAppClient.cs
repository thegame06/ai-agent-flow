using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Channels.WhatsApp;

public sealed record WhatsAppOptions
{
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; init; }
    public string? PhoneNumberId { get; init; }
    public string? BusinessAccountId { get; init; }
    public string WebhookVerifyToken { get; init; } = "agentflow-whatsapp-webhook";
    public string BaseUrl { get; init; } = "https://graph.facebook.com/v17.0";
}

public sealed record WhatsAppIncomingMessage
{
    public string Id { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public long Timestamp { get; init; }
    public WhatsAppTextMessage? Text { get; init; }
    public string? Caption { get; init; }
    public WhatsAppProfile? Profile { get; init; }
}

public sealed record WhatsAppTextMessage
{
    public string Body { get; init; } = string.Empty;
}

public sealed record WhatsAppProfile
{
    public string Name { get; init; } = string.Empty;
}

public sealed record QrAuthResult
{
    public bool Success { get; init; }
    public string? QrCode { get; init; }
    public string? Error { get; init; }

    public static QrAuthResult Ok(string qrCode) => new() { Success = true, QrCode = qrCode };
    public static QrAuthResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Low-level WhatsApp client handling QR auth and Business API.
/// </summary>
public sealed class WhatsAppClient
{
    private readonly WhatsAppOptions _options;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private bool _isConnected;
    private string? _activePhoneNumberId;

    public WhatsAppClient(WhatsAppOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task<QrAuthResult> ConnectWithQrAsync(string channelId, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        _isConnected = false;
        _logger.LogWarning("WhatsApp QR auth is not implemented for production use. Channel {ChannelId} remains disconnected.", channelId);
        return QrAuthResult.Fail("QR auth mode not implemented. Use AuthMode=business with WhatsApp Cloud API credentials.");
    }

    public async Task ConnectWithBusinessApiAsync(string apiToken, string phoneNumberId, CancellationToken ct = default)
    {
        _options.ApiKey = apiToken;
        _activePhoneNumberId = phoneNumberId;
        
        // Validate connection
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);

        try
        {
            var response = await _httpClient.GetAsync(
                $"{_options.BaseUrl}/{phoneNumberId}",
                ct
            );
            
            if (response.IsSuccessStatusCode)
            {
                _isConnected = true;
                _logger.LogInformation("WhatsApp Business API connected for {PhoneNumberId}", phoneNumberId);
            }
            else
            {
                _logger.LogWarning("WhatsApp Business API validation failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp Business API connection error");
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _isConnected = false;
        _activePhoneNumberId = null;
        await Task.CompletedTask;
    }

    public async Task<bool> IsConnectedAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask;
        return _isConnected;
    }

    public async Task<string> SendTextMessageAsync(string to, string content, CancellationToken ct = default)
    {
        if (!_isConnected || string.IsNullOrEmpty(_activePhoneNumberId) || string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("WhatsApp client not connected with Business API credentials");

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
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8,
                "application/json")
        };

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("WhatsApp send failed. Status: {StatusCode}, Body: {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"WhatsApp send failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var id = doc.RootElement
                .GetProperty("messages")[0]
                .GetProperty("id")
                .GetString();

            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("WhatsApp response missing message id");

            _logger.LogInformation("WhatsApp message sent to {To}. MessageId: {MessageId}", to, id);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse WhatsApp send response: {Body}", body);
            throw;
        }
    }
}
