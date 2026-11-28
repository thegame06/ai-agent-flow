using System.Globalization;

namespace AgentFlow.Api.Commerce;

/// <summary>A local opening interval as minutes from midnight: [Open, Close), Close up to 1440.</summary>
public readonly record struct OpeningSpan(int Open, int Close)
{
    public string Label => $"{BusinessHours.FormatMinutes(Open)}-{BusinessHours.FormatMinutes(Close)}";
}

/// <summary>Whether a branch is open at a moment, and when it closes or opens next (local times).</summary>
public sealed record BranchOpenStatus(bool IsOpen, DateTime? ClosesAt, DateTime? NextOpening);

/// <summary>
/// Opening hours of a branch: validation, the hours of a given date (holidays override the week) and
/// whether it is open now. Times are local to the branch; an interval never crosses midnight (use
/// "22:00-24:00" and "00:00-02:00" for that), which keeps every day self-contained.
/// </summary>
public static class BusinessHours
{
    public static readonly string[] Days = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private static readonly Dictionary<string, string> SpanishDays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monday"] = "lunes", ["tuesday"] = "martes", ["wednesday"] = "miercoles", ["thursday"] = "jueves",
        ["friday"] = "viernes", ["saturday"] = "sabado", ["sunday"] = "domingo"
    };

    public static string DayKey(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "monday",
        DayOfWeek.Tuesday => "tuesday",
        DayOfWeek.Wednesday => "wednesday",
        DayOfWeek.Thursday => "thursday",
        DayOfWeek.Friday => "friday",
        DayOfWeek.Saturday => "saturday",
        _ => "sunday"
    };

    public static string SpanishDay(string dayKey) => SpanishDays.GetValueOrDefault(dayKey, dayKey);

    public static string SpanishDay(DayOfWeek day) => SpanishDay(DayKey(day));

    /// <summary>Parses "HH:mm" (00:00 to 24:00) into minutes from midnight.</summary>
    public static bool TryParseTime(string? text, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m) ||
            parts[1].Length != 2 || h < 0 || m < 0 || m > 59 || h > 24 || (h == 24 && m != 0))
            return false;
        minutes = h * 60 + m;
        return true;
    }

    public static string FormatMinutes(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    public static bool TryParseDate(string? text, out DateOnly date)
        => DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>The IANA (or Windows) time zone, or null when it is unknown.</summary>
    public static TimeZoneInfo? FindTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id.Trim()); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    /// <summary>The branch's zone, else the store's, else the default, else UTC.</summary>
    public static TimeZoneInfo ResolveTimeZone(string? branchTimezone, string? storeTimezone)
        => FindTimeZone(branchTimezone) ?? FindTimeZone(storeTimezone) ?? FindTimeZone(CommerceStoreDefaults.Timezone) ?? TimeZoneInfo.Utc;

    /// <summary>Why the hours cannot be saved, or null when they are valid.</summary>
    public static string? Validate(IEnumerable<CommerceOpeningInterval>? openingHours, IEnumerable<CommerceBranchClosure>? closures, string? timezone)
    {
        if (!string.IsNullOrWhiteSpace(timezone) && FindTimeZone(timezone) is null)
            return $"La zona horaria '{timezone}' no existe. Usa un nombre IANA, por ejemplo America/Managua.";

        var byDay = new Dictionary<string, List<(int, int)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var interval in openingHours ?? [])
        {
            var day = interval.Day?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!Days.Contains(day))
                return $"El dia '{interval.Day}' no es valido: usa monday a sunday.";
            var error = ValidateRange(interval.Open, interval.Close, out var span);
            if (error is not null) return $"{SpanishDay(day)}: {error}";
            (byDay.TryGetValue(day, out var list) ? list : byDay[day] = []).Add((span.Open, span.Close));
        }
        foreach (var (day, spans) in byDay)
            if (Overlaps(spans))
                return $"{SpanishDay(day)}: los horarios se enciman.";

        var seenDates = new HashSet<DateOnly>();
        foreach (var closure in closures ?? [])
        {
            if (!TryParseDate(closure.Date, out var date))
                return $"La fecha '{closure.Date}' no es valida: usa yyyy-MM-dd.";
            if (!seenDates.Add(date))
                return $"La fecha {closure.Date} esta repetida.";
            var spans = new List<(int, int)>();
            foreach (var range in closure.Intervals ?? [])
            {
                var error = ValidateRange(range.Open, range.Close, out var span);
                if (error is not null) return $"{closure.Date}: {error}";
                spans.Add((span.Open, span.Close));
            }
            if (Overlaps(spans))
                return $"{closure.Date}: los horarios se enciman.";
        }
        return null;
    }

    private static string? ValidateRange(string? open, string? close, out OpeningSpan span)
    {
        span = default;
        if (!TryParseTime(open, out var o) || o >= 1440)
            return $"la hora de apertura '{open}' no es valida (HH:mm).";
        if (!TryParseTime(close, out var c))
            return $"la hora de cierre '{close}' no es valida (HH:mm, hasta 24:00).";
        if (c <= o)
            return $"el cierre ({close}) debe ser despues de la apertura ({open}); si pasa la medianoche usa dos intervalos (hasta 24:00 y desde 00:00).";
        span = new OpeningSpan(o, c);
        return null;
    }

    private static bool Overlaps(List<(int Open, int Close)> spans)
    {
        var sorted = spans.OrderBy(s => s.Open).ToList();
        for (var i = 1; i < sorted.Count; i++)
            if (sorted[i].Open < sorted[i - 1].Close) return true;
        return false;
    }

    /// <summary>Trims and sorts valid hours so they are stored the same way every time.</summary>
    public static List<CommerceOpeningInterval> NormalizeOpeningHours(IEnumerable<CommerceOpeningInterval>? hours)
        => (hours ?? [])
            .Select(h => (Day: h.Day?.Trim().ToLowerInvariant() ?? string.Empty, Ok: TryParseTime(h.Open, out var o) & TryParseTime(h.Close, out var c), o, c))
            .Where(x => x.Ok && Days.Contains(x.Day) && x.c > x.o)
            .OrderBy(x => Array.IndexOf(Days, x.Day)).ThenBy(x => x.o)
            .Select(x => new CommerceOpeningInterval { Day = x.Day, Open = FormatMinutes(x.o), Close = FormatMinutes(x.c) })
            .ToList();

    public static List<CommerceBranchClosure> NormalizeClosures(IEnumerable<CommerceBranchClosure>? closures)
        => (closures ?? [])
            .Where(c => TryParseDate(c.Date, out _))
            .Select(c => new CommerceBranchClosure
            {
                Date = c.Date.Trim(),
                Reason = string.IsNullOrWhiteSpace(c.Reason) ? null : c.Reason.Trim(),
                Intervals = (c.Intervals ?? [])
                    .Select(r => (Ok: TryParseTime(r.Open, out var o) & TryParseTime(r.Close, out var cl), o, cl))
                    .Where(x => x.Ok && x.cl > x.o)
                    .OrderBy(x => x.o)
                    .Select(x => new CommerceTimeRange { Open = FormatMinutes(x.o), Close = FormatMinutes(x.cl) })
                    .ToList()
            })
            .OrderBy(c => c.Date, StringComparer.Ordinal)
            .ToList();

    /// <summary>The hours on a local date: a closure for that date wins over the weekly hours.</summary>
    public static IReadOnlyList<OpeningSpan> HoursOn(CommerceBranchDocument branch, DateOnly date)
    {
        var closure = branch.Closures.FirstOrDefault(c => TryParseDate(c.Date, out var d) && d == date);
        if (closure is not null)
            return closure.Intervals
                .Select(r => TryParseTime(r.Open, out var o) && TryParseTime(r.Close, out var c) && c > o ? new OpeningSpan(o, c) : (OpeningSpan?)null)
                .OfType<OpeningSpan>().OrderBy(s => s.Open).ToList();

        var day = DayKey(date.DayOfWeek);
        return branch.OpeningHours
            .Where(h => string.Equals(h.Day, day, StringComparison.OrdinalIgnoreCase))
            .Select(h => TryParseTime(h.Open, out var o) && TryParseTime(h.Close, out var c) && c > o ? new OpeningSpan(o, c) : (OpeningSpan?)null)
            .OfType<OpeningSpan>().OrderBy(s => s.Open).ToList();
    }

    /// <summary>The closure for a date, if any.</summary>
    public static CommerceBranchClosure? ClosureOn(CommerceBranchDocument branch, DateOnly date)
        => branch.Closures.FirstOrDefault(c => TryParseDate(c.Date, out var d) && d == date);

    /// <summary>Open now? If so, when it closes (joining back-to-back intervals, also across midnight); if not, when it opens next (within two weeks).</summary>
    public static BranchOpenStatus Status(CommerceBranchDocument branch, DateTime localNow)
    {
        var today = DateOnly.FromDateTime(localNow);
        var minute = localNow.Hour * 60 + localNow.Minute;
        var current = HoursOn(branch, today).FirstOrDefault(s => s.Open <= minute && minute < s.Close);
        if (current != default)
            return new BranchOpenStatus(true, ClosingTime(branch, today, current), null);

        for (var offset = 0; offset <= 14; offset++)
        {
            var date = today.AddDays(offset);
            var next = HoursOn(branch, date).FirstOrDefault(s => offset > 0 || s.Open > minute);
            if (next != default)
                return new BranchOpenStatus(false, null, date.ToDateTime(TimeOnly.MinValue).AddMinutes(next.Open));
        }
        return new BranchOpenStatus(false, null, null);
    }

    private static DateTime ClosingTime(CommerceBranchDocument branch, DateOnly date, OpeningSpan span)
    {
        // "08:00-12:00" + "12:00-18:00" closes at 18:00; "20:00-24:00" + next day "00:00-02:00" closes at 02:00.
        for (var guard = 0; guard < 8; guard++)
        {
            var close = span.Close;
            var joined = HoursOn(branch, date).FirstOrDefault(s => s.Open == close);
            if (joined != default) { span = joined; continue; }
            if (close == 1440)
            {
                var nextDay = date.AddDays(1);
                var early = HoursOn(branch, nextDay).FirstOrDefault(s => s.Open == 0);
                if (early != default) { date = nextDay; span = early; continue; }
            }
            break;
        }
        return date.ToDateTime(TimeOnly.MinValue).AddMinutes(span.Close);
    }

    /// <summary>"08:00-12:00, 14:00-18:00" or "cerrado".</summary>
    public static string Describe(IReadOnlyList<OpeningSpan> spans)
        => spans.Count == 0 ? "cerrado" : string.Join(", ", spans.Select(s => s.Label));

    /// <summary>Why the booking data cannot be saved, or null.</summary>
    public static string? ValidateScheduling(CommerceServiceSchedulingDocument? scheduling)
    {
        if (scheduling is null) return null;
        if (scheduling.DurationMinutes < 0 || scheduling.DurationMinutes > 24 * 60)
            return "La duracion debe estar entre 0 y 1440 minutos.";
        if (scheduling.Bookable && scheduling.DurationMinutes < 5)
            return "Un servicio agendable necesita una duracion de al menos 5 minutos.";
        if (scheduling.BufferMinutes < 0 || scheduling.BufferMinutes > 240)
            return "El margen entre citas debe estar entre 0 y 240 minutos.";
        return null;
    }

    public static CommerceServiceSchedulingDocument NormalizeScheduling(CommerceServiceSchedulingDocument scheduling) => new()
    {
        Bookable = scheduling.Bookable,
        DurationMinutes = Math.Clamp(scheduling.DurationMinutes, 0, 24 * 60),
        BufferMinutes = Math.Clamp(scheduling.BufferMinutes, 0, 240),
        PriceIsFrom = scheduling.PriceIsFrom,
        Preparation = string.IsNullOrWhiteSpace(scheduling.Preparation) ? null : scheduling.Preparation.Trim(),
        ResourceKinds = (scheduling.ResourceKinds ?? [])
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim().ToLowerInvariant())
            .Distinct()
            .ToList()
    };
}
