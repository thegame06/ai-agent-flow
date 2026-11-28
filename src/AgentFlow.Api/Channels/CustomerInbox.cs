using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Api.Channels;

/// <summary>One channel a customer has written through: which number or channel, and which provider.</summary>
public sealed record InboxChannelRef(
    string ChannelId,
    string ChannelName,
    string ChannelType,
    string Provider,
    string? BusinessAddress,
    string SessionId,
    DateTimeOffset LastActivityAt,
    bool ChannelDeleted);

/// <summary>A customer in the inbox: one row per person, whatever channels they used.</summary>
public sealed record InboxCustomer
{
    public required string Key { get; init; }
    public string? PartyId { get; init; }
    public required string Name { get; init; }
    public bool Registered { get; init; }
    public required string Address { get; init; }
    public IReadOnlyList<InboxChannelRef> Channels { get; init; } = [];
    public string? LastMessage { get; init; }
    public string? LastDirection { get; init; }
    public InboxChannelRef? LastChannel { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
    public int Unread { get; init; }
    public bool ReplyPending { get; init; }
    /// <summary>pending / human while a person owns the conversation; null with the assistant.</summary>
    public string? Handoff { get; init; }
    /// <summary>Only sessions of deleted channels (tests, probes): hidden unless asked for.</summary>
    public bool OnlyDeletedChannels { get; init; }
}

/// <summary>One message in a customer's merged timeline, with the channel it went through.</summary>
public sealed record InboxTimelineItem(
    string Id,
    string Direction,
    string Actor,
    string Content,
    DateTimeOffset CreatedAt,
    string Status,
    string? Delivery,
    string? Error,
    string ChannelId,
    string ChannelName,
    string Provider,
    string SessionId,
    string? Attachments);

public sealed record InboxFilter(string? Query = null, string? ChannelId = null, string? Provider = null, bool NeedsAttention = false, bool IncludeDeletedChannels = false);

public interface ICustomerInbox
{
    Task<(IReadOnlyList<InboxCustomer> Items, int Total)> ListAsync(string tenantId, InboxFilter filter, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<InboxTimelineItem>> TimelineAsync(string tenantId, string customerKey, int limit, bool includeDeletedChannels = false, CancellationToken ct = default);
}

/// <summary>
/// The inbox by customer: sessions from every channel are grouped by the person behind them (the
/// registered customer, else the canonical address), so the same customer writing by QR, Twilio and
/// the API is one row with a merged timeline that says where each message came in or went out.
/// </summary>
public sealed class CustomerInbox : ICustomerInbox
{
    private const int MaxSessions = 500;
    private readonly IChannelSessionRepository _sessions;
    private readonly IChannelMessageRepository _messages;
    private readonly IChannelDefinitionRepository _channels;
    private readonly ICustomerResolver? _customers;

    public CustomerInbox(IChannelSessionRepository sessions, IChannelMessageRepository messages, IChannelDefinitionRepository channels, ICustomerResolver? customers = null)
    {
        _sessions = sessions;
        _messages = messages;
        _channels = channels;
        _customers = customers;
    }

    public async Task<(IReadOnlyList<InboxCustomer> Items, int Total)> ListAsync(string tenantId, InboxFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var customers = await LoadAsync(tenantId, ct);
        var filtered = Filter(customers, filter).ToList();
        pageSize = Math.Clamp(pageSize, 1, 100);
        return (filtered.Skip(Math.Max(0, page) * pageSize).Take(pageSize).ToList(), filtered.Count);
    }

    public async Task<IReadOnlyList<InboxTimelineItem>> TimelineAsync(string tenantId, string customerKey, int limit, bool includeDeletedChannels = false, CancellationToken ct = default)
    {
        var customer = (await LoadAsync(tenantId, ct)).FirstOrDefault(c => c.Key == customerKey);
        if (customer is null) return [];

        var items = new List<InboxTimelineItem>();
        foreach (var channel in customer.Channels.Where(c => includeDeletedChannels || !c.ChannelDeleted || customer.OnlyDeletedChannels))
        {
            var messages = await _messages.GetBySessionAsync(channel.SessionId, tenantId, Math.Clamp(limit, 1, 500), ct);
            items.AddRange(messages.Select(m => TimelineItem(m, channel)));
        }
        return items.OrderBy(i => i.CreatedAt).TakeLast(Math.Clamp(limit, 1, 500)).ToList();
    }

