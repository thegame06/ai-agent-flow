using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Channels.WhatsApp;

public sealed class WhatsAppWebQrTransport : IWhatsAppTransport
{
    private readonly ILogger _logger;

    public string Mode => "qr";

    public WhatsAppWebQrTransport(ILogger logger)
    {
        _logger = logger;
    }

    public Task<QrAuthResult> ConnectAsync(string channelId, string? apiToken, string? phoneNumberId, CancellationToken ct = default)
    {
        _logger.LogWarning("WhatsApp QR mode requested for channel {ChannelId}, but Web QR transport is not implemented in this runtime.", channelId);
        return Task.FromResult(QrAuthResult.Fail("QR mode is planned but not implemented yet. Use AuthMode=business."));
    }

    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> IsConnectedAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<string> SendTextMessageAsync(string to, string content, CancellationToken ct = default)
        => throw new InvalidOperationException("WhatsApp QR transport is not connected/implemented");
}
