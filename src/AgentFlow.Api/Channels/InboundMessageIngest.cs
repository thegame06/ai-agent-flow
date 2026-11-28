using System.Text.Json;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Connect;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Api.Channels;

public enum InboundStatus { Processed, Accepted, Duplicate, Ignored, Failed }

/// <summary>What happened to a customer message; <see cref="Reply"/> is set when it was processed inline.</summary>
public sealed record InboundResult(
    InboundStatus Status,
    ChannelSession? Session = null,
    ChannelMessage? Incoming = null,
    ChannelMessage? Reply = null,
    string? InboxMessageId = null,
    string? Reason = null);

public interface IInboundMessageIngest
{
    /// <summary>
    /// Takes a customer message through the one pipeline every channel shares. With
    /// <paramref name="inBackground"/> it answers as soon as the message is recorded and the assistant
    /// replies on its own; otherwise it waits for the reply.
    /// </summary>
    Task<InboundResult> IngestAsync(InboundMessage message, bool inBackground = false, CancellationToken ct = default);
}

/// <summary>
/// The single path from "a customer wrote" to "the assistant answered", the same for WhatsApp (QR,
/// Meta, Twilio), the API, WebChat and generic webhooks:
/// canonical address → dedupe → registered customer → session (sliding window) → message with its
/// provenance and attachments → inbox record → campaign attribution → gateway.
/// Only receiving (auth, payload parsing) and sending stay provider-specific.
/// </summary>
public sealed class InboundMessageIngest : IInboundMessageIngest
{
    private readonly IChannelGateway _gateway;
    private readonly IChannelSessionRepository _sessions;
    private readonly IConnectStore _connectStore;
    private readonly ICustomerResolver _customers;
    private readonly ICampaignOutcomeService? _campaignOutcomes;
    private readonly IServiceScopeFactory? _scopes;
    private readonly ILogger<InboundMessageIngest> _logger;
    private readonly string? _defaultCountryCode;

    public InboundMessageIngest(
        IChannelGateway gateway,
        IChannelSessionRepository sessions,
        IConnectStore connectStore,
        ICustomerResolver customers,
        ILogger<InboundMessageIngest> logger,
        IConfiguration? configuration = null,
        ICampaignOutcomeService? campaignOutcomes = null,
        IServiceScopeFactory? scopes = null)
    {
        _gateway = gateway;
        _sessions = sessions;
        _connectStore = connectStore;
        _customers = customers;
        _logger = logger;
        _campaignOutcomes = campaignOutcomes;
        _scopes = scopes;
        _defaultCountryCode = configuration?["Messaging:DefaultCountryCode"];
    }

