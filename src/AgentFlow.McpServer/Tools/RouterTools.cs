using System.Text.Json;
using AgentFlow.McpServer.Client;

namespace AgentFlow.McpServer.Tools;

// ─────────────────────────────────────────────────────────────────────────────
// ROUTER TOOLS
// Usadas por el agente Router para detectar intenciones y disparar workflows.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Lista todos los workflows publicados del tenant.
/// El Router la usa para saber qué workflows puede disparar dado una intención.
/// </summary>
public sealed class ListWorkflowsTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public ListWorkflowsTool(AgentFlowApiClient api) => _api = api;

    public string Name => "af_list_workflows";

    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Lists all published workflows available in the tenant. Use this to know which workflows can be triggered for a given customer intent.",
        IntendedFor = "router",
        InputSchemaJson = "{}"
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var workflows = await _api.ListWorkflowsAsync(req.TenantId, ct);
        return McpInvokeResult.Success(Name, req.TenantId,
            new { count = workflows.Count, workflows }, req.ExecutionId);
    }
}

/// <summary>
/// Dispara un workflow específico mediante un evento de dominio.
/// El Router la usa cuando detecta la intención del cliente.
/// </summary>
public sealed class TriggerWorkflowTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public TriggerWorkflowTool(AgentFlowApiClient api) => _api = api;

    public string Name => "af_trigger_workflow";

    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Triggers a workflow by firing a domain event. Use when a customer intent matches a known workflow. Provide the event name and any relevant payload extracted from the conversation.",
        IntendedFor = "router",
        InputSchemaJson = """
        {
          "type": "object",
          "required": ["eventName"],
          "properties": {
            "eventName":     { "type": "string", "description": "Domain event name that triggers the workflow (e.g. 'loan.application.started')" },
            "requestedBy":   { "type": "string", "description": "User identifier or phone number that initiated the request" },
            "correlationId": { "type": "string", "description": "Optional session or conversation ID for traceability" },
            "payload":       { "type": "object", "description": "Key-value data extracted from the conversation to pass to the workflow" }
          }
        }
        """
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = ParseInput(req.InputJson);
        var eventName = input.GetString("eventName");
        if (string.IsNullOrWhiteSpace(eventName))
            return McpInvokeResult.Fail(Name, "eventName is required");

        var requestedBy = input.GetString("requestedBy") ?? req.TenantId;
        var correlationId = input.GetString("correlationId");
        var payload = input.TryGetProperty("payload", out var payloadEl)
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadEl.GetRawText())
            : null;

        var result = await _api.TriggerWorkflowAsync(req.TenantId, eventName, requestedBy, correlationId, payload, ct);

        if (result == null)
            return McpInvokeResult.Fail(Name, $"No published workflow found for event '{eventName}'");

        return McpInvokeResult.Success(Name, req.TenantId,
            new { executionId = result.Id, status = result.Status, workflowId = result.WorkflowDefinitionId }, req.ExecutionId);
    }

    private static JsonElement ParseInput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return default; }
    }
}

/// <summary>
/// Recupera el contexto de sesión activa de un usuario en un canal.
/// El Router y el WorkflowBrain la usan para personalizar la respuesta.
/// </summary>
public sealed class GetSessionContextTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public GetSessionContextTool(AgentFlowApiClient api) => _api = api;

    public string Name => "af_get_session_context";

    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Retrieves the current session context for a user (display name, channel, window status, expiry). Use to personalize responses and determine if a WhatsApp template message is required.",
        IntendedFor = "any",
        InputSchemaJson = """
        {
          "type": "object",
          "required": ["sessionId"],
          "properties": {
            "sessionId": { "type": "string", "description": "The active session ID" }
          }
        }
        """
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = ParseInput(req.InputJson);
        var sessionId = input.GetString("sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            return McpInvokeResult.Fail(Name, "sessionId is required");

        var session = await _api.GetSessionContextAsync(req.TenantId, sessionId, ct);
        if (session == null)
            return McpInvokeResult.Fail(Name, $"Session '{sessionId}' not found or expired");

        return McpInvokeResult.Success(Name, req.TenantId, session, req.ExecutionId);
    }

    private static JsonElement ParseInput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return default; }
    }
}
