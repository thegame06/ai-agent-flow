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
    private readonly IMediaStore? _media;
    private readonly AgentFlow.Api.Collections.ICollectionsAssistantService? _receipts;
    private readonly InboundBatcher? _batcher;
    private readonly IChannelMessageRepository? _messages;
    private readonly TimeSpan _batchWindow;

    /// <summary>How long to wait for more messages from the customer before answering them together.</summary>
    public static readonly TimeSpan DefaultBatchWindow = TimeSpan.FromSeconds(2);
    private readonly IReadOnlyList<IInboundMediaFetcher> _fetchers;
    private readonly ILogger<InboundMessageIngest> _logger;
    private readonly string? _defaultCountryCode;
    private readonly TimeSpan _restartAfter;

    /// <summary>After this long without messages the assistant starts a fresh conversation.</summary>
    public static readonly TimeSpan DefaultRestartAfter = TimeSpan.FromHours(12);

    public InboundMessageIngest(
        IChannelGateway gateway,
        IChannelSessionRepository sessions,
        IConnectStore connectStore,
        ICustomerResolver customers,
        ILogger<InboundMessageIngest> logger,
        IConfiguration? configuration = null,
        ICampaignOutcomeService? campaignOutcomes = null,
        IServiceScopeFactory? scopes = null,
        IMediaStore? media = null,
        IEnumerable<IInboundMediaFetcher>? fetchers = null,
        AgentFlow.Api.Collections.ICollectionsAssistantService? receipts = null,
        InboundBatcher? batcher = null,
        IChannelMessageRepository? messages = null)
    {
        _receipts = receipts;
        _batcher = batcher;
        _messages = messages;
        _batchWindow = int.TryParse(configuration?["Messaging:BatchWindowMs"], out var ms) && ms >= 0
            ? TimeSpan.FromMilliseconds(ms)
            : DefaultBatchWindow;
        _media = media;
        _fetchers = fetchers?.ToList() ?? [];
        _gateway = gateway;
        _sessions = sessions;
        _connectStore = connectStore;
        _customers = customers;
        _logger = logger;
        _campaignOutcomes = campaignOutcomes;
        _scopes = scopes;
        _defaultCountryCode = configuration?["Messaging:DefaultCountryCode"];
        _restartAfter = double.TryParse(configuration?["Messaging:ConversationRestartHours"], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var hours) && hours > 0
            ? TimeSpan.FromHours(hours)
            : DefaultRestartAfter;
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
        var restarted = RestartIfIdle(session, _restartAfter, DateTimeOffset.UtcNow);
        if (!address.Unresolved)
        {
            // One conversation per customer: if they were just talking on another channel, the
            // assistant continues that conversation here (same thread, same advisor hand-off).
            var others = await _sessions.GetActiveByUserAsync(address.Value, message.TenantId, ct) ?? [];
            if (ConversationToFollow(session, others, _restartAfter, DateTimeOffset.UtcNow) is { } latest)
            {
                CarryOverHandoff(latest, session);
                if (!string.IsNullOrWhiteSpace(latest.ThreadId) && latest.ThreadId != session.ThreadId)
                    session.LinkThread(latest.ThreadId!);
            }
        }

        var provider = message.Provider.Trim().ToLowerInvariant();
        message = message with { Attachments = await StoreAttachmentsAsync(message, provider, ct) };
        StampSession(session, channel, provider, address, displayName, customer, message.BusinessAddress);
        var stored = message.Attachments
            .Where(a => a.MediaId is not null && a.Kind is "image" or "document")
            .Select(a => a.MediaId!).ToList();
        var attached = stored.Count > 0 && customer is not null && _receipts is not null &&
                       await TryAttachReceiptsAsync(message.TenantId, customer.PartyId, stored, ct);
        if (stored.Count > 0 && !attached)
        {
            // The receipt the customer just sent, for the payment report that usually follows.
            session.Metadata[Provenance.LastMediaIdsKey] = string.Join(',', stored);
            session.Metadata[Provenance.LastMediaAtKey] = DateTimeOffset.UtcNow.ToString("O");
        }
        session.RecordIncomingMessage(text);
        session.ExtendExpiration(TimeSpan.FromHours(channel.SessionWindowHours));
        await _sessions.UpdateAsync(session, ct);

        var incoming = ChannelMessage.CreateIncoming(message.TenantId, channel.Id, session.Id, address.Value, text,
            message.RawPayload, message.ExternalId);
        StampMessage(incoming, channel, provider, address, message, customer);
        if (restarted) incoming.Metadata["conversation.restarted"] = "true";

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

        // The customer's one conversation: bursts are batched and turns serialized per customer, not per channel.
        var customerKey = address.Unresolved ? session.Id : $"{message.TenantId}:{address.Value}";

        // Messages sent seconds apart are answered once, together. A phone turn is already one
        // utterance and the caller is waiting on the line, so calls are answered at once.
        if (!inBackground && _batcher is not null && _batchWindow > TimeSpan.Zero && channel.Type != ChannelType.Voice)
        {
            var version = _batcher.Add(customerKey, incoming);
            await Task.Delay(_batchWindow, CancellationToken.None);
            var batch = _batcher.TakeIfLatest(customerKey, version);
            if (batch is null)
            {
                // A newer message will answer this one too; keep it in the conversation as received.
                incoming.Status = MessageStatus.Received;
                incoming.Metadata["agentflow.batched"] = "true";
                if (_messages is not null) await _messages.InsertAsync(incoming, CancellationToken.None);
                return new(InboundStatus.Accepted, session, incoming, InboxMessageId: inbox.Id, Reason: "Se responde junto con el siguiente mensaje.");
            }
            incoming = InboundBatcher.Combine(batch);
        }

        if (inBackground && _scopes is not null)
        {
            _ = Task.Run(() => ProcessInScopeAsync(incoming, customerKey), CancellationToken.None);
            return new(InboundStatus.Accepted, session, incoming, InboxMessageId: inbox.Id);
        }

        // The reply goes out through the provider; a caller giving up (bridge or Twilio timeouts)
        // must not cancel the assistant halfway.
        ChannelMessage reply;
        using (_batcher is null ? null : await _batcher.TurnAsync(customerKey))
            reply = await _gateway.ProcessMessageAsync(incoming, CancellationToken.None);
        var failed = reply.Direction == MessageDirection.Incoming && reply.Status == MessageStatus.Failed;
        return new(failed ? InboundStatus.Failed : InboundStatus.Processed, session, incoming, reply, inbox.Id, failed ? reply.ErrorMessage : null);
    }

    /// <summary>
    /// Keeps a copy of every file the customer sent (the provider's own copy expires or needs its
    /// credentials). A file that cannot be fetched is still recorded as sent, without a copy.
    /// </summary>
    private async Task<IReadOnlyList<InboundAttachment>> StoreAttachmentsAsync(InboundMessage message, string provider, CancellationToken ct)
    {
        if (_media is null || message.Attachments.Count == 0) return message.Attachments;
        var result = new List<InboundAttachment>(message.Attachments.Count);
        foreach (var attachment in message.Attachments)
        {
            try
            {
                var file = attachment.Data is { Length: > 0 } data
                    ? (data, attachment.MimeType ?? "application/octet-stream")
                    : _fetchers.FirstOrDefault(f => f.Handles(provider)) is { } fetcher
                        ? await fetcher.FetchAsync(message.TenantId, message.Channel, attachment, ct)
                        : null;
                if (file is { } f && f.Item1.Length > 0)
                {
                    var saved = await _media.SaveAsync(message.TenantId, f.Item1, f.Item2, attachment.FileName, provider, ct);
                    result.Add(attachment with { MediaId = saved.Id, MimeType = saved.ContentType, FileName = saved.FileName, Data = null });
                    continue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not keep a copy of a file sent through {Provider}", provider);
            }
            result.Add(attachment with { Data = null });
        }
        return result;
    }

    private async Task<bool> TryAttachReceiptsAsync(string tenantId, string partyId, IReadOnlyList<string> mediaIds, CancellationToken ct)
    {
        try { return await _receipts!.AttachReceiptsAsync(tenantId, partyId, mediaIds, ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not attach the customer's file to a pending payment report");
            return false;
        }
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

    /// <summary>
    /// What identifies a delivery for deduplication: the provider's message id, else the caller's
    /// correlation id, else the send time. Without any of them a message is never treated as a
    /// duplicate: the same customer writing the same "hola" twice is two messages.
    /// </summary>
    public static string ExternalKey(InboundMessage message, CustomerAddress address, string text)
    {
        var provider = message.Provider.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(message.ExternalId)) return $"{provider}:{message.ExternalId}";
        if (!string.IsNullOrWhiteSpace(message.CorrelationId)) return $"{provider}:corr:{message.CorrelationId}";
        if (message.SentAt is { } sentAt) return $"{provider}:{address.Value}:{sentAt.ToUnixTimeSeconds()}:{text}";
        return $"{provider}:once:{Guid.NewGuid():N}";
    }

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

    /// <summary>
    /// A customer writing again after many hours starts a fresh conversation for the assistant: the old
    /// thread made it pick up where it left off and repeat its last action (re-reporting a payment on a
    /// later "hola"). The customer's state (balance, promise, payments) still comes from its tools.
    /// A conversation a person owns is left alone.
    /// </summary>
    public static bool RestartIfIdle(ChannelSession session, TimeSpan restartAfter, DateTimeOffset now)
    {
        if (session.MessageCount == 0 || string.IsNullOrWhiteSpace(session.ThreadId) ||
            ConversationHandoff.AssistantPaused(session) || now - session.LastActivityAt < restartAfter)
            return false;
        session.UnlinkThread();
        session.Metadata["conversation.restarted_at"] = now.ToString("O");
        return true;
    }

    /// <summary>
    /// The customer's conversation on another channel that this message continues: their most recent
    /// session elsewhere, active within the restart window and more recent than this channel's own.
    /// </summary>
    public static ChannelSession? ConversationToFollow(ChannelSession session, IEnumerable<ChannelSession> others, TimeSpan restartAfter, DateTimeOffset now)
    {
        var latest = others
            .Where(o => o.Id != session.Id && o.TenantId == session.TenantId && now - o.LastActivityAt < restartAfter)
            .OrderByDescending(o => o.LastActivityAt)
            .FirstOrDefault();
        if (latest is null) return null;
        var mine = session.MessageCount == 0 ? DateTimeOffset.MinValue : session.LastActivityAt;
        return latest.LastActivityAt > mine ? latest : null;
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

    private async Task ProcessInScopeAsync(ChannelMessage incoming, string customerKey)
    {
        try
        {
            using var turn = _batcher is null ? null : await _batcher.TurnAsync(customerKey);
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
