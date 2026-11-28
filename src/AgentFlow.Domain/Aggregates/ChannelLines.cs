namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// A line is a channel with its own business number, purpose and assistant (collections, sales,
/// marketing). A tenant may have several lines of one type (two voice numbers, three WhatsApp
/// numbers); inbound traffic is matched to a line by the number the customer reached, outbound
/// traffic goes out by the line that was chosen. Stored in <see cref="ChannelDefinition.Config"/>.
/// </summary>
public static class ChannelLines
{
    /// <summary>The line's business number in E.164 (+525512345678): caller id and inbound match.</summary>
    public const string PhoneNumberKey = "PhoneNumber";
    /// <summary>Meta's id of the WhatsApp business number (the number itself may not be stored).</summary>
    public const string PhoneNumberIdKey = "PhoneNumberId";
    public const string PurposeKey = "Purpose";
    /// <summary>"true" marks the line used when nothing (number or choice) points to another.</summary>
    public const string DefaultLineKey = "DefaultLine";
    /// <summary>Hours customers may be contacted on this line, "08:00-21:00" (local time).</summary>
    public const string ContactHoursKey = "ContactHours";
    /// <summary>IANA or Windows time zone of <see cref="ContactHoursKey"/>; America/Mexico_City by default.</summary>
    public const string TimeZoneKey = "TimeZone";
    public const string DefaultTimeZone = "America/Mexico_City";

    public static class Purposes
    {
        public const string General = "general";
        public const string Collections = "collections";
        public const string Sales = "sales";
        public const string Marketing = "marketing";
        public static readonly IReadOnlyList<string> All = [General, Collections, Sales, Marketing];
    }

    /// <summary>
    /// A channel id (letters, digits, '-' or '_') rather than a channel type key ("voice",
    /// "whatsapp"...): webhooks and calls name the line they belong to this way.
    /// </summary>
    public static bool IsLineKey(string? key)
        => !string.IsNullOrWhiteSpace(key) && key!.Length is >= 8 and <= 64 &&
           !Enum.GetNames<ChannelType>().Contains(key, StringComparer.OrdinalIgnoreCase) &&
           !string.Equals(key, "callcenter", StringComparison.OrdinalIgnoreCase) &&
           key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Digits only: "whatsapp:+1 (901) 660-8125" and "+19016608125" are the same line.</summary>
    public static string Digits(string? number)
        => number is null ? string.Empty : new string(number.Where(char.IsDigit).ToArray());

    public static string? NumberOf(ChannelDefinition channel)
        => channel.Config.GetValueOrDefault(PhoneNumberKey) is { Length: > 0 } number && Digits(number).Length >= 7
            ? number.Trim()
            : null;

    public static string? NumberIdOf(ChannelDefinition channel)
        => channel.Config.GetValueOrDefault(PhoneNumberIdKey) is { Length: > 0 } id && !string.IsNullOrWhiteSpace(id) ? id.Trim() : null;

    /// <summary>The line's purpose; a line without one is general (any campaign may use it).</summary>
    public static string PurposeOf(ChannelDefinition channel)
        => NormalizePurpose(channel.Config.GetValueOrDefault(PurposeKey));

    public static string NormalizePurpose(string? purpose) => (purpose ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "collections" or "cobranza" or "cobro" => Purposes.Collections,
        "sales" or "ventas" or "venta" => Purposes.Sales,
        "marketing" or "mercadeo" or "prospectos" => Purposes.Marketing,
        _ => Purposes.General
    };

    public static bool IsDefault(ChannelDefinition channel)
        => string.Equals(channel.Config.GetValueOrDefault(DefaultLineKey), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="number"/> (any format) is this line's number.</summary>
    public static bool Answers(ChannelDefinition channel, string? number)
    {
        var digits = Digits(number);
        if (digits.Length < 7) return false;
        var own = Digits(NumberOf(channel));
        return own.Length >= 7 && own == digits;
    }

    /// <summary>
    /// The line for traffic on <paramref name="businessNumber"/>: the line with that number; else the
    /// default line; else a line with no number of its own (it answers for the connection's
    /// number); else the first. Only active lines are considered.
    /// </summary>
    public static ChannelDefinition? Pick(IEnumerable<ChannelDefinition> channels, string? businessNumber = null)
    {
        var active = channels.Where(x => x.Status == ChannelStatus.Active).ToList();
        if (active.Count == 0) return null;
        return active.FirstOrDefault(x => Answers(x, businessNumber))
               ?? active.FirstOrDefault(IsDefault)
               ?? active.FirstOrDefault(x => NumberOf(x) is null)
               ?? active[0];
    }

    /// <summary>
    /// The other channel of the same tenant that already uses this line's number (or Meta number id),
    /// if any: a number belongs to one line, or inbound traffic could reach the wrong assistant.
    /// </summary>
    public static ChannelDefinition? NumberTakenBy(ChannelDefinition channel, IEnumerable<ChannelDefinition> others)
    {
        var number = Digits(NumberOf(channel));
        var numberId = NumberIdOf(channel);
        if (number.Length < 7 && numberId is null) return null;
        return others.FirstOrDefault(other =>
            other.Id != channel.Id &&
            SameFamily(other.Type, channel.Type) &&
            ((number.Length >= 7 && Digits(NumberOf(other)) == number) ||
             (numberId is not null && string.Equals(NumberIdOf(other), numberId, StringComparison.Ordinal))));
    }

    /// <summary>A number may serve WhatsApp and calls at once, but not two lines of the same kind.</summary>
    private static bool SameFamily(ChannelType a, ChannelType b)
        => a == b || (IsVoice(a) && IsVoice(b));

    public static bool IsVoice(ChannelType type) => type is ChannelType.Voice or ChannelType.CallCenter;

    /// <summary>
    /// Whether <paramref name="at"/> falls within the line's contact hours. A line without hours
    /// (or with hours that cannot be read) allows any time.
    /// </summary>
    public static bool WithinContactHours(ChannelDefinition channel, DateTimeOffset at)
    {
        if (!TryParseHours(channel.Config.GetValueOrDefault(ContactHoursKey), out var from, out var to)) return true;
        var local = TimeZoneInfo.ConvertTime(at, ZoneOf(channel));
        var now = TimeOnly.FromTimeSpan(local.TimeOfDay);
        return from <= to ? now >= from && now < to : now >= from || now < to;
    }

    /// <summary>
    /// When the line's contact hours open next (now, when they are open), or null for a line without
    /// hours: a scheduled campaign blocked by the hours runs again then.
    /// </summary>
    public static DateTimeOffset? NextContactTime(ChannelDefinition channel, DateTimeOffset at)
    {
        if (!TryParseHours(channel.Config.GetValueOrDefault(ContactHoursKey), out var from, out _)) return null;
        if (WithinContactHours(channel, at)) return at;
        var zone = ZoneOf(channel);
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var opening = local.Date + from.ToTimeSpan();
        if (opening <= local.DateTime) opening = opening.AddDays(1);
        return new DateTimeOffset(opening, zone.GetUtcOffset(opening));
    }

    public static bool TryParseHours(string? hours, out TimeOnly from, out TimeOnly to)
    {
        from = default;
        to = default;
        var parts = (hours ?? string.Empty).Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 &&
               TimeOnly.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out from) &&
               TimeOnly.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out to) &&
               from != to;
    }

    private static TimeZoneInfo ZoneOf(ChannelDefinition channel)
    {
        var id = channel.Config.GetValueOrDefault(TimeZoneKey);
        foreach (var candidate in new[] { id, DefaultTimeZone, "Central Standard Time (Mexico)" })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate!); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
