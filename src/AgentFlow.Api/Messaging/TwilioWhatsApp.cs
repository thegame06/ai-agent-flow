using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Api.Messaging;

/// <summary>
/// WhatsApp through Twilio (AuthMode "twilio"): Twilio forwards incoming messages as a signed form post
/// and replies go out through Twilio's Messages API with the channel's Twilio connection.
/// </summary>
public static class TwilioWhatsApp
{
    public const string AuthMode = "twilio";
    private const string AddressPrefix = "whatsapp:";

    public static bool IsTwilio(ChannelDefinition channel)
        => string.Equals(channel.Config.GetValueOrDefault("AuthMode"), AuthMode, StringComparison.OrdinalIgnoreCase);

    /// <summary>"whatsapp:+50581143874" → "+50581143874".</summary>
    public static string? Phone(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var value = address.Trim();
        if (value.StartsWith(AddressPrefix, StringComparison.OrdinalIgnoreCase))
            value = value[AddressPrefix.Length..].Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// The active Twilio channel of the number the customer wrote to (config FromPhoneNumber), or a Twilio
    /// channel with no number set. Never a QR or Meta channel.
    /// </summary>
    public static ChannelDefinition? ChannelFor(IReadOnlyList<ChannelDefinition> channels, string? to)
    {
        var twilio = channels.Where(c => c.Status == ChannelStatus.Active && IsTwilio(c)).ToList();
        var number = Digits(Phone(to));
        if (number.Length > 0)
        {
            var match = twilio.FirstOrDefault(c => Digits(c.Config.GetValueOrDefault("FromPhoneNumber")) == number);
            if (match is not null) return match;
        }
        return twilio.FirstOrDefault(c => Digits(c.Config.GetValueOrDefault("FromPhoneNumber")).Length == 0);
    }

    private static string Digits(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : new string(value.Where(char.IsDigit).ToArray());
}

/// <summary>The fields Twilio posts for an incoming WhatsApp message.</summary>
public sealed class TwilioWhatsAppForm
{
    public string? MessageSid { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Body { get; set; }
    public string? ProfileName { get; set; }
    public string? NumMedia { get; set; }
}
