using AgentFlow.Abstractions;

namespace AgentFlow.Api.Workflow;

public static class RuntimeCompatibilityPolicy
{
    public static bool TryParseRuntimeKind(string? raw, out AgentRuntimeKind kind, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            kind = AgentRuntimeKind.Text;
            normalized = AgentRuntimeKind.Text.ToString();
            return true;
        }

        if (Enum.TryParse<AgentRuntimeKind>(raw, true, out kind))
        {
            normalized = kind.ToString();
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public static bool IsTriggerEventCompatible(AgentRuntimeKind runtimeKind, string? triggerEventName)
    {
        if (string.IsNullOrWhiteSpace(triggerEventName))
            return true;

        var lower = triggerEventName.Trim().ToLowerInvariant();
        if (lower.Contains("call.", StringComparison.Ordinal))
            return runtimeKind == AgentRuntimeKind.Voice;

        if (lower.Contains("realtime", StringComparison.Ordinal) || lower.Contains("video", StringComparison.Ordinal))
            return runtimeKind == AgentRuntimeKind.MultimodalRealtime;

        if (lower.Contains("message.", StringComparison.Ordinal))
            return runtimeKind == AgentRuntimeKind.Text;

        return true;
    }

    public static bool TryParseRuntimeKindFromTrigger(string? triggerEventName, out AgentRuntimeKind runtimeKind)
    {
        runtimeKind = AgentRuntimeKind.Text;
        if (string.IsNullOrWhiteSpace(triggerEventName))
            return false;

        var lower = triggerEventName.Trim().ToLowerInvariant();
        if (lower.Contains("call.", StringComparison.Ordinal))
        {
            runtimeKind = AgentRuntimeKind.Voice;
            return true;
        }

        if (lower.Contains("realtime", StringComparison.Ordinal) || lower.Contains("video", StringComparison.Ordinal))
        {
            runtimeKind = AgentRuntimeKind.MultimodalRealtime;
            return true;
        }

        if (lower.Contains("message.", StringComparison.Ordinal))
        {
            runtimeKind = AgentRuntimeKind.Text;
            return true;
        }

        return false;
    }

    public static bool IsAgentCompatible(AgentRuntimeKind workflowRuntime, AgentRuntimeKind agentRuntime)
        => workflowRuntime == agentRuntime;

    public static string BuildTriggerError(AgentRuntimeKind runtimeKind, string triggerEventName)
        => $"El runtime '{runtimeKind}' no es compatible con el evento '{triggerEventName}'. " +
           "Usa Text para mensajes, Voice para llamadas o MultimodalRealtime para eventos realtime.";

    public static string BuildAgentRuntimeError(string agentName, string agentId, AgentRuntimeKind workflowRuntime, AgentRuntimeKind agentRuntime)
        => $"El agente '{agentName}' (id: {agentId}) usa runtime '{agentRuntime}' y no es compatible con el workflow runtime '{workflowRuntime}'. " +
           "Selecciona un agente del mismo runtime o cambia el runtime del flujo.";
}
