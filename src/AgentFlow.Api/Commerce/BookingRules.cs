using System.Globalization;

namespace AgentFlow.Api.Commerce;

/// <summary>
/// How a catalog item is reserved. Anything can be reserved: a doctor's appointment, a workshop bay,
/// a restaurant table, a hotel room, a house, a machine. What changes is how time is measured.
/// </summary>
public static class BookingModes
{
    /// <summary>Fixed length starting at set times: a doctor, a workshop service, a restaurant seating.</summary>
    public const string Appointment = "appointment";
    /// <summary>The customer picks how many hours, within a minimum and a maximum: a machine, a court, a room.</summary>
    public const string Hourly = "hourly";
    /// <summary>Whole days, handed over and returned at set times: equipment or vehicle rental.</summary>
    public const string Daily = "daily";
    /// <summary>Nights, with check-in and check-out times: a hotel room, a house.</summary>
    public const string Nightly = "nightly";

    public static readonly string[] All = [Appointment, Hourly, Daily, Nightly];
}

/// <summary>What the price is for.</summary>
public static class PriceUnits
{
    public const string Booking = "booking";
    public const string Hour = "hour";
    public const string Day = "day";
    public const string Night = "night";
    public const string Person = "person";

    public static readonly string[] All = [Booking, Hour, Day, Night, Person];

    public static string DefaultFor(string mode) => mode switch
    {
        BookingModes.Hourly => Hour,
        BookingModes.Daily => Day,
        BookingModes.Nightly => Night,
        _ => Booking
    };

    public static string Spanish(string unit) => unit switch
    {
        Hour => "por hora",
        Day => "por dia",
        Night => "por noche",
        Person => "por persona",
        _ => "por reserva"
    };
}

/// <summary>Reservation settings of a catalog item (service or tangible).</summary>
public sealed class CommerceBookingDocument
{
    public bool Bookable { get; set; }
    /// <summary>One of <see cref="BookingModes"/>.</summary>
    public string Mode { get; set; } = BookingModes.Appointment;
    /// <summary>Appointment: how long it takes.</summary>
    public int DurationMinutes { get; set; }
    /// <summary>Hourly: shortest and longest booking, and the step between lengths.</summary>
    public int MinMinutes { get; set; }
    public int MaxMinutes { get; set; }
    public int StepMinutes { get; set; }
    /// <summary>Daily and nightly: fewest and most days or nights; 0 = no maximum.</summary>
    public int MinUnits { get; set; }
    public int MaxUnits { get; set; }
    /// <summary>Daily: hand-over and return time. Nightly: check-in and check-out time. Local "HH:mm".</summary>
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    /// <summary>Time the resource stays blocked after each booking (cleaning, maintenance, turnover).</summary>
    public int BufferMinutes { get; set; }
    /// <summary>One of <see cref="PriceUnits"/>; empty takes the mode's (per night for nightly, and so on).</summary>
    public string PriceUnit { get; set; } = string.Empty;
    /// <summary>The price is a starting price ("desde").</summary>
    public bool PriceIsFrom { get; set; }
    /// <summary>How many people one booking is for (a table, a room); 0 = not asked.</summary>
    public int MinPartySize { get; set; }
    public int MaxPartySize { get; set; }
    /// <summary>Kinds of resources one booking takes ("dentist", "chair", "table", "double-room", "excavator").</summary>
    public List<string> ResourceKinds { get; set; } = [];
    /// <summary>What the customer must know or do before: preparation, what to bring, policies.</summary>
    public string? Instructions { get; set; }
}

/// <summary>Validation, normalization and a plain description of <see cref="CommerceBookingDocument"/>.</summary>
public static class BookingRules
{
    private const int MaxMinutesInRange = 14 * 24 * 60;

