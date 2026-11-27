using AgentFlow.Abstractions.Connect;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Api.Connect;

/// <summary>Result of an outbound send. <c>Sent</c> only when the channel transport accepted it.</summary>
public sealed record OutboundSendResult(
    bool Sent,
    string? ChannelId,
    string? ChannelMessageId,
    string? InboxMessageId,
    string? Error);

/// <summary>
/// Sends a business-initiated message (campaign, workflow) through a real channel and keeps an
/// operational record in the connect inbox with the true outcome (Sent / Failed + error).
/// </summary>
public interface IOutboundMessageSender
{
    /// <param name="channel">A channel id, or a channel type ("whatsapp", "api", ...) resolved to the tenant's first active channel of that type.</param>
    Task<OutboundSendResult> SendAsync(
        string tenantId,
        string channel,
        string recipient,
        string content,
        string requestedBy,
        string? campaignId = null,
        string? templateId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);
}

public sealed class OutboundMessageSender : IOutboundMessageSender
{
    private readonly IChannelDefinitionRepository _channels;
    private readonly IChannelGateway _gateway;
    private readonly IConnectStore _connectStore;

    public OutboundMessageSender(IChannelDefinitionRepository channels, IChannelGateway gateway, IConnectStore connectStore)
    {
        _channels = channels;
        _gateway = gateway;
        _connectStore = connectStore;
    }

    public async Task<OutboundSendResult> SendAsync(
        string tenantId,
        string channel,
        string recipient,
        string content,
        string requestedBy,
        string? campaignId = null,
        string? templateId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        string? error = null;
        string? channelId = null;
        string? channelMessageId = null;

        if (string.IsNullOrWhiteSpace(recipient) || recipient.Contains("{{", StringComparison.Ordinal))
        {
            // An unresolved template token is never a real address; fail loudly instead of "sending".
            error = $"invalid_recipient:{recipient}";
        }
        else
        {
            var definition = await ResolveChannelAsync(tenantId, channel, ct);
            if (definition is null)
            {
                error = $"no_active_channel:{channel}";
            }
            else
            {
                channelId = definition.Id;
                var outgoing = ChannelMessage.CreateOutgoing(
                    tenantId: tenantId,
                    channelId: definition.Id,
                    sessionId: $"outbound-{Guid.NewGuid():N}",
                    to: recipient,
                    content: content);
                outgoing.Metadata["origin"] = campaignId is null ? "workflow" : "campaign";
                if (campaignId is not null) outgoing.Metadata["campaign_id"] = campaignId;
                if (templateId is not null) outgoing.Metadata["template_id"] = templateId;
                if (metadata is not null)
                {
                    foreach (var (key, value) in metadata) outgoing.Metadata[key] = value;
                }

                try
                {
                    var result = await _gateway.SendMessageAsync(definition.Id, outgoing, ct);
                    channelMessageId = outgoing.Id;
                    if (!result.Success) error = result.Error ?? "send_failed";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    error = ex.Message;
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        var record = await _connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            Channel = channel,
            Recipient = recipient,
            Content = content,
            CampaignId = campaignId,
            TemplateId = templateId,
            Status = error is null ? ConnectOperationalStatus.Sent : ConnectOperationalStatus.Failed,
            LastError = error,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = requestedBy
        }, ct);

        return new OutboundSendResult(error is null, channelId, channelMessageId, record.Id, error);
    }

    private async Task<ChannelDefinition?> ResolveChannelAsync(string tenantId, string channel, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channel)) return null;

        var byId = await _channels.GetByIdAsync(channel, tenantId, ct);
        if (byId is not null) return byId.Status == ChannelStatus.Active ? byId : null;

        if (!Enum.TryParse<ChannelType>(channel, ignoreCase: true, out var type)) return null;
        var ofType = await _channels.GetByTypeAsync(type, tenantId, ct);
        return ofType.FirstOrDefault(x => x.Status == ChannelStatus.Active);
    }
}