    public async Task<InboundResult> IngestAsync(InboundMessage message, bool inBackground = false, CancellationToken ct = default)
    {
        var channel = message.Channel;
        var address = CustomerAddress.Normalize(message.From, channel.Type,
            channel.Config.GetValueOrDefault("DefaultCountryCode") ?? _defaultCountryCode);
        if (address.Value.Length == 0)
            return new(InboundStatus.Ignored, Reason: "Message without a sender.");

        var text = ComposeText(message.Text, message.Attachments);
        if (text.Length == 0)
            return new(InboundStatus.Ignored, Reason: "Message without text or attachments.");

        // A provider retry (timeouts, redeliveries) must not be answered twice.
        var externalKey = ExternalKey(message, address, text);
        var existing = await _connectStore.GetInboxMessageByExternalKeyAsync(message.TenantId, externalKey, ct);
        if (existing is not null)
            return new(InboundStatus.Duplicate, InboxMessageId: existing.Id);

        var customer = await TryResolveAsync(message.TenantId, address, ct);
        var displayName = customer?.Name ?? Clean(message.DisplayName);

        var handler = _gateway.GetHandler(channel.Type)
            ?? throw new InvalidOperationException($"No handler for channel type {channel.Type}.");
        await AdoptLegacySessionAsync(message, address, ct);
        var prior = await _sessions.GetByChannelAndIdentifierAsync(channel.Id, address.Value, message.TenantId, ct);
        var context = ChannelContext.Create(channel.Type, channel.Id, message.CorrelationId ?? Guid.NewGuid().ToString("N"), address.Value, displayName);
        var session = await handler.GetOrCreateSessionAsync(context, channel, ct);
        CarryOverHandoff(prior, session);

        var provider = message.Provider.Trim().ToLowerInvariant();
        StampSession(session, channel, provider, address, displayName, customer, message.BusinessAddress);
        session.RecordIncomingMessage(text);
        session.ExtendExpiration(TimeSpan.FromHours(channel.SessionWindowHours));
        await _sessions.UpdateAsync(session, ct);

        var incoming = ChannelMessage.CreateIncoming(message.TenantId, channel.Id, session.Id, address.Value, text,
            message.RawPayload, message.ExternalId);
        StampMessage(incoming, channel, provider, address, message, customer);

        var inbox = await _connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = message.TenantId,
            Channel = channel.Type.ToString().ToLowerInvariant(),
            Recipient = address.Value,
            Content = text,
            ExternalEventKey = externalKey,
            Status = ConnectOperationalStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = $"{provider}-ingest"
        }, ct);

        await AttributeAsync(message.TenantId, address.Value, text, channel, ct);

        if (inBackground && _scopes is not null)
        {
            _ = Task.Run(() => ProcessInScopeAsync(incoming), CancellationToken.None);
            return new(InboundStatus.Accepted, session, incoming, InboxMessageId: inbox.Id);
        }

        // The reply goes out through the provider; a caller giving up (bridge or Twilio timeouts)
        // must not cancel the assistant halfway.
        var reply = await _gateway.ProcessMessageAsync(incoming, CancellationToken.None);
        var failed = reply.Direction == MessageDirection.Incoming && reply.Status == MessageStatus.Failed;
        return new(failed ? InboundStatus.Failed : InboundStatus.Processed, session, incoming, reply, inbox.Id, failed ? reply.ErrorMessage : null);
    }

    /// <summary>The text the assistant reads: what the customer wrote, plus a line per file they sent.</summary>
    public static string ComposeText(string? text, IReadOnlyList<InboundAttachment> attachments)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(text)) lines.Add(text.Trim());
        foreach (var a in attachments)
            lines.Add($"[El cliente envió {KindLabel(a.Kind)}{(string.IsNullOrWhiteSpace(a.FileName) ? string.Empty : $" ({a.FileName})")}]");
        return string.Join("\n", lines);
    }

    private static string KindLabel(string kind) => kind.ToLowerInvariant() switch
    {
        "image" => "una imagen",
        "document" => "un documento",
        "audio" or "voice" or "ptt" => "un audio",
        "video" => "un video",
        "sticker" => "un sticker",
        _ => "un archivo"
    };

    private static string ExternalKey(InboundMessage message, CustomerAddress address, string text)
        => !string.IsNullOrWhiteSpace(message.ExternalId)
            ? $"{message.Provider.ToLowerInvariant()}:{message.ExternalId}"
            : $"{message.Provider.ToLowerInvariant()}:{address.Value}:{message.SentAt?.ToUnixTimeSeconds()}:{text}";

    private async Task<ResolvedCustomer?> TryResolveAsync(string tenantId, CustomerAddress address, CancellationToken ct)
    {
        try { return await _customers.ResolveAsync(tenantId, address, ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not look up the registered customer for {Address}", address.Value);
            return null;
        }
    }

    /// <summary>
    /// A person who owns the customer's conversation keeps it when a new session opens (the old one
    /// expired): the assistant must not start answering a customer an advisor still has.
    /// </summary>
    public static void CarryOverHandoff(ChannelSession? prior, ChannelSession session)
    {
        if (prior is null || prior.Id == session.Id || !ConversationHandoff.AssistantPaused(prior) ||
            ConversationHandoff.AssistantPaused(session)) return;
        foreach (var (key, value) in prior.Metadata.Where(kv => kv.Key.StartsWith("handoff.", StringComparison.Ordinal)).ToList())
            session.Metadata[key] = value;
    }

    /// <summary>A conversation opened before addresses were canonical keeps going under the canonical address.</summary>
    private async Task AdoptLegacySessionAsync(InboundMessage message, CustomerAddress address, CancellationToken ct)
    {
        if (await _sessions.GetByChannelAndIdentifierAsync(message.Channel.Id, address.Value, message.TenantId, ct) is not null)
            return;
        foreach (var legacy in CustomerAddress.LegacyForms(message.From, address))
        {
            var old = await _sessions.GetByChannelAndIdentifierAsync(message.Channel.Id, legacy, message.TenantId, ct);
            if (old is null || old.IsExpired()) continue;
            old.UseIdentifier(address.Value);
            await _sessions.UpdateAsync(old, ct);
            return;
        }
    }

    private static void StampSession(ChannelSession session, ChannelDefinition channel, string provider, CustomerAddress address,
        string? displayName, ResolvedCustomer? customer, string? businessAddress)
    {
        session.Metadata[Provenance.ProviderKey] = provider;
        session.Metadata[Provenance.ChannelTypeKey] = channel.Type.ToString();
        session.Metadata[Provenance.ChannelNameKey] = channel.Name;
        session.Metadata[Provenance.CustomerAddressKey] = address.Value;
        if (!string.IsNullOrWhiteSpace(businessAddress)) session.Metadata[Provenance.BusinessAddressKey] = businessAddress;
        if (address.Unresolved) session.Metadata[Provenance.CustomerUnresolvedKey] = "true";
        // The registered customer's name wins over whatever the app shows ("Unknown", a nickname).
        if (!string.IsNullOrWhiteSpace(displayName)) session.Metadata[Provenance.DisplayNameKey] = displayName;
        if (customer is not null)
        {
            session.Metadata[Provenance.PartyIdKey] = customer.PartyId;
            if (!string.IsNullOrWhiteSpace(customer.Name)) session.Metadata[Provenance.PartyNameKey] = customer.Name!;
        }
    }

    private static void StampMessage(ChannelMessage incoming, ChannelDefinition channel, string provider, CustomerAddress address,
        InboundMessage message, ResolvedCustomer? customer)
    {
        incoming.Metadata[Provenance.ProviderKey] = provider;
        incoming.Metadata[Provenance.ChannelTypeKey] = channel.Type.ToString();
        incoming.Metadata[Provenance.ChannelNameKey] = channel.Name;
        incoming.Metadata[Provenance.CustomerAddressKey] = address.Value;
        incoming.Metadata[Provenance.CustomerRawKey] = message.From;
        incoming.Metadata["actor"] = "customer";
        if (!string.IsNullOrWhiteSpace(message.BusinessAddress)) incoming.Metadata[Provenance.BusinessAddressKey] = message.BusinessAddress!;
        if (!string.IsNullOrWhiteSpace(message.CorrelationId)) incoming.Metadata["correlation_id"] = message.CorrelationId!;
        if (customer is not null) incoming.Metadata[Provenance.PartyIdKey] = customer.PartyId;
        if (message.Attachments.Count > 0) incoming.Metadata[Provenance.AttachmentsKey] = JsonSerializer.Serialize(message.Attachments);
        if (message.Metadata is not null)
            foreach (var kv in message.Metadata)
                incoming.Metadata.TryAdd(kv.Key, kv.Value);
    }

    private async Task AttributeAsync(string tenantId, string address, string text, ChannelDefinition channel, CancellationToken ct)
    {
        if (_campaignOutcomes is null) return;
        try
        {
            var answeredByAssistant = !string.IsNullOrWhiteSpace(channel.RouterAgentId) || !string.IsNullOrWhiteSpace(channel.Config.GetValueOrDefault("DefaultAgentId"));
            var contact = await _campaignOutcomes.AttributeReplyAsync(tenantId, address, text, DateTimeOffset.UtcNow, ct, answeredByAssistant);
            if (contact is not null)
                _logger.LogInformation("Reply attributed to campaign {CampaignId} contact {ContactId}: {Outcome}", contact.CampaignId, contact.Id, contact.Outcome);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not attribute the customer message to a campaign contact");
        }
    }

    private async Task ProcessInScopeAsync(ChannelMessage incoming)
    {
        try
        {
            using var scope = _scopes!.CreateScope();
            var gateway = scope.ServiceProvider.GetRequiredService<IChannelGateway>();
            await gateway.ProcessMessageAsync(incoming, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background processing failed for message {MessageId}", incoming.Id);
        }
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Trim().Equals("unknown", StringComparison.OrdinalIgnoreCase) ? null : value.Trim();
}
