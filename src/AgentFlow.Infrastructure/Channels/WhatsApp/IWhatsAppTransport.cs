namespace AgentFlow.Infrastructure.Channels.WhatsApp;

public interface IWhatsAppTransport
{
    string Mode { get; }
    Task<QrAuthResult> ConnectAsync(string channelId, string? apiToken, string? phoneNumberId, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task<bool> IsConnectedAsync(CancellationToken ct = default);
    Task<string> SendTextMessageAsync(string to, string content, CancellationToken ct = default);

    /// <summary>
    /// Sends a pre-approved template message to re-open a closed conversation window.
    /// Required by WhatsApp Business policy when the 24-hour window has expired.
    /// </summary>
    Task<string> SendTemplateMessageAsync(string to, string templateName, CancellationToken ct = default);
}
