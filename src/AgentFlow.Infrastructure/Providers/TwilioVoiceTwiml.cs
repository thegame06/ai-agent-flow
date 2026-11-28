using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentFlow.Infrastructure.Providers;

/// <summary>
/// TwiML for a phone conversation with the assistant: Twilio speaks the assistant's text (Amazon
/// Polly, Mexican Spanish) and transcribes what the customer says (&lt;Gather input="speech"&gt;),
/// posting it to AgentFlow, which answers with the next turn. No separate STT/TTS provider is needed.
/// </summary>
public static class TwilioVoiceTwiml
{
    public const string Language = "es-MX";
    public const string Voice = "Polly.Mia";
    public const string Goodbye = "Gracias por tu tiempo. Hasta luego.";
    public const string DefaultGreeting = "Hola, gracias por llamar. ¿En qué te puedo ayudar?";
    /// <summary>Longest text spoken in one turn; a phone reply longer than this loses the caller.</summary>
    public const int MaxSpokenLength = 900;

    private static readonly Regex Url = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MarkdownSymbols = new(@"[*_`#>~|]+", RegexOptions.Compiled);
    private static readonly Regex ListBullet = new(@"^\s*(?:[-•]|\d+[.)])\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex LongDigits = new(@"\d{6,}", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"[ \t]+", RegexOptions.Compiled);

    public static string Response(params string[] verbs) => $"<Response>{string.Concat(verbs)}</Response>";

    public static string Say(string text)
        => $"<Say language=\"{Language}\" voice=\"{Voice}\">{SecurityElement.Escape(Speakable(text))}</Say>";

    /// <summary>
    /// Speaks <paramref name="prompt"/> and listens; what the customer said (or nothing, after a
    /// silence) is posted to <paramref name="actionUrl"/>.
    /// </summary>
    public static string Gather(string actionUrl, string? prompt)
    {
        var inner = string.IsNullOrWhiteSpace(prompt) ? string.Empty : Say(prompt!);
        return $"<Gather input=\"speech\" language=\"{Language}\" speechTimeout=\"auto\" timeout=\"6\" " +
               $"actionOnEmptyResult=\"true\" method=\"POST\" action=\"{SecurityElement.Escape(actionUrl)}\">{inner}</Gather>";
    }

    public static string Hangup() => "<Hangup/>";

    /// <summary>
    /// The assistant writes for a chat screen; on the phone, Markdown, links and emojis are noise and
    /// long numbers (accounts, references) must be read digit by digit.
    /// </summary>
    public static string Speakable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var result = Url.Replace(text, "el enlace que te enviamos por mensaje");
        result = ListBullet.Replace(result, string.Empty);
        result = MarkdownSymbols.Replace(result, string.Empty);
        result = LongDigits.Replace(result, m => string.Join(" ", m.Value.ToCharArray()));
        result = RemoveEmoji(result);

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .Select(line => line.EndsWith('.') || line.EndsWith('?') || line.EndsWith('!') || line.EndsWith(':') || line.EndsWith(',')
                ? line
                : line + ".");
        result = Spaces.Replace(string.Join(" ", lines), " ").Trim();

        if (result.Length > MaxSpokenLength)
        {
            var cut = result.LastIndexOf('.', MaxSpokenLength);
            result = cut > MaxSpokenLength / 2 ? result[..(cut + 1)] : result[..MaxSpokenLength];
        }
        return result;
    }

    private static string RemoveEmoji(string text)
    {
        var builder = new StringBuilder(text.Length);
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            var element = e.GetTextElement();
            var category = CharUnicodeInfo.GetUnicodeCategory(element, 0);
            var isEmoji = category is UnicodeCategory.OtherSymbol or UnicodeCategory.Surrogate
                          || char.IsSurrogate(element[0])
                          || element.Any(c => c is '️' or '‍');
            if (!isEmoji) builder.Append(element);
        }
        return builder.ToString();
    }
}
