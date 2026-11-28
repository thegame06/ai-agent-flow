using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Api.Channels;

/// <summary>
/// A customer message as it arrived, before AgentFlow does anything with it. Every channel and provider
/// (WhatsApp through QR, Meta or Twilio; the API; WebChat; generic webhooks) translates its own payload
/// into this, and <see cref="IInboundMessageIngest"/> does the rest the same way for all of them.
/// </summary>
public sealed record InboundMessage
{
    public required string TenantId { get; init; }
    public required ChannelDefinition Channel { get; init; }
    /// <summary>Who delivered it: qr, meta, twilio, api, webchat, webhook.</summary>
    public required string Provider { get; init; }
    /// <summary>The provider's own id for the message (deduplication).</summary>
    public string? ExternalId { get; init; }
    /// <summary>The customer as the provider wrote it ("50581143874@c.us", "whatsapp:+505…", a visitor id).</summary>
    public required string From { get; init; }
    /// <summary>The name the customer's app shows (WhatsApp profile name), if any.</summary>
    public string? DisplayName { get; init; }
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<InboundAttachment> Attachments { get; init; } = [];
    /// <summary>The business address it was sent to: a phone number, a Meta phone_number_id, a widget id.</summary>
    public string? BusinessAddress { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    /// <summary>Extra caller metadata copied onto the stored message.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public string? RawPayload { get; init; }
}

/// <summary>
/// A file the customer sent: kind (image, document, audio, video, sticker), where the provider keeps it,
/// and once stored, <see cref="MediaId"/> in AgentFlow's media store. <see cref="Data"/> is set when the
/// provider sends the bytes themselves (the QR bridge); it is never serialized.
/// </summary>
public sealed record InboundAttachment(
    string Kind,
    string? Url = null,
    string? MimeType = null,
    string? ProviderMediaId = null,
    string? FileName = null,
    string? MediaId = null,
    [property: System.Text.Json.Serialization.JsonIgnore] byte[]? Data = null);

/// <summary>Where a message came in or went out. Stored on every incoming message and session.</summary>
public static class Provenance
{
    public const string ProviderKey = "channel.provider";
    public const string ChannelTypeKey = "channel.type";
    public const string ChannelNameKey = "channel.name";
    public const string BusinessAddressKey = "channel.business_address";
    public const string CustomerAddressKey = "customer.address";
    public const string CustomerRawKey = "customer.raw_address";
    public const string CustomerUnresolvedKey = "customer.unresolved";
    public const string PartyIdKey = "party_id";
    public const string PartyNameKey = "party_name";
    public const string DisplayNameKey = "display_name";
    public const string AttachmentsKey = "attachments";
    /// <summary>Stored files the customer sent most recently (comma-separated media ids) and when.</summary>
    public const string LastMediaIdsKey = "last_media_ids";
    public const string LastMediaAtKey = "last_media_at";

    /// <summary>The provider a channel delivers through: qr, meta or twilio for WhatsApp; the type for the rest.</summary>
    public static string ProviderOf(ChannelDefinition channel)
    {
        if (channel.Type != ChannelType.WhatsApp)
            return channel.Type.ToString().ToLowerInvariant();
        return channel.Config.GetValueOrDefault("AuthMode")?.Trim().ToLowerInvariant() switch
        {
            "twilio" => "twilio",
            "business" => "meta",
            _ => "qr"
        };
    }
}
