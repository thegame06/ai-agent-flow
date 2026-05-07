using System.Text;
using System.Text.Json;

namespace AgentFlow.Api.Workflow;

public interface IWorkflowSecurityPolicyService
{
    void ValidateDefinitionOrThrow(string definitionJson);
    void ValidatePayloadOrThrow(Dictionary<string, object?>? payload);
    bool IsAllowedActivityType(string activityType);
}

public sealed class WorkflowSecurityPolicyService : IWorkflowSecurityPolicyService
{
    private static readonly HashSet<string> AllowedActivityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "connect.send_whatsapp_template",
        "connect.update_inbox_status",
        "connect.enqueue_campaign_message"
    };

    private const int MaxActivities = 100;
    private const int MaxTimeoutMs = 120000;
    private const int MaxRetryCount = 5;
    private const int MaxRetryDelayMs = 30000;
    private const int MaxPayloadBytes = 65536;

    public void ValidateDefinitionOrThrow(string definitionJson)
    {
        using var doc = JsonDocument.Parse(definitionJson);
        if (!doc.RootElement.TryGetProperty("activities", out var activities) || activities.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Workflow definition must include an activities array.");

        if (activities.GetArrayLength() > MaxActivities)
            throw new InvalidOperationException($"Workflow exceeds max activities ({MaxActivities}).");

        foreach (var activity in activities.EnumerateArray())
        {
            var type = activity.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(type) || !IsAllowedActivityType(type))
                throw new InvalidOperationException($"Activity type '{type ?? "(null)"}' is not allowed.");

            var timeoutMs = activity.TryGetProperty("timeoutMs", out var timeoutValue) && timeoutValue.TryGetInt32(out var t) ? t : 30000;
            var retryCount = activity.TryGetProperty("retryCount", out var retryValue) && retryValue.TryGetInt32(out var r) ? r : 0;
            var retryDelayMs = activity.TryGetProperty("retryDelayMs", out var delayValue) && delayValue.TryGetInt32(out var d) ? d : 0;

            if (timeoutMs <= 0 || timeoutMs > MaxTimeoutMs)
                throw new InvalidOperationException($"Activity '{type}' timeoutMs must be between 1 and {MaxTimeoutMs}.");
            if (retryCount < 0 || retryCount > MaxRetryCount)
                throw new InvalidOperationException($"Activity '{type}' retryCount must be between 0 and {MaxRetryCount}.");
            if (retryDelayMs < 0 || retryDelayMs > MaxRetryDelayMs)
                throw new InvalidOperationException($"Activity '{type}' retryDelayMs must be between 0 and {MaxRetryDelayMs}.");
        }
    }

    public void ValidatePayloadOrThrow(Dictionary<string, object?>? payload)
    {
        var json = JsonSerializer.Serialize(payload ?? new Dictionary<string, object?>());
        var byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > MaxPayloadBytes)
            throw new InvalidOperationException($"Payload exceeds max size ({MaxPayloadBytes} bytes).");
    }

    public bool IsAllowedActivityType(string activityType) => AllowedActivityTypes.Contains(activityType);
}
