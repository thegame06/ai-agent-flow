using System.Text.Json;

namespace AgentFlow.Api.Workflow;

/// <summary>
/// Builds the token context that workflow activity configs resolve against
/// (<c>{{payload.contact.recipient}}</c>, <c>{{steps.send.messageId}}</c>, ...).
/// </summary>
public static class WorkflowTemplateContext
{
    public const int MaxFlattenDepth = 6;

    /// <summary>
    /// Exposes a payload value as a token and, for objects, every nested field too:
    /// <c>{{payload.contact}}</c> (raw JSON) and <c>{{payload.contact.recipient}}</c>.
    /// Use a case-insensitive context so PascalCase payloads resolve camelCase tokens.
    /// </summary>
    public static void FlattenPayload(IDictionary<string, string> context, string key, JsonElement value, int depth = 0)
    {
        context[key] = value.ToString() ?? string.Empty;
        if (value.ValueKind != JsonValueKind.Object || depth >= MaxFlattenDepth) return;

        foreach (var property in value.EnumerateObject())
            FlattenPayload(context, $"{key}.{property.Name}", property.Value, depth + 1);
    }

    /// <summary>Context for a serialized trigger payload (keys prefixed with <c>payload.</c>).</summary>
    public static Dictionary<string, string> FromPayloadJson(string payloadJson)
    {
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson) ?? new();
        foreach (var (key, value) in payload)
            FlattenPayload(context, $"payload.{key}", value);
        return context;
    }

    /// <summary>Replaces every <c>{{key}}</c> present in the context; unknown tokens are left as-is.</summary>
    public static string ResolveTokens(string value, IReadOnlyDictionary<string, string> context)
    {
        var result = value;
        foreach (var (key, replacement) in context)
            result = result.Replace($"{{{{{key}}}}}", replacement, StringComparison.OrdinalIgnoreCase);
        return result;
    }
}
