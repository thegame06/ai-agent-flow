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
    public string? QrBridgeBaseUrl { get; init; }
    public string? QrBridgeApiKey { get; init; }
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
/// WhatsApp client delegating to transport providers (business API and QR mode).
/// </summary>
public sealed class WhatsAppClient
{
    private readonly ILogger _logger;
    private readonly IWhatsAppTransport _businessTransport;
    private readonly IWhatsAppTransport _qrTransport;
    private IWhatsAppTransport? _activeTransport;

    public WhatsAppClient(WhatsAppOptions options, ILogger logger)
    {
        _logger = logger;
        _businessTransport = new WhatsAppBusinessApiTransport(options, logger);
        _qrTransport = new WhatsAppWebQrTransport(options, logger);
    }

    public async Task<QrAuthResult> ConnectWithQrAsync(string channelId, CancellationToken ct = default)
    {
        _activeTransport = _qrTransport;
        var result = await _activeTransport.ConnectAsync(channelId, null, null, ct);
        if (!result.Success) _activeTransport = null;
        return result;
    }

    public async Task ConnectWithBusinessApiAsync(string apiToken, string phoneNumberId, CancellationToken ct = default)
    {
        _activeTransport = _businessTransport;
        var result = await _activeTransport.ConnectAsync("business", apiToken, phoneNumberId, ct);

        if (!result.Success)
        {
            _activeTransport = null;
            throw new InvalidOperationException(result.Error ?? "Failed to connect WhatsApp Business API");
        }

        _logger.LogInformation("WhatsApp connected in {Mode} mode", _businessTransport.Mode);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_activeTransport != null)
            await _activeTransport.DisconnectAsync(ct);

        _activeTransport = null;
    }

    public async Task<bool> IsConnectedAsync(CancellationToken ct = default)
    {
        if (_activeTransport == null) return false;
        return await _activeTransport.IsConnectedAsync(ct);
    }

    public async Task<string> SendTextMessageAsync(string to, string content, CancellationToken ct = default)
    {
        if (_activeTransport == null)
            throw new InvalidOperationException("WhatsApp client is not connected");

        return await _activeTransport.SendTextMessageAsync(to, content, ct);
    }
}
