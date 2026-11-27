using System.Text.Json;

namespace AgentFlow.Infrastructure.Channels.WhatsApp;

/// <summary>
/// An approved template attached to an outgoing message (metadata "wa.template": the object Meta
/// expects under "template": name, language.code and components).
/// </summary>
public sealed record WhatsAppTemplatePayload(string Name, string Language, string? ComponentsJson)
{
    public const string MetadataKey = "wa.template";

    public static WhatsAppTemplatePayload? From(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || !metadata.TryGetValue(MetadataKey, out var json) || string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) return null;
            var language = root.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.Object && l.TryGetProperty("code", out var code)
                ? code.GetString() ?? "es"
                : "es";
            var components = root.TryGetProperty("components", out var c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() > 0
                ? c.GetRawText()
                : null;
            return new WhatsAppTemplatePayload(name, language, components);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
