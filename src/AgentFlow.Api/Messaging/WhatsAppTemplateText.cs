using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgentFlow.Api.Messaging;

/// <summary>A business field a template placeholder can be filled with (the contact's campaign variables).</summary>
public sealed record TemplateField(string Key, string Label, string Example, string Group);

/// <summary>A template ready to send: the Meta payload, the text the customer reads and what was missing.</summary>
public sealed record WhatsAppTemplateMessage(string Name, string Language, JsonArray Components, string Text, IReadOnlyList<string> Missing)
{
    /// <summary>The object Meta expects under "template" in a messages call.</summary>
    public string ToSendJson() => new JsonObject
    {
        ["name"] = Name,
        ["language"] = new JsonObject { ["code"] = Language },
        ["components"] = JsonNode.Parse(Components.ToJsonString())
    }.ToJsonString();
}

/// <summary>Placeholders, mapping, rendering and Meta payloads of WhatsApp templates (no I/O).</summary>
public static partial class WhatsAppTemplateText
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex Placeholder();

    /// <summary>The variables campaigns put on each contact, offered when mapping a template.</summary>
    public static readonly IReadOnlyList<TemplateField> Fields =
    [
        new("primer_nombre", "Primer nombre", "Ana", "Cliente"),
        new("nombre", "Nombre completo", "Ana López", "Cliente"),
        new("cliente_id", "Número de cliente", "CLI-1001", "Cliente"),
        new("total_vencido", "Saldo vencido", "1,350.00", "Cobranza"),
        new("saldo_total", "Saldo total", "8,000.00", "Cobranza"),
        new("moneda", "Moneda", "NIO", "Cobranza"),
        new("dias_atraso", "Días de atraso", "25", "Cobranza"),
        new("etapa", "Etapa de atraso", "1-30 días", "Cobranza"),
        new("num_prestamos", "Número de préstamos", "1", "Cobranza"),
        new("prestamos", "Préstamos", "PR-1001", "Cobranza"),
        new("proximo_pago_fecha", "Próximo pago (fecha)", "05/12/2026", "Cobranza"),
        new("proximo_pago_monto", "Próximo pago (monto)", "4,100.00", "Cobranza"),
        new("monto_promesa", "Monto prometido (seguimientos)", "500.00", "Cobranza"),
        new("fecha_promesa", "Fecha prometida (seguimientos)", "15/12/2026", "Cobranza"),
        new("compras", "Compras", "3", "Ventas"),
        new("total_comprado", "Total comprado", "12,500.00", "Ventas"),
        new("saldo_pendiente", "Saldo pendiente", "2,300.00", "Ventas"),
    ];

    private static readonly Dictionary<string, TemplateField> FieldsByKey = Fields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> PlaceholdersIn(string? text)
        => Placeholder().Matches(text ?? string.Empty).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Every slot a value goes into when sending: header/body placeholders, a media header, URL button suffixes.</summary>
    public static IReadOnlyList<(string Component, string Key)> Slots(IEnumerable<WhatsAppTemplateComponent> components)
    {
        var slots = new List<(string, string)>();
        foreach (var c in components)
        {
            switch (c.Type.ToUpperInvariant())
            {
                case "HEADER" when c.Format is null || c.Format.Equals("TEXT", StringComparison.OrdinalIgnoreCase):
                    slots.AddRange(PlaceholdersIn(c.Text).Select(k => ("header", k)));
                    break;
                case "HEADER" when c.Format is "IMAGE" or "VIDEO" or "DOCUMENT":
                    slots.Add(("header_media", "media"));
                    break;
                case "BODY":
                    slots.AddRange(PlaceholdersIn(c.Text).Select(k => ("body", k)));
                    break;
                case "BUTTONS":
                    for (var i = 0; i < (c.Buttons?.Count ?? 0); i++)
                        if (c.Buttons![i].Type.Equals("URL", StringComparison.OrdinalIgnoreCase))
                            slots.AddRange(PlaceholdersIn(c.Buttons[i].Url).Select(k => ($"button:{i}", k)));
                    break;
            }
        }
        return slots;
    }

    /// <summary>
    /// Keeps the mapping a person already made for each slot and adds the new ones: a named
    /// placeholder that is a known field ({{total_vencido}}) maps itself.
    /// </summary>
    public static List<WhatsAppTemplateParameter> MergeParameters(IEnumerable<WhatsAppTemplateComponent> components, IEnumerable<WhatsAppTemplateParameter>? existing)
    {
        // First mapping wins when the same slot comes more than once (callers pass "new, then old").
        var old = (existing ?? []).GroupBy(p => (p.Component, p.Key)).ToDictionary(g => g.Key, g => g.First());
        return Slots(components).Select(slot =>
        {
            if (old.TryGetValue(slot, out var kept)) return kept;
            var known = FieldsByKey.GetValueOrDefault(slot.Key);
            return new WhatsAppTemplateParameter
            {
                Component = slot.Component,
                Key = slot.Key,
                Source = known is null ? "static" : "field",
                Field = known?.Key,
                Example = known?.Example
            };
        }).ToList();
    }

    /// <summary>Changes when the content changes (not the status): name, language, category and components.</summary>
    public static string Hash(string name, string language, string category, IEnumerable<WhatsAppTemplateComponent> components)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            name,
            language,
            category = category.ToUpperInvariant(),
            components = components.Select(c => new
            {
                type = c.Type.ToUpperInvariant(),
                format = c.Format?.ToUpperInvariant(),
                text = c.Text?.Trim(),
                buttons = c.Buttons?.Select(b => new { type = b.Type.ToUpperInvariant(), text = b.Text, url = b.Url, phone = b.PhoneNumber })
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }

    /// <summary>
    /// Fills the template for one contact. A field with no value (or an empty static) is reported
    /// in Missing: the caller must not send a template with an empty parameter.
    /// </summary>
    public static WhatsAppTemplateMessage Build(WhatsAppTemplateDocument template, IReadOnlyDictionary<string, string>? variables)
    {
        var lookup = new Dictionary<string, string>(variables ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var parameters = MergeParameters(template.Components, template.Parameters).ToDictionary(p => (p.Component, p.Key));
        var named = template.ParameterFormat.Equals("NAMED", StringComparison.OrdinalIgnoreCase);
        var twilio = template.Provider == WhatsAppTemplateProviders.Twilio;
        var missing = new List<string>();

        string? Value(string component, string key)
        {
            var p = parameters.GetValueOrDefault((component, key));
            var value = p is null ? null
                : p.Source == "static" ? p.StaticValue
                : p.Field is null ? null : lookup.GetValueOrDefault(p.Field);
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(p?.Source == "field" && p.Field is not null ? p.Field : $"{component}:{{{{{key}}}}}");
                return null;
            }
            return value.Trim();
        }

        string Fill(string? text, string component) => Placeholder().Replace(text ?? string.Empty,
            m => Value(component, m.Groups[1].Value) ?? m.Value);

        JsonObject TextParam(string component, string key)
        {
            var param = new JsonObject { ["type"] = "text", ["text"] = Value(component, key) ?? string.Empty };
            // Twilio fills variables by their number ({{2}} may come before {{1}}), so it gets the key too.
            if (named || twilio) param["parameter_name"] = key;
            return param;
        }

        var payload = new JsonArray();
        var lines = new List<string>();
        foreach (var c in template.Components)
        {
            switch (c.Type.ToUpperInvariant())
            {
                case "HEADER" when c.Format is "IMAGE" or "VIDEO" or "DOCUMENT":
                {
                    var link = Value("header_media", "media");
                    var kind = c.Format!.ToLowerInvariant();
                    payload.Add(new JsonObject
                    {
                        ["type"] = "header",
                        ["parameters"] = new JsonArray(new JsonObject { ["type"] = kind, [kind] = new JsonObject { ["link"] = link ?? string.Empty } })
                    });
                    lines.Add($"[{kind}]");
                    break;
                }
                case "HEADER":
                {
                    var keys = PlaceholdersIn(c.Text);
                    if (keys.Count > 0)
                        payload.Add(new JsonObject { ["type"] = "header", ["parameters"] = new JsonArray(keys.Select(k => (JsonNode)TextParam("header", k)).ToArray()) });
                    lines.Add($"*{Fill(c.Text, "header")}*");
                    break;
                }
                case "BODY":
                {
                    var keys = PlaceholdersIn(c.Text);
                    if (keys.Count > 0)
                        payload.Add(new JsonObject { ["type"] = "body", ["parameters"] = new JsonArray(keys.Select(k => (JsonNode)TextParam("body", k)).ToArray()) });
                    lines.Add(Fill(c.Text, "body"));
                    break;
                }
                case "FOOTER":
                    lines.Add(c.Text ?? string.Empty);
                    break;
                case "BUTTONS":
                    for (var i = 0; i < (c.Buttons?.Count ?? 0); i++)
                    {
                        var b = c.Buttons![i];
                        if (b.Type.Equals("URL", StringComparison.OrdinalIgnoreCase))
                        {
                            var keys = PlaceholdersIn(b.Url);
                            if (keys.Count > 0)
                                payload.Add(new JsonObject
                                {
                                    ["type"] = "button",
                                    ["sub_type"] = "url",
                                    ["index"] = i.ToString(),
                                    ["parameters"] = new JsonArray(keys.Select(k => (JsonNode)new JsonObject { ["type"] = "text", ["text"] = Value($"button:{i}", k) ?? string.Empty }).ToArray())
                                });
                        }
                        lines.Add($"[{b.Text}]");
                    }
                    break;
            }
        }

        var text = string.Join("\n\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
        // Twilio sends a template by its Content SID, not by name.
        var sendName = twilio && !string.IsNullOrWhiteSpace(template.ContentSid) ? template.ContentSid! : template.Name;
        return new WhatsAppTemplateMessage(sendName, template.Language, payload, text, missing.Distinct().ToList());
    }

    /// <summary>
    /// What Meta needs to review a new or edited template: the components with an example for
    /// every placeholder (positional examples in order, named ones by name).
    /// </summary>
    public static JsonArray ToMetaComponents(IEnumerable<WhatsAppTemplateComponent> components, IReadOnlyList<WhatsAppTemplateParameter> parameters, string parameterFormat)
    {
        var named = parameterFormat.Equals("NAMED", StringComparison.OrdinalIgnoreCase);
        var examples = parameters.ToDictionary(p => (p.Component, p.Key), p => p.Example ?? FieldsByKey.GetValueOrDefault(p.Field ?? string.Empty)?.Example ?? p.StaticValue ?? "ejemplo");
        string Example(string component, string key) => examples.GetValueOrDefault((component, key)) ?? "ejemplo";

        var result = new JsonArray();
        foreach (var c in components)
        {
            var type = c.Type.ToUpperInvariant();
            var node = new JsonObject { ["type"] = type };
            switch (type)
            {
                case "HEADER":
                    node["format"] = (c.Format ?? "TEXT").ToUpperInvariant();
                    if ((c.Format ?? "TEXT").Equals("TEXT", StringComparison.OrdinalIgnoreCase))
                    {
                        node["text"] = c.Text;
                        var keys = PlaceholdersIn(c.Text);
                        if (keys.Count > 0)
                            node["example"] = named
                                ? new JsonObject { ["header_text_named_params"] = new JsonArray(keys.Select(k => (JsonNode)new JsonObject { ["param_name"] = k, ["example"] = Example("header", k) }).ToArray()) }
                                : new JsonObject { ["header_text"] = new JsonArray(keys.Select(k => (JsonNode)JsonValue.Create(Example("header", k))!).ToArray()) };
                    }
                    else
                    {
                        node["example"] = new JsonObject { ["header_handle"] = new JsonArray(JsonValue.Create(Example("header_media", "media"))) };
                    }
                    break;
                case "BODY":
                {
                    node["text"] = c.Text;
                    var keys = PlaceholdersIn(c.Text);
                    if (keys.Count > 0)
                        node["example"] = named
                            ? new JsonObject { ["body_text_named_params"] = new JsonArray(keys.Select(k => (JsonNode)new JsonObject { ["param_name"] = k, ["example"] = Example("body", k) }).ToArray()) }
                            : new JsonObject { ["body_text"] = new JsonArray(new JsonArray(keys.Select(k => (JsonNode)JsonValue.Create(Example("body", k))!).ToArray())) };
                    break;
                }
                case "FOOTER":
                    node["text"] = c.Text;
                    break;
                case "BUTTONS":
                {
                    var buttons = new JsonArray();
                    for (var i = 0; i < (c.Buttons?.Count ?? 0); i++)
                    {
                        var b = c.Buttons![i];
                        var button = new JsonObject { ["type"] = b.Type.ToUpperInvariant(), ["text"] = b.Text };
                        if (!string.IsNullOrWhiteSpace(b.Url))
                        {
                            button["url"] = b.Url;
                            var keys = PlaceholdersIn(b.Url);
                            if (keys.Count > 0)
                                button["example"] = new JsonArray(JsonValue.Create(Placeholder().Replace(b.Url, m => Example($"button:{i}", m.Groups[1].Value))));
                        }
                        if (!string.IsNullOrWhiteSpace(b.PhoneNumber)) button["phone_number"] = b.PhoneNumber;
                        buttons.Add(button);
                    }
                    node["buttons"] = buttons;
                    break;
                }
            }
            result.Add(node);
        }
        return result;
    }

    /// <summary>Reads the "components" array Meta returns for a template.</summary>
    public static List<WhatsAppTemplateComponent> FromMetaComponents(JsonElement components)
    {
        var list = new List<WhatsAppTemplateComponent>();
        if (components.ValueKind != JsonValueKind.Array) return list;
        foreach (var c in components.EnumerateArray())
        {
            var component = new WhatsAppTemplateComponent
            {
                Type = Str(c, "type")?.ToUpperInvariant() ?? "BODY",
                Format = Str(c, "format")?.ToUpperInvariant(),
                Text = Str(c, "text")
            };
            if (c.TryGetProperty("buttons", out var buttons) && buttons.ValueKind == JsonValueKind.Array)
                component.Buttons = buttons.EnumerateArray().Select(b => new WhatsAppTemplateButton
                {
                    Type = Str(b, "type")?.ToUpperInvariant() ?? "QUICK_REPLY",
                    Text = Str(b, "text") ?? string.Empty,
                    Url = Str(b, "url"),
                    PhoneNumber = Str(b, "phone_number")
                }).ToList();
            list.Add(component);
        }
        return list;
    }

    /// <summary>Meta's examples for an existing template, used as the default sample values.</summary>
    public static Dictionary<(string Component, string Key), string> ExamplesFromMeta(JsonElement components)
    {
        var examples = new Dictionary<(string, string), string>();
        if (components.ValueKind != JsonValueKind.Array) return examples;
        foreach (var c in components.EnumerateArray())
        {
            var type = Str(c, "type")?.ToUpperInvariant();
            if (type is not ("HEADER" or "BODY") || !c.TryGetProperty("example", out var example)) continue;
            var component = type == "HEADER" ? "header" : "body";
            var keys = PlaceholdersIn(Str(c, "text"));
            if (example.TryGetProperty(component == "header" ? "header_text" : "body_text", out var positional))
            {
                var values = positional.ValueKind == JsonValueKind.Array && positional.GetArrayLength() > 0 && positional[0].ValueKind == JsonValueKind.Array
                    ? positional[0].EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToList()
                    : positional.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty).ToList();
                for (var i = 0; i < Math.Min(keys.Count, values.Count); i++) examples[(component, keys[i])] = values[i];
            }
            if (example.TryGetProperty(component == "header" ? "header_text_named_params" : "body_text_named_params", out var namedParams) && namedParams.ValueKind == JsonValueKind.Array)
                foreach (var p in namedParams.EnumerateArray())
                    if (Str(p, "param_name") is { } name) examples[(component, name)] = Str(p, "example") ?? string.Empty;
        }
        return examples;
    }

    /// <summary>
    /// Editor helper (as in the reference design): authors write {{primer_nombre}}; for a
    /// positional template the placeholders are renumbered {{1}}..{{n}} in order of appearance,
    /// across header and body, and each number keeps its field.
    /// </summary>
    public static (List<WhatsAppTemplateComponent> Components, List<WhatsAppTemplateParameter> Parameters) ToPositional(IEnumerable<WhatsAppTemplateComponent> components)
    {
        var copy = components.Select(c => new WhatsAppTemplateComponent { Type = c.Type, Format = c.Format, Text = c.Text, Buttons = c.Buttons }).ToList();
        var parameters = new List<WhatsAppTemplateParameter>();
        foreach (var c in copy.Where(c => c.Type.ToUpperInvariant() is "HEADER" or "BODY"))
        {
            var component = c.Type.Equals("HEADER", StringComparison.OrdinalIgnoreCase) ? "header" : "body";
            var numbering = new Dictionary<string, string>(StringComparer.Ordinal);
            c.Text = Placeholder().Replace(c.Text ?? string.Empty, m =>
            {
                var name = m.Groups[1].Value;
                if (!numbering.TryGetValue(name, out var number))
                {
                    number = (numbering.Count + 1).ToString();
                    numbering[name] = number;
                    var field = FieldsByKey.GetValueOrDefault(name);
                    parameters.Add(new WhatsAppTemplateParameter
                    {
                        Component = component,
                        Key = number,
                        Source = field is null ? "static" : "field",
                        Field = field?.Key,
                        // Not a known field: a fixed value the person fills in when mapping.
                        Example = field?.Example ?? name
                    });
                }
                return "{{" + number + "}}";
            });
        }
        return (copy, parameters);
    }

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
