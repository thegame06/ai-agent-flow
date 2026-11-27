using System.Text.RegularExpressions;
using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Core.Engine;

/// <summary>
/// Rewrites an agent's reply right before it is sent. Used to put in data the model must not
/// transcribe itself (bank accounts, references): the agent writes a placeholder and a transformer
/// replaces it with the exact text.
/// </summary>
public interface IChannelReplyTransformer
{
    Task<string> TransformAsync(ChannelMessage incoming, ChannelDefinition channel, string reply, CancellationToken ct = default);
}

/// <summary>Channel-specific formatting of agent replies.</summary>
public static class ChannelReplyFormatting
{
    private static readonly Regex DoubleStar = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex DoubleUnderscore = new(@"__(.+?)__", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Heading = new(@"^\s{0,3}#{1,6}\s+", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// WhatsApp bolds with *one* asterisk and has no headings: models write Markdown anyway
    /// ("**cuenta**", "### Opciones"), which shows up as stray symbols on the phone.
    /// </summary>
    public static string ForWhatsApp(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var result = DoubleStar.Replace(text, "*$1*");
        result = DoubleUnderscore.Replace(result, "_$1_");
        return Heading.Replace(result, string.Empty);
    }

    public static string ForChannel(ChannelType type, string text)
        => type == ChannelType.WhatsApp ? ForWhatsApp(text) : text;
}
