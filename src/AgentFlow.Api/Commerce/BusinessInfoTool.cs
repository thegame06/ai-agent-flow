using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AgentFlow.Api.Commerce;

/// <summary>
/// The business as customers see it: what it does, its branches, where they are and their hours,
/// whether each is open now and holidays coming up. Read-only; every assistant may use it.
/// </summary>
internal sealed class BusinessInfoTool(IServiceScopeFactory scopeFactory) : SalesAssistantToolBase(scopeFactory)
{
    public override string ExtensionId => "builtin.business.get_info";
    public override string Name => BusinessInfo.ToolName;
    public override string Description =>
        "Datos del negocio: que hace, sus sucursales con direccion, telefono, mapa y horario de atencion, si estan abiertas ahora, cuando cierran o abren, y los feriados proximos. Usala para preguntas de ubicacion, horario o contacto.";
    public override string InputSchemaJson =>
        """{"type":"object","properties":{"branch":{"type":"string","description":"Nombre, codigo o zona de la sucursal que menciona el cliente. Vacio para todas."},"date":{"type":"string","description":"Fecha por la que pregunta (yyyy-MM-dd), por ejemplo si abren un dia concreto."}}}""";

    protected override async Task<object> RunAsync(IServiceProvider services, SalesCall call, JsonElement input, CancellationToken ct)
    {
        var commerce = services.GetRequiredService<ICommerceStore>();
        var settings = await commerce.GetStoreSettingsAsync(call.TenantId, ct);
        var branches = await commerce.SearchBranchesAsync(call.TenantId, null, 0, 100, ct);
        var now = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        return BusinessInfo.Build(settings, branches, now, Text(input, "branch"), Text(input, "date"));
    }
}

/// <summary>Builds what the business info tool returns; pure so it can be tested.</summary>
public static class BusinessInfo
{
    public const string ToolName = "af_business_get_info";
    private const int UpcomingClosureDays = 30;

    public static object Build(
        CommerceStoreSettingsDocument settings,
        IReadOnlyList<CommerceBranchDocument> allBranches,
        DateTimeOffset nowUtc,
        string? branchQuery,
        string? dateText)
    {
        var active = allBranches.Where(b => b.Active).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var matched = string.IsNullOrWhiteSpace(branchQuery) ? active : active.Where(b => Matches(b, branchQuery!)).ToList();
        var noMatch = matched.Count == 0 && !string.IsNullOrWhiteSpace(branchQuery);
        var shown = noMatch ? active : matched;
        var askedDate = BusinessHours.TryParseDate(dateText, out var parsed) ? parsed : (DateOnly?)null;

        var businessZone = BusinessHours.ResolveTimeZone(null, settings.Timezone);
        var businessNow = TimeZoneInfo.ConvertTime(nowUtc, businessZone);

        return new
        {
            business = new
            {
                name = settings.StoreName,
                description = settings.BusinessDescription,
                timezone = businessZone.Id,
                today = businessNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                weekday = BusinessHours.SpanishDay(businessNow.DayOfWeek),
                localTime = businessNow.ToString("HH:mm", CultureInfo.InvariantCulture)
            },
            branchesFound = shown.Count,
            note = noMatch
                ? $"Ninguna sucursal coincide con '{branchQuery}'; estas son todas las sucursales activas."
                : active.Count == 0 ? "El negocio no tiene sucursales registradas." : null,
            branches = shown.Select(b => Branch(b, settings, nowUtc, askedDate)).ToList()
        };
    }

    private static object Branch(CommerceBranchDocument branch, CommerceStoreSettingsDocument settings, DateTimeOffset nowUtc, DateOnly? askedDate)
    {
        var zone = BusinessHours.ResolveTimeZone(branch.Timezone, settings.Timezone);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime;
        var today = DateOnly.FromDateTime(localNow);
        var hasHours = branch.OpeningHours.Count > 0;
        var status = hasHours ? BusinessHours.Status(branch, localNow) : null;

        return new
        {
            code = branch.Code,
            name = branch.Name,
            address = branch.Address,
            phone = branch.Phone,
            mapsUrl = branch.MapsUrl,
            notes = branch.Notes,
            timezone = zone.Id,
            hoursConfigured = hasHours,
            weeklyHours = hasHours
                ? BusinessHours.Days.Select(day => new
                {
                    day = BusinessHours.SpanishDay(day),
                    hours = BusinessHours.Describe(WeeklySpans(branch, day))
                }).ToList()
                : null,
            openNow = status?.IsOpen,
            closesAt = status?.ClosesAt is { } closes ? Moment(closes, today) : null,
            nextOpening = status?.NextOpening is { } opens ? Moment(opens, today) : null,
            upcomingClosures = branch.Closures
                .Select(c => (Closure: c, Ok: BusinessHours.TryParseDate(c.Date, out var d), Date: d))
                .Where(x => x.Ok && x.Date >= today && x.Date <= today.AddDays(UpcomingClosureDays))
                .Select(x => new
                {
                    date = x.Closure.Date,
                    weekday = BusinessHours.SpanishDay(x.Date.DayOfWeek),
                    reason = x.Closure.Reason,
                    hours = BusinessHours.Describe(BusinessHours.HoursOn(branch, x.Date))
                })
                .ToList(),
            askedDate = askedDate is { } date && hasHours
                ? new
                {
                    date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    weekday = BusinessHours.SpanishDay(date.DayOfWeek),
                    hours = BusinessHours.Describe(BusinessHours.HoursOn(branch, date)),
                    reason = BusinessHours.ClosureOn(branch, date)?.Reason
                }
                : null
        };
    }

    private static IReadOnlyList<OpeningSpan> WeeklySpans(CommerceBranchDocument branch, string day)
        => branch.OpeningHours
            .Where(h => string.Equals(h.Day, day, StringComparison.OrdinalIgnoreCase))
            .Select(h => BusinessHours.TryParseTime(h.Open, out var o) && BusinessHours.TryParseTime(h.Close, out var c) && c > o ? new OpeningSpan(o, c) : (OpeningSpan?)null)
            .OfType<OpeningSpan>()
            .OrderBy(s => s.Open)
            .ToList();

    /// <summary>"hoy 18:00", "manana 08:00" or "lunes 2026-12-07 08:00".</summary>
    private static string Moment(DateTime local, DateOnly today)
    {
        var date = DateOnly.FromDateTime(local);
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (date == today) return $"hoy {time}";
        if (date == today.AddDays(1)) return $"manana {time}";
        return $"{BusinessHours.SpanishDay(date.DayOfWeek)} {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {time}";
    }

    private static bool Matches(CommerceBranchDocument branch, string query)
    {
        var q = Fold(query);
        return new[] { branch.Name, branch.Code, branch.Address }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Fold(v!))
            .Any(v => v.Contains(q, StringComparison.Ordinal) || q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4).Any(w => v.Contains(w, StringComparison.Ordinal)));
    }

    /// <summary>Lower case without accents, so "Leon" finds "León".</summary>
    private static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString();
    }
}