    private async Task<IReadOnlyList<InboxCustomer>> LoadAsync(string tenantId, CancellationToken ct)
    {
        var sessions = new List<ChannelSession>();
        for (var page = 0; sessions.Count < MaxSessions; page++)
        {
            var (items, total) = await _sessions.SearchAsync(tenantId, page: page, pageSize: 100, ct: ct);
            sessions.AddRange(items);
            if (items.Count < 100 || sessions.Count >= total) break;
        }

        var channels = new Dictionary<string, ChannelDefinition>();
        foreach (var id in sessions.Select(s => s.ChannelId).Distinct())
            if (await _channels.GetByIdAsync(id, tenantId, ct) is { } channel)
                channels[id] = channel;

        // Sessions opened before customers were linked: look the person up once per address.
        var resolved = new Dictionary<string, ResolvedCustomer?>();
        if (_customers is not null)
        {
            foreach (var session in sessions.Where(s => string.IsNullOrWhiteSpace(s.Metadata.GetValueOrDefault(Provenance.PartyIdKey))))
            {
                var address = AddressOf(session, channels.GetValueOrDefault(session.ChannelId));
                if (resolved.ContainsKey(address.Value)) continue;
                try { resolved[address.Value] = await _customers.ResolveAsync(tenantId, address, ct); }
                catch { resolved[address.Value] = null; }
            }
        }

        return Group(sessions, channels, resolved);
    }

