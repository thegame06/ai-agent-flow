namespace AgentFlow.Application.Channels;

/// <summary>
/// Optional capability for channel handlers that can expose a QR code for authentication.
/// </summary>
public interface IChannelQrProvider
{
    Task<string?> GetQrCodeAsync(CancellationToken ct = default);
}
