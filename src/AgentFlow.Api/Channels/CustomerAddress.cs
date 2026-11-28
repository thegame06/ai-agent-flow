using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Api.Channels;

/// <summary>
/// The one way a customer is addressed, whatever channel or provider they write through. A phone
/// becomes E.164 ("+50581143874") whether it arrived as "50581143874" (Meta), "whatsapp:+505…"
/// (Twilio), "50581143874@c.us" (QR bridge) or "8114 3874" with the tenant's default country.
/// Anything that is not a phone (a web visitor id, an API user id) is kept as given, trimmed.
/// </summary>
public sealed record CustomerAddress(string Value, bool IsPhone, bool Unresolved)
{
    private static readonly string[] Prefixes = ["whatsapp:", "tel:", "sms:"];
    private static readonly string[] PhoneSuffixes = ["@c.us", "@s.whatsapp.net"];

    /// <summary>Canonical form of <paramref name="raw"/>; <paramref name="defaultCountryCode"/> ("505") completes local numbers.</summary>
    public static CustomerAddress Normalize(string? raw, ChannelType channelType, string? defaultCountryCode = null)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0) return new(string.Empty, false, true);

        foreach (var prefix in Prefixes)
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                value = value[prefix.Length..].Trim();

        // A WhatsApp linked id ("…@lid") is not a phone: it matches no customer, loan or campaign.
        if (value.EndsWith("@lid", StringComparison.OrdinalIgnoreCase))
            return new(value, false, true);

        foreach (var suffix in PhoneSuffixes)
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                value = value[..^suffix.Length];

        var looksLikePhone = value.Length > 0 && value.All(c => char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')' or '.');
        var phoneChannel = channelType is ChannelType.WhatsApp or ChannelType.Voice or ChannelType.CallCenter;
        if (!looksLikePhone || (!phoneChannel && !value.StartsWith('+') && value.Count(char.IsDigit) < 8))
            return new(value, false, false);

        var international = value.StartsWith('+') || value.StartsWith("00", StringComparison.Ordinal);
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (value.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.Length < 7) return new(value, false, true);

        var country = new string((defaultCountryCode ?? string.Empty).Where(char.IsDigit).ToArray());
        // A local number (no country code): complete it with the tenant's country, dropping a trunk 0.
        if (!international && country.Length > 0 && digits.Length <= 10 && !digits.StartsWith(country, StringComparison.Ordinal))
            digits = country + digits.TrimStart('0');

        return new("+" + digits, true, false);
    }

    /// <summary>Forms a session may have been stored under before addresses were canonical.</summary>
    public static IReadOnlyList<string> LegacyForms(string? raw, CustomerAddress canonical)
    {
        var forms = new List<string>();
        void Add(string? x) { if (!string.IsNullOrWhiteSpace(x) && x != canonical.Value && !forms.Contains(x)) forms.Add(x); }
        Add(raw?.Trim());
        if (canonical.IsPhone)
        {
            var digits = canonical.Value.TrimStart('+');
            Add(digits);
            Add(digits + "@c.us");
        }
        return forms;
    }
}
