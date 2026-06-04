namespace AgentFlow.Core.Engine;

internal static class ChannelGatewayResponseInterpreter
{
    internal sealed record HandoffDirective(string TargetAgentId, string Intent, string PayloadJson);
    internal sealed record RoutingHandoffDirective(string WorkflowBrainAgentId, string? WorkflowExecutionId, string? Intent);
    internal sealed record FallbackDirective(
        string CustomerMessage,
        string State,
        int NextTurn,
        bool RequiresHumanReview,
        string? ReasonCode,
        string? EscalationTarget,
        bool SuppressCustomerReply);

    public static HandoffDirective? TryParseHandoffDirective(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "handoff", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!root.TryGetProperty("targetAgentId", out var targetEl) || string.IsNullOrWhiteSpace(targetEl.GetString()))
                return null;

            var intent = root.TryGetProperty("intent", out var intentEl) && !string.IsNullOrWhiteSpace(intentEl.GetString())
                ? intentEl.GetString()!
                : "delegated_task";

            var payloadJson = root.TryGetProperty("payload", out var payloadEl)
                ? payloadEl.GetRawText()
                : "{}";

            return new HandoffDirective(targetEl.GetString()!, intent, payloadJson);
        }
        catch
        {
            return null;
        }
    }

    public static RoutingHandoffDirective? TryParseRoutingHandoff(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "routing_handoff", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!root.TryGetProperty("workflowBrainAgentId", out var agentEl) ||
                string.IsNullOrWhiteSpace(agentEl.GetString()))
                return null;

            var execId = root.TryGetProperty("workflowExecutionId", out var execEl) ? execEl.GetString() : null;
            var intent = root.TryGetProperty("intent", out var intentEl) ? intentEl.GetString() : null;
            return new RoutingHandoffDirective(agentEl.GetString()!, execId, intent);
        }
        catch { return null; }
    }

    public static FallbackDirective? TryParseFallbackDirective(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "routing_fallback", StringComparison.OrdinalIgnoreCase))
                return null;
            var suppressCustomerReply = root.TryGetProperty("suppressCustomerReply", out var suppressEl)
                && suppressEl.ValueKind == System.Text.Json.JsonValueKind.True;
            if (!root.TryGetProperty("customerMessage", out var msgEl) &&
                !suppressCustomerReply)
                return null;

            var state = root.TryGetProperty("state", out var stateEl) ? (stateEl.GetString() ?? "inactive") : "inactive";
            var nextTurn = root.TryGetProperty("nextTurn", out var turnEl) && turnEl.TryGetInt32(out var t) ? t : 0;
            var requiresHumanReview = root.TryGetProperty("requiresHumanReview", out var rrEl)
                && rrEl.ValueKind == System.Text.Json.JsonValueKind.True;
            var reasonCode = root.TryGetProperty("reasonCode", out var reasonEl) ? reasonEl.GetString() : null;
            var escalationTarget = root.TryGetProperty("escalationTarget", out var etEl) ? etEl.GetString() : null;
            var customerMessage = msgEl.ValueKind == System.Text.Json.JsonValueKind.String
                ? (msgEl.GetString() ?? string.Empty)
                : string.Empty;
            return new FallbackDirective(customerMessage, state, nextTurn, requiresHumanReview, reasonCode, escalationTarget, suppressCustomerReply);
        }
        catch
        {
            return null;
        }
    }

    public static bool ShouldSuppressCustomerDelivery(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return false;
        var text = response.Trim();
        var lower = text.ToLowerInvariant();

        var hasInternalNoun =
            lower.Contains("herramienta", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("tool", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("tenant", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("workflow", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("sesión", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("sesion", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("mcp", StringComparison.OrdinalIgnoreCase);

        var hasFailureVerb =
            lower.Contains("no está disponible", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("no esta disponible", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("error", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("no hay suficiente", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("not available", StringComparison.OrdinalIgnoreCase);

        var looksLikeInternalPayload =
            (text.StartsWith("{", StringComparison.Ordinal) && text.EndsWith("}", StringComparison.Ordinal))
            || lower.Contains("\"errorcode\"", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("stacktrace", StringComparison.OrdinalIgnoreCase);

        return looksLikeInternalPayload || (hasInternalNoun && hasFailureVerb);
    }

    public static string? ExtractResponseText(string? responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
            return responseJson;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (root.TryGetProperty("message", out var msg) && msg.ValueKind == System.Text.Json.JsonValueKind.String)
                    return msg.GetString();
                if (root.TryGetProperty("finalResponse", out var final) && final.ValueKind == System.Text.Json.JsonValueKind.String)
                    return final.GetString();
            }
        }
        catch
        {
        }

        return responseJson;
    }
}
