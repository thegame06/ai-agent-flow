using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AgentFlow.Abstractions.Connect;

namespace AgentFlow.Api.Campaigns;

/// <summary>Result of classifying a customer's reply to a campaign message.</summary>
/// <param name="Outcome">Outcome code from <see cref="CampaignOutcomes"/>, or null when the reply is unclear.</param>
public sealed record CampaignReplyClassification(string? Outcome, decimal? PromiseAmount, DateTimeOffset? PromiseDate, string? MatchedRule);

/// <summary>
/// Deterministic, rule-based classifier for Spanish replies (si / no / promesa de pago / ya pague /
/// numero equivocado / no me escriban ...). It is the baseline every tenant gets without an LLM;
/// agents, workflows and operators can always overwrite the outcome it sets.
/// </summary>
public static partial class CampaignReplyClassifier
{
    private static readonly (string Outcome, Regex Pattern)[] Rules =
    [
        (CampaignOutcomes.OptOut, R(@"no me (vuelvan a )?(escriban|contacten|molesten|marquen|llamen|manden)|dejen de (escribir|molestar|llamar|mandar)|\bstop\b|dar(me)? de baja|no quiero (recibir|que me)")),
        (CampaignOutcomes.WrongNumber, R(@"numero equivocado|se equivoc|equivocad[oa]s?\b|no soy (yo|esa persona|el|ella|quien)|no conozco a|aqui no vive|no es (su|el) numero")),
        (CampaignOutcomes.Dispute, R(@"no reconozco|no debo nada|yo no debo|no es mi (deuda|prestamo|credito|cuenta)|nunca (pedi|solicite|saque|tuve)|no tengo ningun (prestamo|credito)|eso no es mio")),
        (CampaignOutcomes.AlreadyPaid, R(@"ya (lo )?(pague|realice el pago|hice el pago|deposite|transferi|liquide|abone|cubri|quede al corriente|esta pagad)|ya (esta|quedo) (pagado|liquidado|cubierto)|ya estoy al corriente")),
        (CampaignOutcomes.CannotPay, R(@"no (tengo|cuento con) (dinero|para pagar|como pagar|con que pagar|lana|plata|trabajo|empleo)|no puedo pagar|no me alcanza|me quede sin (trabajo|empleo|dinero)|estoy sin (trabajo|empleo|dinero)|no tengo para|desemplead|me despidieron|perdi (el|mi) (trabajo|empleo)")),
        // "No se", "algun dia": the customer answered but committed to nothing. Not a refusal.
        (CampaignOutcomes.Replied, R(@"^no se\b(?! si)|^ni idea\b|(todavia|aun) no se\b|no sabria|no tengo (fecha|idea)|algun dia|un dia de estos|cuando (pueda|tenga|se pueda)\b|mas adelante|el (proximo|otro) (ano|mes)|el ano que viene")),
        (CampaignOutcomes.NotInterested, R(@"no (voy|pienso) a? ?pagar|no pagare|no me interesa|no quiero|no gracias|^(no|nel|nop|nope|para nada)\b")),
        (CampaignOutcomes.PromiseToPay, R(@"\b(pago|pagare|pagaria|voy a pagar|puedo pagar|podria pagar|te pago|le pago|les pago|deposito|depositare|voy a depositar|abono|abonare|voy a abonar|liquido|liquidare|hago el pago|hare el pago|realizo el pago|realizare el pago|cubro|cubrire)\b")),
        (CampaignOutcomes.NeedsHuman, R(@"\basesor|\bejecutivo|hablar con (alguien|una persona|un humano|un agente)|\bhumano\b|una persona real")),
        (CampaignOutcomes.CallbackRequested, R(@"(llam|marc)a(me|nme)|mas tarde|en otro momento|luego (te|le|les) (escribo|aviso|marco|contesto)|ahorita no puedo|estoy ocupad|despues (te|le|les)|otro dia")),
        (CampaignOutcomes.Interested, R(@"^(si|claro|ok|okay|va|vale|de acuerdo|acepto|perfecto|esta bien|dale|sale|me interesa|por supuesto|con gusto)\b|me interesa"))
    ];

