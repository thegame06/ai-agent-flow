using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentFlow.Infrastructure.Channels.WhatsApp;

public sealed class WhatsAppWebQrTransport : IWhatsAppTransport
{
    private readonly WhatsAppOptions _options;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private string? _channelId;

    public string Mode => "qr";

    public WhatsAppWebQrTransport(WhatsAppOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public void SetChannelContext(string channelId)
    {
        _channelId = channelId;
        ApplyBridgeAuth();
    }

    public async Task<QrAuthResult> ConnectAsync(string channelId, string? apiToken, string? phoneNumberId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.QrBridgeBaseUrl))
            return QrAuthResult.Fail("QrBridgeBaseUrl is required for QR mode");

        _channelId = channelId;
        ApplyBridgeAuth();

        var startPayload = new
        {
            channelId
        };

        var startResponse = await _httpClient.PostAsync(
            $"{_options.QrBridgeBaseUrl.TrimEnd('/')}/session/start",
            new StringContent(JsonSerializer.Serialize(startPayload), Encoding.UTF8, "application/json"),
            ct);

        if (!startResponse.IsSuccessStatusCode)
        {
            var body = await startResponse.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("QR bridge start failed: {StatusCode} {Body}", startResponse.StatusCode, body);
            return QrAuthResult.Fail($"QR bridge start failed: {(int)startResponse.StatusCode}");
        }

        var qrResponse = await _httpClient.GetAsync($"{_options.QrBridgeBaseUrl.TrimEnd('/')}/session/qr?channelId={Uri.EscapeDataString(channelId)}", ct);
        if (!qrResponse.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "QR bridge session started for channel {ChannelId}; QR still pending (status {StatusCode}).",
                channelId,
                (int)qrResponse.StatusCode);
            // Session is started; QR can appear a few seconds later.
            return new QrAuthResult { Success = true, QrCode = null };
        }

        var qrBody = await qrResponse.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(qrBody);
        var qr = doc.RootElement.TryGetProperty("qrCode", out var qrElement) ? qrElement.GetString() : null;

        if (string.IsNullOrWhiteSpace(qr))
        {
            _logger.LogInformation("QR bridge session started for channel {ChannelId}; QR not available yet.", channelId);
            return new QrAuthResult { Success = true, QrCode = null };
        }

        return QrAuthResult.Ok(qr);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.QrBridgeBaseUrl) || string.IsNullOrWhiteSpace(_channelId))
            return;

        ApplyBridgeAuth();
        try
        {
            await _httpClient.PostAsync(
                $"{_options.QrBridgeBaseUrl.TrimEnd('/')}/session/disconnect",
                new StringContent(JsonSerializer.Serialize(new { channelId = _channelId }), Encoding.UTF8, "application/json"),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QR bridge disconnect failed (non-blocking) for channel {ChannelId}", _channelId);
        }
    }

    public async Task<bool> IsConnectedAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.QrBridgeBaseUrl) || string.IsNullOrWhiteSpace(_channelId))
            return false;

        ApplyBridgeAuth();
        var response = await _httpClient.GetAsync($"{_options.QrBridgeBaseUrl.TrimEnd('/')}/session/status?channelId={Uri.EscapeDataString(_channelId)}", ct);
        if (!response.IsSuccessStatusCode) return false;

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("connected", out var connected) && connected.GetBoolean();
    }

    public async Task<string?> GetQrCodeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.QrBridgeBaseUrl) || string.IsNullOrWhiteSpace(_channelId))
            return null;

        ApplyBridgeAuth();
        var response = await _httpClient.GetAsync($"{_options.QrBridgeBaseUrl.TrimEnd('/')}/session/qr?channelId={Uri.EscapeDataString(_channelId)}", ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "QR bridge /session/qr failed for channel {ChannelId}: {StatusCode} {Body}",
                _channelId,
                (int)response.StatusCode,
                errorBody);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("qrCode", out var qr) ? qr.GetString() : null;
    }

    public async Task<string> SendTextMessageAsync(string to, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.QrBridgeBaseUrl) || string.IsNullOrWhiteSpace(_channelId))
            throw new InvalidOperationException("QR bridge is not configured/connected");

        ApplyBridgeAuth();
        var payload = new { channelId = _channelId, to, content };

        var response = await _httpClient.PostAsync(
            $"{_options.QrBridgeBaseUrl.TrimEnd('/')}/messages/send",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"QR bridge send failed: {(int)response.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var id = doc.RootElement.TryGetProperty("messageId", out var messageId) ? messageId.GetString() : null;
        return string.IsNullOrWhiteSpace(id) ? $"qr-{Guid.NewGuid():N}" : id;
    }

    private void ApplyBridgeAuth()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
        if (!string.IsNullOrWhiteSpace(_options.QrBridgeApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.QrBridgeApiKey);
        }
    }

    /// <summary>
    /// QR/WhatsApp Web does not support Business API templates.
    /// Template messages are only available via WhatsApp Business Cloud API.
    /// Log a warning and fall back to a plain text message as best-effort.
    /// </summary>
    public async Task<string> SendTemplateMessageAsync(string to, string templateName, CancellationToken ct = default)
    {
        // QR transport has no template API — send a plain notice instead so the conversation
        // is not silently dropped. In production use the Business API transport.
        var fallbackContent = $"[Template: {templateName}] — Por favor contáctanos para continuar.";
        return await SendTextMessageAsync(to, fallbackContent, ct);
    }
}