    /// <summary>Groups sessions into customers. Pure, so the grouping rules are testable.</summary>
    public static IReadOnlyList<InboxCustomer> Group(
        IEnumerable<ChannelSession> sessions,
        IReadOnlyDictionary<string, ChannelDefinition> channels,
        IReadOnlyDictionary<string, ResolvedCustomer?>? resolved = null)
    {
        var rows = sessions.Select(s =>
        {
            var channel = channels.GetValueOrDefault(s.ChannelId);
            var address = AddressOf(s, channel);
            var partyId = s.Metadata.GetValueOrDefault(Provenance.PartyIdKey) is { Length: > 0 } p ? p : resolved?.GetValueOrDefault(address.Value)?.PartyId;
            var partyName = s.Metadata.GetValueOrDefault(Provenance.PartyNameKey) is { Length: > 0 } n ? n : resolved?.GetValueOrDefault(address.Value)?.Name;
            return (Session: s, Channel: channel, Address: address, PartyId: partyId, PartyName: partyName);
        }).ToList();

        return rows
            .GroupBy(r => r.PartyId is not null ? $"party:{r.PartyId}" : $"addr:{r.Address.Value}")
            .Select(g =>
            {
                var ordered = g.OrderByDescending(r => r.Session.LastActivityAt).ToList();
                var latest = ordered[0];
                var refs = ordered
                    .GroupBy(r => r.Session.ChannelId)
                    .Select(c => c.First())
                    .Select(r => ChannelRef(r.Session, r.Channel))
                    .ToList();
                var name = ordered.Select(r => r.PartyName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                           ?? ordered.Select(r => r.Session.Metadata.GetValueOrDefault(Provenance.DisplayNameKey)).FirstOrDefault(IsRealName)
                           ?? latest.Address.Value;
                var handoff = ordered.Select(r => ConversationHandoff.State(r.Session))
                    .FirstOrDefault(x => x is ConversationHandoff.Pending or ConversationHandoff.WithPerson);
                return new InboxCustomer
                {
                    Key = g.Key,
                    PartyId = latest.PartyId ?? ordered.Select(r => r.PartyId).FirstOrDefault(x => x is not null),
                    Name = name,
                    Registered = ordered.Any(r => r.PartyId is not null),
                    Address = latest.Address.Value,
                    Channels = refs,
                    LastMessage = LastText(latest.Session),
                    LastDirection = latest.Session.Metadata.GetValueOrDefault("last_message_direction"),
                    LastChannel = refs.FirstOrDefault(c => c.SessionId == latest.Session.Id),
                    LastActivityAt = latest.Session.LastActivityAt,
                    Unread = ordered.Sum(r => int.TryParse(r.Session.Metadata.GetValueOrDefault("unread_count"), out var u) ? u : 0),
                    ReplyPending = ordered.Any(r => r.Session.Metadata.GetValueOrDefault("reply_pending") == "true"),
                    Handoff = handoff,
                    OnlyDeletedChannels = refs.All(c => c.ChannelDeleted)
                };
            })
            .OrderByDescending(c => c.LastActivityAt)
            .ToList();
    }

    public static IEnumerable<InboxCustomer> Filter(IEnumerable<InboxCustomer> customers, InboxFilter filter)
    {
        var query = filter.Query?.Trim();
        var digits = query is null ? string.Empty : new string(query.Where(char.IsDigit).ToArray());
        return customers
            .Where(c => filter.IncludeDeletedChannels || !c.OnlyDeletedChannels)
            // A real customer's row lists only live channels unless deleted (test) ones are asked for.
            .Select(c => filter.IncludeDeletedChannels ? c : c with { Channels = c.Channels.Where(ch => !ch.ChannelDeleted).ToList() })
            .Where(c => string.IsNullOrWhiteSpace(filter.ChannelId) || c.Channels.Any(ch => ch.ChannelId == filter.ChannelId))
            .Where(c => string.IsNullOrWhiteSpace(filter.Provider) || c.Channels.Any(ch => string.Equals(ch.Provider, filter.Provider, StringComparison.OrdinalIgnoreCase)))
            .Where(c => !filter.NeedsAttention || c.Handoff is not null || c.ReplyPending)
            .Where(c => string.IsNullOrWhiteSpace(query) ||
                        c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (digits.Length >= 4 && new string(c.Address.Where(char.IsDigit).ToArray()).Contains(digits, StringComparison.Ordinal)) ||
                        c.Address.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static CustomerAddress AddressOf(ChannelSession session, ChannelDefinition? channel)
    {
        if (session.Metadata.GetValueOrDefault(Provenance.CustomerAddressKey) is { Length: > 0 } stored)
            return new CustomerAddress(stored, stored.StartsWith('+'), false);
        var type = channel?.Type ?? (Enum.TryParse<ChannelType>(session.ChannelType, true, out var t) ? t : ChannelType.Custom);
        var address = CustomerAddress.Normalize(session.Identifier, type);
        return address.Value.Length == 0 ? new CustomerAddress(session.Identifier, false, true) : address;
    }

    private static InboxChannelRef ChannelRef(ChannelSession session, ChannelDefinition? channel)
        => new(
            session.ChannelId,
            channel?.Name ?? session.Metadata.GetValueOrDefault(Provenance.ChannelNameKey) ?? "Canal eliminado",
            channel?.Type.ToString() ?? session.ChannelType,
            channel is null ? session.Metadata.GetValueOrDefault(Provenance.ProviderKey) ?? session.ChannelType.ToLowerInvariant() : Provenance.ProviderOf(channel),
            session.Metadata.GetValueOrDefault(Provenance.BusinessAddressKey),
            session.Id,
            session.LastActivityAt,
            channel is null);

    private static InboxTimelineItem TimelineItem(ChannelMessage m, InboxChannelRef channel)
    {
        var actor = m.Metadata.GetValueOrDefault("actor") is { Length: > 0 } a ? a
            : m.Direction == MessageDirection.Incoming ? "customer"
            : m.Metadata.GetValueOrDefault("origin") is "campaign" ? "campaign"
            : m.Metadata.GetValueOrDefault("origin") is "workflow" ? "workflow"
            : "assistant";
        return new(
            m.Id,
            m.Direction == MessageDirection.Incoming ? "in" : "out",
            actor,
            m.Content,
            m.CreatedAt,
            m.Status.ToString(),
            m.Metadata.GetValueOrDefault("agentflow.delivery"),
            m.ErrorMessage,
            channel.ChannelId,
            channel.ChannelName,
            m.Metadata.GetValueOrDefault(Provenance.ProviderKey) ?? channel.Provider,
            m.SessionId,
            m.Metadata.GetValueOrDefault(Provenance.AttachmentsKey));
    }

    private static string? LastText(ChannelSession session)
        => session.Metadata.GetValueOrDefault("last_message_direction") == "outgoing"
            ? session.Metadata.GetValueOrDefault("last_agent_message") ?? session.Metadata.GetValueOrDefault("last_customer_message")
            : session.Metadata.GetValueOrDefault("last_customer_message") ?? session.Metadata.GetValueOrDefault("last_agent_message");

    private static bool IsRealName(string? name)
        => !string.IsNullOrWhiteSpace(name) && !name.Trim().Equals("unknown", StringComparison.OrdinalIgnoreCase);
}
