using System.Text.RegularExpressions;

namespace AgentFlow.Api.Campaigns;

/// <summary>Fills <c>{{variable}}</c> placeholders of a campaign message with a contact's variables.</summary>
public static partial class CampaignMessageRenderer
{
    [GeneratedRegex(@"\{\{\s*([\w\.]+)\s*\}\}")]
    private static partial Regex Token();

    /// <summary>Renders the template; <c>Missing</c> lists placeholders the contact has no value for.</summary>
    public static (string Text, IReadOnlyList<string> Missing) Render(string template, IReadOnlyDictionary<string, string>? variables)
    {
        var missing = new List<string>();
        var lookup = variables is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);

        var text = Token().Replace(template ?? string.Empty, match =>
        {
            var key = match.Groups[1].Value;
            if (key.StartsWith("variables.", StringComparison.OrdinalIgnoreCase))
                key = key["variables.".Length..];
            if (lookup.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;
            missing.Add(key);
            return match.Value;
        });

        return (text, missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Placeholder names used by a template.</summary>
    public static IReadOnlyList<string> Placeholders(string? template) =>
        Token().Matches(template ?? string.Empty).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