    /// <summary>Why the settings cannot be saved, or null.</summary>
    public static string? Validate(CommerceBookingDocument? booking)
    {
        if (booking is null) return null;
        var mode = booking.Mode?.Trim().ToLowerInvariant();
        if (!BookingModes.All.Contains(mode))
            return $"El modo de reserva '{booking.Mode}' no existe: usa appointment, hourly, daily o nightly.";
        if (!string.IsNullOrWhiteSpace(booking.PriceUnit) && !PriceUnits.All.Contains(booking.PriceUnit.Trim().ToLowerInvariant()))
            return $"La unidad de precio '{booking.PriceUnit}' no existe: usa booking, hour, day, night o person.";
        if (booking.BufferMinutes < 0 || booking.BufferMinutes > 24 * 60)
            return "El tiempo bloqueado despues de cada reserva debe estar entre 0 y 1440 minutos.";
        if (booking.MinPartySize < 0 || booking.MaxPartySize < 0 || booking.MaxPartySize > 1000)
            return "Las personas por reserva deben estar entre 0 y 1000.";
        if (booking.MaxPartySize > 0 && booking.MinPartySize > booking.MaxPartySize)
            return "El minimo de personas no puede ser mayor que el maximo.";

        switch (mode)
        {
            case BookingModes.Appointment:
                if (booking.DurationMinutes < 0 || booking.DurationMinutes > 24 * 60)
                    return "La duracion debe estar entre 0 y 1440 minutos.";
                if (booking.Bookable && booking.DurationMinutes < 5)
                    return "Una cita necesita una duracion de al menos 5 minutos.";
                break;
            case BookingModes.Hourly:
                if (booking.Bookable && booking.MinMinutes < 5)
                    return "La reserva por horas necesita un minimo de al menos 5 minutos.";
                if (booking.MinMinutes < 0 || booking.MaxMinutes < 0 || booking.MaxMinutes > MaxMinutesInRange)
                    return "El minimo y el maximo deben estar entre 0 y 20160 minutos (14 dias).";
                if (booking.MaxMinutes > 0 && booking.MaxMinutes < booking.MinMinutes)
                    return "El maximo no puede ser menor que el minimo.";
                if (booking.StepMinutes < 0 || booking.StepMinutes > 24 * 60)
                    return "El incremento debe estar entre 0 y 1440 minutos.";
                break;
            case BookingModes.Daily:
            case BookingModes.Nightly:
                var unit = mode == BookingModes.Daily ? "dias" : "noches";
                if (booking.MinUnits < 0 || booking.MaxUnits < 0 || booking.MaxUnits > 365)
                    return $"Los {unit} deben estar entre 0 y 365.";
                if (booking.MaxUnits > 0 && booking.MaxUnits < Math.Max(1, booking.MinUnits))
                    return $"El maximo de {unit} no puede ser menor que el minimo.";
                if (!string.IsNullOrWhiteSpace(booking.StartTime) && !ValidClock(booking.StartTime))
                    return $"La hora '{booking.StartTime}' no es valida (HH:mm).";
                if (!string.IsNullOrWhiteSpace(booking.EndTime) && !ValidClock(booking.EndTime))
                    return $"La hora '{booking.EndTime}' no es valida (HH:mm).";
                break;
        }
        return null;
    }

    private static bool ValidClock(string text) => BusinessHours.TryParseTime(text, out var minutes) && minutes < 1440;

    /// <summary>Trims, lower-cases and fills defaults (price unit by mode, check-in 15:00 / check-out 12:00).</summary>
    public static CommerceBookingDocument Normalize(CommerceBookingDocument booking)
    {
        var mode = BookingModes.All.Contains(booking.Mode?.Trim().ToLowerInvariant()) ? booking.Mode!.Trim().ToLowerInvariant() : BookingModes.Appointment;
        var priceUnit = booking.PriceUnit?.Trim().ToLowerInvariant();
        var normalized = new CommerceBookingDocument
        {
            Bookable = booking.Bookable,
            Mode = mode,
            BufferMinutes = Math.Clamp(booking.BufferMinutes, 0, 24 * 60),
            PriceUnit = PriceUnits.All.Contains(priceUnit) ? priceUnit! : PriceUnits.DefaultFor(mode),
            PriceIsFrom = booking.PriceIsFrom,
            MinPartySize = Math.Max(0, booking.MinPartySize),
            MaxPartySize = Math.Max(0, booking.MaxPartySize),
            Instructions = string.IsNullOrWhiteSpace(booking.Instructions) ? null : booking.Instructions.Trim(),
            ResourceKinds = (booking.ResourceKinds ?? [])
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim().ToLowerInvariant())
                .Distinct()
                .ToList()
        };