    // Doubt words: "no se si pueda pagar el viernes" is not a promise. Left for a person to read.
    private static readonly Regex Hedge = R(@"no se si|tal vez|talvez|quizas|quiza|a lo mejor|creo que|igual y|no estoy seguro|no prometo|si me pagan|si me depositan|a ver si|vere si|lo intento|intentare");

    private static readonly HashSet<string> ClearEvenWithDoubt =
    [
        CampaignOutcomes.OptOut, CampaignOutcomes.WrongNumber, CampaignOutcomes.Dispute,
        CampaignOutcomes.NeedsHuman, CampaignOutcomes.CallbackRequested,
        // A situation, not a commitment: "tal vez el proximo ano, estoy desempleado" still says they cannot pay.
        CampaignOutcomes.CannotPay
    ];

    private static readonly string[] Weekdays = ["domingo", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado"];
    private static readonly string[] Months = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    /// <param name="receivedAt">When the reply arrived, in the tenant's local offset; relative dates resolve from it.</param>
    public static CampaignReplyClassification Classify(string? reply, DateTimeOffset receivedAt)
    {
        var text = Normalize(reply);
        if (text.Length == 0) return new(null, null, null, null);

        // With doubt ("no se si", "tal vez"...) only unambiguous intents count; yes/no/promise are left for a person.
        var hedged = Hedge.IsMatch(text);
        foreach (var (outcome, pattern) in Rules)
        {
            if (hedged && !ClearEvenWithDoubt.Contains(outcome)) continue;
            if (!pattern.IsMatch(text)) continue;
            if (outcome != CampaignOutcomes.PromiseToPay)
                return new(outcome, null, null, pattern.ToString());
            if (Hedge.IsMatch(text))
                return new(null, null, null, "hedge");

            return new(outcome, ExtractAmount(text), ExtractDate(text, receivedAt), pattern.ToString());
        }

        // A bare date or amount after a collections message ("el viernes", "$500 el 15") is a promise.
        var date = ExtractDate(text, receivedAt);
        var amount = ExtractAmount(text);
        if (Hedge.IsMatch(text))
            return new(null, null, null, "hedge");
        if (date is not null && (amount is not null || text.Length <= 40))
            return new(CampaignOutcomes.PromiseToPay, amount, date, "date_or_amount");

        return new(null, null, null, null);
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) || c is '$' or '/' or '.' or ',' ? c : ' ');
        }
        return Spaces().Replace(sb.ToString(), " ").Trim(' ', '.', ',');
    }

    /// <summary>Resolves what a customer said about a date ("el lunes", "manana", "el 15 de diciembre") from <paramref name="now"/> (local time).</summary>
    public static DateTimeOffset? ResolveDate(string? text, DateTimeOffset now)
    {
        var normalized = Normalize(text);
        return normalized.Length == 0 ? null : ExtractDate(normalized, now);
    }

    internal static DateTimeOffset? ExtractDate(string text, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);

        if (text.Contains("pasado manana")) return today.AddDays(2);
        if (Regex.IsMatch(text, @"\bmanana\b") && !Regex.IsMatch(text, @"\b(en|por|de|esta) la manana\b")) return today.AddDays(1);
        if (Regex.IsMatch(text, @"\bhoy\b")) return today;
        if (Regex.IsMatch(text, @"(proxima|otra|siguiente) semana")) return today.AddDays(7);
        if (text.Contains("fin de mes")) return new DateTimeOffset(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month), 0, 0, 0, today.Offset);
        if (text.Contains("quincena"))
            return today.Day < 15
                ? new DateTimeOffset(today.Year, today.Month, 15, 0, 0, 0, today.Offset)
                : new DateTimeOffset(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month), 0, 0, 0, today.Offset);

        var inDays = Regex.Match(text, @"en (\d{1,2}|un|una|dos|tres|cuatro|cinco) dias?");
        if (inDays.Success)
        {
            var n = inDays.Groups[1].Value switch { "un" or "una" => 1, "dos" => 2, "tres" => 3, "cuatro" => 4, "cinco" => 5, var d => int.Parse(d, CultureInfo.InvariantCulture) };
            return today.AddDays(n);
        }

        var slash = Regex.Match(text, @"\b(\d{1,2})/(\d{1,2})(?:/(\d{2,4}))?\b");
        if (slash.Success)
        {
            var day = int.Parse(slash.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(slash.Groups[2].Value, CultureInfo.InvariantCulture);
            var year = slash.Groups[3].Success ? int.Parse(slash.Groups[3].Value, CultureInfo.InvariantCulture) : today.Year;
            if (year < 100) year += 2000;
            if (month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
            {
                var date = new DateTimeOffset(year, month, day, 0, 0, 0, today.Offset);
                return !slash.Groups[3].Success && date < today ? date.AddYears(1) : date;
            }
        }

        var dayOfMonth = Regex.Match(text, @"\b(?:el|dia|el dia) (\d{1,2})(?: de (" + string.Join('|', Months) + "))?\\b");
        if (dayOfMonth.Success)
        {
            var day = int.Parse(dayOfMonth.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = dayOfMonth.Groups[2].Success ? Array.IndexOf(Months, dayOfMonth.Groups[2].Value) + 1 : today.Month;
            var year = today.Year;
            if (day >= 1 && day <= 31)
            {
                if (!dayOfMonth.Groups[2].Success && day < today.Day) { month++; if (month > 12) { month = 1; year++; } }
                else if (dayOfMonth.Groups[2].Success && month < today.Month) year++;
                day = Math.Min(day, DateTime.DaysInMonth(year, month));
                return new DateTimeOffset(year, month, day, 0, 0, 0, today.Offset);
            }
        }

        for (var i = 0; i < Weekdays.Length; i++)
        {
            if (!Regex.IsMatch(text, $@"\b{Weekdays[i]}\b")) continue;
            var delta = ((i - (int)today.DayOfWeek) + 7) % 7;
            return today.AddDays(delta == 0 ? 7 : delta);
        }

        return null;
    }

    internal static decimal? ExtractAmount(string text)
    {
        var match = Regex.Match(text, @"\$\s?(\d[\d.,]*)");
        if (!match.Success)
            match = Regex.Match(text, @"(\d[\d.,]*)\s?(mil\b|pesos|mxn|dolares|usd|cordobas|lempiras|quetzales|soles)");
        if (!match.Success)
            match = Regex.Match(text, @"\b(?:pago|abono|deposito|pagare|abonare|depositare|te pago|le pago|cubro) (?:de |unos |como )?(\d[\d.,]*)");
        if (!match.Success) return null;

        var value = ParseNumber(match.Groups[1].Value);
        if (value is null) return null;
        if (match.Groups.Count > 2 && match.Groups[2].Value == "mil") value *= 1000;
        return value >= 10 ? value : null;
    }

    private static decimal? ParseNumber(string raw)
    {
        raw = raw.TrimEnd('.', ',');
        if (raw.Contains(',') && raw.Contains('.'))
            raw = raw.LastIndexOf(',') > raw.LastIndexOf('.') ? raw.Replace(".", "").Replace(',', '.') : raw.Replace(",", "");
        else if (raw.Contains(','))
            raw = raw.Length - raw.LastIndexOf(',') - 1 == 3 ? raw.Replace(",", "") : raw.Replace(',', '.');
        else if (raw.Count(c => c == '.') > 1 || (raw.Contains('.') && raw.Length - raw.LastIndexOf('.') - 1 == 3))
            raw = raw.Replace(".", "");
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static Regex R(string pattern) => new(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
