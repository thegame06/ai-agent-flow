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
    private bool _isConnected;
    private string? _activePhoneNumberId;

    public WhatsAppClient(WhatsAppOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<QrAuthResult> ConnectWithQrAsync(string channelId, CancellationToken ct = default)
    {
        // TODO: Implement QR-based authentication (like OpenClaw/whatsapp-web.js)
        // For now, return mock success
        await Task.Delay(100, ct);
        _isConnected = true;
        _logger.LogInformation("WhatsApp QR mock connection for channel {ChannelId}", channelId);
        return QrAuthResult.Ok("data:image/png;base64,...mock-qr-code...");
    }

    public async Task ConnectWithBusinessApiAsync(string apiToken, string phoneNumberId, CancellationToken ct = default)
    {
        _options.ApiKey = apiToken;
        _activePhoneNumberId = phoneNumberId;
        
        // Validate connection
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);

        try
        {
            var response = await httpClient.GetAsync(
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
        if (!_isConnected || string.IsNullOrEmpty(_activePhoneNumberId))
            throw new InvalidOperationException("WhatsApp client not connected");

        // Mock send for now
        await Task.Delay(50, ct);
        _logger.LogInformation("WhatsApp mock send to {To}: {Content}", to, content);
        return $"mock_wamid_{Guid.NewGuid():N}";
    }
}