        switch (mode)
        {
            case BookingModes.Appointment:
                normalized.DurationMinutes = Math.Clamp(booking.DurationMinutes, 0, 24 * 60);
                break;
            case BookingModes.Hourly:
                normalized.MinMinutes = Math.Clamp(booking.MinMinutes, 0, MaxMinutesInRange);
                normalized.MaxMinutes = Math.Clamp(booking.MaxMinutes, 0, MaxMinutesInRange);
                normalized.StepMinutes = booking.StepMinutes > 0 ? Math.Min(booking.StepMinutes, 24 * 60) : 30;
                break;
            default:
                normalized.MinUnits = Math.Max(1, booking.MinUnits);
                normalized.MaxUnits = Math.Max(0, booking.MaxUnits);
                normalized.StartTime = Clock(booking.StartTime) ?? (mode == BookingModes.Nightly ? "15:00" : "08:00");
                normalized.EndTime = Clock(booking.EndTime) ?? (mode == BookingModes.Nightly ? "12:00" : "17:00");
                break;
        }
        return normalized;
    }

    private static string? Clock(string? text)
        => BusinessHours.TryParseTime(text, out var minutes) && minutes < 1440 ? BusinessHours.FormatMinutes(minutes) : null;

    /// <summary>One line for the assistant and the catalog: "cita de 45 min", "por noches (minimo 2), entrada 15:00 y salida 12:00".</summary>
    public static string Describe(CommerceBookingDocument booking)
    {
        var parts = new List<string>();
        switch (booking.Mode)
        {
            case BookingModes.Hourly:
                parts.Add(booking.MaxMinutes > 0
                    ? $"por horas, de {Length(booking.MinMinutes)} a {Length(booking.MaxMinutes)}"
                    : $"por horas, minimo {Length(booking.MinMinutes)}");
                break;
            case BookingModes.Daily:
                parts.Add($"por dias ({Range(booking.MinUnits, booking.MaxUnits, "dia", "dias")}), entrega {booking.StartTime} y devolucion {booking.EndTime}");
                break;
            case BookingModes.Nightly:
                parts.Add($"por noches ({Range(booking.MinUnits, booking.MaxUnits, "noche", "noches")}), entrada {booking.StartTime} y salida {booking.EndTime}");
                break;
            default:
                parts.Add($"cita de {Length(booking.DurationMinutes)}");
                break;
        }
        if (booking.MaxPartySize > 0)
            parts.Add(booking.MinPartySize > 1
                ? $"para {booking.MinPartySize} a {booking.MaxPartySize} personas"
                : $"hasta {booking.MaxPartySize} personas");
        return string.Join(", ", parts);
    }

    private static string Range(int min, int max, string one, string many)
    {
        var low = Math.Max(1, min);
        if (max <= 0) return low == 1 ? $"desde 1 {one}" : $"minimo {low} {many}";
        return low == max ? $"{low} {(low == 1 ? one : many)}" : $"{low} a {max} {many}";
    }

    /// <summary>"45 min", "2 h", "1 h 30 min".</summary>
    public static string Length(int minutes)
    {
        if (minutes < 60) return $"{minutes.ToString(CultureInfo.InvariantCulture)} min";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} h" : $"{hours} h {rest} min";
    }
}
