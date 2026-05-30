using System.Text.Json;
using AgentFlow.Abstractions.Workflows;
using AgentFlow.Abstractions.Workflow;

namespace AgentFlow.Api.Workflow;

public sealed class WorkflowRoutingCatalog : IWorkflowRoutingCatalog
{
    private readonly IWorkflowStudioStore _store;

    public WorkflowRoutingCatalog(IWorkflowStudioStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<WorkflowRoutingCandidate>> ListPublishedCandidatesAsync(
        string tenantId,
        string channel,
        CancellationToken ct = default)
    {
        var definitions = await _store.GetDefinitionsAsync(tenantId, ct);
        var expectedEvent = EventForChannel(channel);

        return definitions
            .Where(x => x.Status == WorkflowDefinitionStatus.Published)
            .Where(x => string.Equals(x.TriggerEventName, expectedEvent, StringComparison.OrdinalIgnoreCase))
            .SelectMany(ToCandidates)
            .Where(x => !string.IsNullOrWhiteSpace(x.IntentKey) && !string.IsNullOrWhiteSpace(x.WorkflowDefinitionId))
            .ToList();
    }

    private static IEnumerable<WorkflowRoutingCandidate> ToCandidates(WorkflowDefinitionContract definition)
    {
        var workflowDescription = ReadWorkflowDescription(definition);
        var targetAgentId = ReadFirstWorkflowAgentId(definition.DefinitionJson);
        var intents = ReadStartIntents(definition.DefinitionJson, definition.TriggerEventName);

        foreach (var intent in intents)
        {
            var rawIntentKey = !string.IsNullOrWhiteSpace(intent.Label) ? intent.Label! : intent.Id;
            var intentKey = NormalizeIntentKey(rawIntentKey);
            if (string.IsNullOrWhiteSpace(intentKey))
                continue;

            yield return new WorkflowRoutingCandidate
            {
                WorkflowDefinitionId = definition.Id,
                WorkflowName = definition.Name,
                WorkflowDescription = workflowDescription,
                TargetAgentId = targetAgentId,
                IntentKey = intentKey,
                IntentLabel = intent.Label,
                IntentDescription = intent.Description,
                ExamplePhrases = intent.Examples,
                ConfidenceThreshold = intent.ConfidenceThreshold ?? 0.7,
                TriggerEventName = definition.TriggerEventName
            };
        }
    }

    private static string EventForChannel(string? channel) => channel?.Trim().ToLowerInvariant() switch
    {
        "voice" or "callcenter" => "connect.call.received",
        "email" => "connect.message.received",
        "whatsapp" or "webchat" or "telegram" or "slack" or "api" => "connect.message.received",
        _ => "connect.message.received"
    };

    private static IReadOnlyList<WorkflowStartIntentSnapshot> ReadStartIntents(string definitionJson, string eventName)
    {
        try
        {
            using var doc = JsonDocument.Parse(definitionJson);
            if (doc.RootElement.TryGetProperty("start", out var start) &&
                start.TryGetProperty("intents", out var intentsEl) &&
                intentsEl.ValueKind == JsonValueKind.Array)
            {
                var items = new List<WorkflowStartIntentSnapshot>();
                foreach (var item in intentsEl.EnumerateArray())
                {
                    items.Add(new WorkflowStartIntentSnapshot(
                        item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N"),
                        item.TryGetProperty("label", out var labelEl) ? labelEl.GetString() : null,
                        item.TryGetProperty("description", out var descEl) ? descEl.GetString() : null,
                        item.TryGetProperty("examples", out var exEl) && exEl.ValueKind == JsonValueKind.Array
                            ? exEl.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
                            : Array.Empty<string>(),
                        item.TryGetProperty("triggerSource", out var sourceEl) ? sourceEl.GetString() : "message",
                        item.TryGetProperty("confidenceThreshold", out var confEl) && confEl.ValueKind == JsonValueKind.Number
                            ? confEl.GetDouble()
                            : 0.7));
                }

                if (items.Count > 0)
                    return items;
            }
        }
        catch
        {
            // Ignore malformed definitions and fall back to a synthetic intent.
        }

        return new[]
        {
            new WorkflowStartIntentSnapshot("intent-main", "Intencion principal", $"Inicio para {eventName}", Array.Empty<string>(), "message", 0.7)
        };
    }

    private static string? ReadFirstWorkflowAgentId(string definitionJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(definitionJson);
            if (!doc.RootElement.TryGetProperty("activities", out var activities) || activities.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var activity in activities.EnumerateArray())
            {
                if (!activity.TryGetProperty("type", out var typeEl) ||
                    !string.Equals(typeEl.GetString(), "ai.agent", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (activity.TryGetProperty("config", out var configEl) &&
                    configEl.ValueKind == JsonValueKind.Object &&
                    configEl.TryGetProperty("agentId", out var agentIdEl) &&
                    !string.IsNullOrWhiteSpace(agentIdEl.GetString()))
                    return agentIdEl.GetString();

                if (activity.TryGetProperty("aiAgent", out var aiAgentEl) &&
                    aiAgentEl.ValueKind == JsonValueKind.Object &&
                    aiAgentEl.TryGetProperty("agentId", out var aiAgentIdEl) &&
                    !string.IsNullOrWhiteSpace(aiAgentIdEl.GetString()))
                    return aiAgentIdEl.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? ReadWorkflowDescription(WorkflowDefinitionContract definition)
    {
        if (definition.Metadata.TryGetValue("description", out var metadataDescription) &&
            !string.IsNullOrWhiteSpace(metadataDescription))
            return metadataDescription;

        try
        {
            using var doc = JsonDocument.Parse(definition.DefinitionJson);
            if (doc.RootElement.TryGetProperty("description", out var descriptionEl) &&
                descriptionEl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(descriptionEl.GetString()))
                return descriptionEl.GetString();
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string NormalizeIntentKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray());

        while (cleaned.Contains("__", StringComparison.Ordinal))
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);

        return cleaned.Trim('_');
    }

    private sealed record WorkflowStartIntentSnapshot(
        string Id,
        string? Label,
        string? Description,
        IReadOnlyList<string> Examples,
        string? TriggerSource,
        double? ConfidenceThreshold);
}
