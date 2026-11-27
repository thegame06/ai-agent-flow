using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Api.Connect;

/// <summary>One conversation or campaign contact waiting for (or held by) a person.</summary>
public sealed record AttentionItem
{
    public string? SessionId { get; init; }
    public string? ChannelId { get; init; }
    public string? ContactId { get; init; }
    public string? CampaignId { get; init; }
    public string? CampaignName { get; init; }
    public string? PartyId { get; init; }
    public string Customer { get; init; } = string.Empty;
    public string Recipient { get; init; } = string.Empty;
    /// <summary>pending (waiting for a person), human (a person took it) or flagged (campaign contact, no live conversation).</summary>
    public string State { get; init; } = "flagged";
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset? Since { get; init; }
    public string? Assignee { get; init; }
    public string? LastCustomerMessage { get; init; }
    public int Unread { get; init; }
}

public sealed record ConversationMessageView(string Id, string Direction, string Actor, string Content, DateTimeOffset CreatedAt, string? Status);

public sealed record ConversationView
{
    public string SessionId { get; init; } = string.Empty;
    public string ChannelId { get; init; } = string.Empty;
    public string Recipient { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public string? Reason { get; init; }
    public string? Assignee { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
    public IReadOnlyList<ConversationMessageView> Messages { get; init; } = [];
    public string? ContactId { get; init; }
    public string? CampaignName { get; init; }
}

public sealed record HandoffReplyResult(bool Sent, string? Error);

public interface IConversationHandoffService
{
    Task<IReadOnlyList<AttentionItem>> QueueAsync(string tenantId, CancellationToken ct = default);
    Task<ConversationView?> GetAsync(string tenantId, string sessionId, CancellationToken ct = default);
    /// <summary>The assistant (or a rule) asks for a person; the assistant stops answering.</summary>
    Task<bool> RequestAsync(string tenantId, string sessionId, string reason, string requestedBy, CancellationToken ct = default);
    Task<bool> TakeAsync(string tenantId, string sessionId, string userId, CancellationToken ct = default);
    /// <summary>Gives the conversation back to the assistant and closes the campaign contact's attention flag.</summary>
    Task<bool> ReleaseAsync(string tenantId, string sessionId, CancellationToken ct = default);
    /// <summary>A person answers in the conversation (taking it if nobody had).</summary>
    Task<HandoffReplyResult> ReplyAsync(string tenantId, string sessionId, string userId, string text, CancellationToken ct = default);
    /// <summary>A person writes to a flagged campaign contact that has no live conversation yet.</summary>
    /// <summary>A note for the assistant when a person handled the conversation and gave it back.</summary>
    Task<string?> GetAssistantNoteAsync(string tenantId, string sessionId, CancellationToken ct = default);
    Task<HandoffReplyResult> ReplyToContactAsync(string tenantId, string contactId, string userId, string text, CancellationToken ct = default);
}

public sealed class ConversationHandoffService : IConversationHandoffService
{
    private readonly IChannelSessionRepository _sessions;
    private readonly IChannelMessageRepository _messages;
    private readonly IChannelGateway _gateway;
    private readonly ICampaignStore _campaigns;
    private readonly IOutboundMessageSender _outbound;

    public ConversationHandoffService(
        IChannelSessionRepository sessions,
        IChannelMessageRepository messages,
        IChannelGateway gateway,
        ICampaignStore campaigns,
        IOutboundMessageSender outbound)
    {
        _sessions = sessions;
        _messages = messages;
        _gateway = gateway;
        _campaigns = campaigns;
        _outbound = outbound;
    }

    public async Task<IReadOnlyList<AttentionItem>> QueueAsync(string tenantId, CancellationToken ct = default)
    {
        var (recent, _) = await _sessions.SearchAsync(tenantId, page: 0, pageSize: 500, ct: ct);
        var held = recent.Where(ConversationHandoff.AssistantPaused).ToList();
        var contacts = await _campaigns.GetContactsNeedingAttentionAsync(tenantId, 300, ct);
        var campaignNames = new Dictionary<string, string>();

        async Task<string?> CampaignName(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (!campaignNames.TryGetValue(id, out var name))
                campaignNames[id] = name = (await _campaigns.GetCampaignAsync(tenantId, id, ct))?.Name ?? "Campana";
            return name;
        }

        var items = new List<AttentionItem>();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in held)
        {
            var key = RecipientKeys.From(session.Identifier);
            var contact = key is null ? null : contacts.FirstOrDefault(c => c.RecipientKey == key);
            if (key is not null) covered.Add(key);
            items.Add(new AttentionItem
            {
                SessionId = session.Id,
                ChannelId = session.ChannelId,
                ContactId = contact?.Id,
                CampaignId = contact?.CampaignId,
                CampaignName = await CampaignName(contact?.CampaignId),
                PartyId = contact?.PartyId,
                Customer = contact?.DisplayName
                    ?? (session.Metadata.GetValueOrDefault("display_name") is { Length: > 0 } dn && dn != "Unknown" ? dn : session.Identifier),
                Recipient = session.Identifier,
                State = ConversationHandoff.State(session)!,
                Reason = session.Metadata.GetValueOrDefault(ConversationHandoff.ReasonKey) ?? contact?.AttentionReason ?? string.Empty,
                Since = ParseDate(session.Metadata.GetValueOrDefault(ConversationHandoff.RequestedAtKey)) ?? contact?.AttentionAt,
                Assignee = session.Metadata.GetValueOrDefault(ConversationHandoff.AssigneeKey),
                LastCustomerMessage = session.Metadata.GetValueOrDefault("last_customer_message") ?? contact?.LastReply,
                Unread = int.TryParse(session.Metadata.GetValueOrDefault("unread_count"), out var unread) ? unread : 0
            });
        }

        foreach (var contact in contacts.Where(c => c.RecipientKey is null || !covered.Contains(c.RecipientKey)))
        {
            var session = await FindSessionAsync(tenantId, contact.RecipientKey, recent);
            items.Add(new AttentionItem
            {
                SessionId = session?.Id,
                ChannelId = session?.ChannelId,
                ContactId = contact.Id,
                CampaignId = contact.CampaignId,
                CampaignName = await CampaignName(contact.CampaignId),
                PartyId = contact.PartyId,
                Customer = contact.DisplayName ?? contact.Recipient,
                Recipient = contact.Recipient,
                State = "flagged",
                Reason = contact.AttentionReason ?? string.Empty,
                Since = contact.AttentionAt ?? contact.UpdatedAt,
                LastCustomerMessage = contact.LastReply
            });
        }

        // Oldest first: whoever has waited longest is attended first.
        return items.OrderBy(i => i.State == "human" ? 1 : 0).ThenBy(i => i.Since ?? DateTimeOffset.MaxValue).ToList();
    }

    public async Task<ConversationView?> GetAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(sessionId, tenantId, ct);
        if (session is null) return null;

        var messages = await _messages.GetBySessionAsync(sessionId, tenantId, 100, ct);
        var key = RecipientKeys.From(session.Identifier);
        var contact = key is null ? null
            : await _campaigns.FindLatestContactByRecipientKeyAsync(tenantId, key, DateTimeOffset.UtcNow.AddDays(-30), ct);

        return new ConversationView
        {
            SessionId = session.Id,
            ChannelId = session.ChannelId,
            Recipient = session.Identifier,
            DisplayName = session.Metadata.GetValueOrDefault("display_name") is { Length: > 0 } dn && dn != "Unknown" ? dn : null,
            State = ConversationHandoff.State(session),
            Reason = session.Metadata.GetValueOrDefault(ConversationHandoff.ReasonKey),
            Assignee = session.Metadata.GetValueOrDefault(ConversationHandoff.AssigneeKey),
            LastActivityAt = session.LastActivityAt,
            ContactId = contact?.Id,
            CampaignName = contact is null ? null : (await _campaigns.GetCampaignAsync(tenantId, contact.CampaignId, ct))?.Name,
            Messages = messages
                .OrderBy(m => m.CreatedAt)
                .Select(m => new ConversationMessageView(
                    m.Id,
                    m.Direction == MessageDirection.Incoming ? "in" : "out",
                    m.Direction == MessageDirection.Incoming ? "customer" : (m.Metadata.GetValueOrDefault("actor") is "human" ? "human" : "assistant"),
                    m.Content,
                    m.CreatedAt,
                    m.Direction == MessageDirection.Outgoing && m.Status == MessageStatus.Failed ? "failed" : m.Metadata.GetValueOrDefault("agentflow.delivery")))
                .ToList()
        };
    }

    public async Task<bool> RequestAsync(string tenantId, string sessionId, string reason, string requestedBy, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(sessionId, tenantId, ct);
        if (session is null) return false;
        ConversationHandoff.Request(session, reason, requestedBy, DateTimeOffset.UtcNow);
        await _sessions.UpdateAsync(session, ct);
        return true;
    }

    public async Task<bool> TakeAsync(string tenantId, string sessionId, string userId, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(sessionId, tenantId, ct);
        if (session is null) return false;
        ConversationHandoff.Take(session, userId, DateTimeOffset.UtcNow);
        session.Metadata["unread_count"] = "0";
        await _sessions.UpdateAsync(session, ct);
        return true;
    }

    public async Task<bool> ReleaseAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(sessionId, tenantId, ct);
        if (session is null) return false;
        ConversationHandoff.Release(session, DateTimeOffset.UtcNow);
        session.Metadata["reply_pending"] = "false";
        await _sessions.UpdateAsync(session, ct);

        // The person handled it: the campaign contact no longer needs attention.
        var key = RecipientKeys.From(session.Identifier);
        var contact = key is null ? null
            : await _campaigns.FindLatestContactByRecipientKeyAsync(tenantId, key, DateTimeOffset.UtcNow.AddDays(-30), ct);
        if (contact is { NeedsAttention: true })
            await _campaigns.UpdateContactExecutionAsync(contact with { NeedsAttention = false, UpdatedAt = DateTimeOffset.UtcNow }, ct);
        return true;
    }

    public async Task<HandoffReplyResult> ReplyAsync(string tenantId, string sessionId, string userId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(false, "Escribe un mensaje.");
        var session = await _sessions.GetByIdAsync(sessionId, tenantId, ct);
        if (session is null) return new(false, "Conversacion no encontrada.");

        // Answering means owning the conversation: the assistant stops until it is given back.
        if (ConversationHandoff.State(session) != ConversationHandoff.WithPerson)
        {
            ConversationHandoff.Take(session, userId, DateTimeOffset.UtcNow);
            await _sessions.UpdateAsync(session, ct);
        }

        var outgoing = ChannelMessage.CreateOutgoing(tenantId, session.ChannelId, session.Id, session.Identifier, text.Trim());
        outgoing.Metadata["actor"] = "human";
        outgoing.Metadata["actor_user_id"] = userId;
        outgoing.Metadata["agentflow.delivery"] = "sent";
        var send = await _gateway.SendMessageAsync(session.ChannelId, outgoing, ct);
        if (send.Success)
        {
            session.Metadata["reply_pending"] = "false";
            session.Metadata["unread_count"] = "0";
            session.Metadata["last_agent_message"] = outgoing.Content;
            session.Metadata[ConversationHandoff.LastAdvisorMessageKey] = outgoing.Content;
            await _sessions.UpdateAsync(session, ct);
        }
        return new(send.Success, send.Success ? null : send.Error ?? "No se pudo enviar el mensaje.");
    }

    public async Task<string?> GetAssistantNoteAsync(string tenantId, string sessionId, CancellationToken ct = default)
        => ConversationHandoff.AssistantNote(await _sessions.GetByIdAsync(sessionId, tenantId, ct), DateTimeOffset.UtcNow);

    public async Task<HandoffReplyResult> ReplyToContactAsync(string tenantId, string contactId, string userId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(false, "Escribe un mensaje.");
        var contact = await _campaigns.GetContactExecutionAsync(tenantId, contactId, ct);
        if (contact is null) return new(false, "Contacto no encontrado.");
        var campaign = await _campaigns.GetCampaignAsync(tenantId, contact.CampaignId, ct);

        var result = await _outbound.SendAsync(tenantId, campaign?.Channel ?? contact.Channel, contact.Recipient, text.Trim(), userId,
            contact.CampaignId, null, new Dictionary<string, string> { ["actor"] = "human", ["campaign_contact_execution_id"] = contact.Id }, ct);
        return new(result.Sent, result.Sent ? null : result.Error ?? "No se pudo enviar el mensaje.");
    }

    private async Task<ChannelSession?> FindSessionAsync(string tenantId, string? recipientKey, IReadOnlyList<ChannelSession> recent)
    {
        if (recipientKey is null) return null;
        await Task.CompletedTask;
        return recent.Where(s => RecipientKeys.From(s.Identifier) == recipientKey)
            .OrderByDescending(s => s.LastActivityAt).FirstOrDefault();
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var at) ? at : null;
}
