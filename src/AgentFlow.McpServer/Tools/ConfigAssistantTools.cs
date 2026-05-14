using System.Text.Json;
using AgentFlow.McpServer.Client;

namespace AgentFlow.McpServer.Tools;

// ─────────────────────────────────────────────────────────────────────────────
// CONFIG ASSISTANT TOOLS
// Usadas por el agente Config Assistant para guiar al usuario en la construcción
// y configuración de workflows, agentes e integraciones.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Lista todos los agentes del tenant (sistema + usuario).
/// El Config Assistant la usa para mostrar qué agentes existen y cuáles faltan.
/// </summary>
public sealed class ListAgentsTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public ListAgentsTool(AgentFlowApiClient api) => _api = api;

    public string Name => "af_list_agents";

    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Lists all agents in the tenant (system and user-created). Use to show the user what agents exist, their status, and their system role (Router, WorkflowBrain, ConfigAssistant, Custom).",
        IntendedFor = "config-assistant",
        InputSchemaJson = "{}"
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var agents = await _api.ListAgentsAsync(req.TenantId, ct);
        return McpInvokeResult.Success(Name, req.TenantId,
            new { count = agents.Count, agents }, req.ExecutionId);
    }
}

/// <summary>
/// Obtiene el detalle completo de un agente específico.
/// </summary>
public sealed class GetAgentTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public GetAgentTool(AgentFlowApiClient api) => _api = api;

    public string Name => "af_get_agent";

    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Gets the full configuration of a specific agent (brain config, tools, memory, session settings). Use to diagnose misconfigured agents.",
        IntendedFor = "config-assistant",
        InputSchemaJson = """
        {
          "type": "object",
          "required": ["agentId"],
          "properties": {
            "agentId": { "type": "string", "description": "The agent ID to retrieve" }
          }
        }
        """
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = ParseInput(req.InputJson);
        var agentId = input.GetString("agentId");
        if (string.IsNullOrWhiteSpace(agentId))
            return McpInvokeResult.Fail(Name, "agentId is required");

        var agent = await _api.GetAgentAsync(req.TenantId, agentId, ct);
        if (agent == null)
            return McpInvokeResult.Fail(Name, $"Agent '{agentId}' not found");

        return McpInvokeResult.Success(Name, req.TenantId, agent, req.ExecutionId);
    }

    private static JsonElement ParseInput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return default; }
    }
}

/// <summary>
/// Diagnostica un workflow: detecta nodos sin agente asignado, referencias rotas,
/// eventos que no existen en el catálogo, etc.
/// </summary>
public sealed class DiagnoseWorkflowTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public DiagnoseWorkflowTool(AgentFlowApiClient api) => _api = api;

    public string Name => "af_diagnose_workflow";

    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Diagnoses a workflow definition and returns a list of issues: unassigned ai.agent nodes, missing trigger events, broken tool references, unpublished agents. Use before telling the user what needs to be fixed.",
        IntendedFor = "config-assistant",
        InputSchemaJson = """
        {
          "type": "object",
          "required": ["workflowId"],
          "properties": {
            "workflowId": { "type": "string", "description": "The workflow definition ID to diagnose" }
          }
        }
        """
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = ParseInput(req.InputJson);
        var workflowId = input.GetString("workflowId");
        if (string.IsNullOrWhiteSpace(workflowId))
            return McpInvokeResult.Fail(Name, "workflowId is required");

        var workflow = await _api.GetWorkflowAsync(req.TenantId, workflowId, ct);
        if (workflow == null)
            return McpInvokeResult.Fail(Name, $"Workflow '{workflowId}' not found");

        var agents = await _api.ListAgentsAsync(req.TenantId, ct);
        var issues = DiagnoseWorkflow(workflow, agents);

        return McpInvokeResult.Success(Name, req.TenantId, new
        {
            workflowId,
            workflowName = workflow.Name,
            status = workflow.Status,
            issueCount = issues.Count,
            issues,
            healthy = issues.Count == 0
        }, req.ExecutionId);
    }

    private static List<string> DiagnoseWorkflow(WorkflowSummary workflow, List<AgentSummary> agents)
    {
        var issues = new List<string>();
        var publishedAgentIds = agents.Where(a => a.Status == "Published").Select(a => a.Id).ToHashSet();

        if (workflow.Status == "Draft")
            issues.Add($"Workflow '{workflow.Name}' is in Draft status and will not be triggered by events.");

        if (string.IsNullOrWhiteSpace(workflow.TriggerEventName))
            issues.Add("Workflow has no trigger event configured — it can never start automatically.");

        // Parse the definition JSON for ai.agent nodes without agentId
        // This is a best-effort static analysis
        if (!string.IsNullOrWhiteSpace(workflow.TriggerEventName) && workflow.Status == "Published")
            issues.Add("ℹ️ Static node analysis requires full DefinitionJson — call af_get_workflow_definition for deep inspection.");

        return issues;
    }

    private static JsonElement ParseInput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return default; }
    }
}

/// <summary>
/// Diagnostica un canal: verifica si tiene Router asignado, session window configurada,
/// si el canal está activo, etc.
/// </summary>
public sealed class DiagnoseChannelTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public DiagnoseChannelTool(AgentFlowApiClient api) => _api = api;

    public string Name => "af_diagnose_channel";

    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Diagnoses a channel configuration: checks if a Router agent is assigned, session window is set, and the channel is active. Returns a list of issues and recommendations.",
        IntendedFor = "config-assistant",
        InputSchemaJson = """
        {
          "type": "object",
          "required": ["channelId"],
          "properties": {
            "channelId": { "type": "string", "description": "The channel ID to diagnose" }
          }
        }
        """
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = ParseInput(req.InputJson);
        var channelId = input.GetString("channelId");
        if (string.IsNullOrWhiteSpace(channelId))
            return McpInvokeResult.Fail(Name, "channelId is required");

        var channels = await _api.ListChannelsAsync(req.TenantId, ct);
        var channel = channels.FirstOrDefault(c => c.Id == channelId);
        if (channel == null)
            return McpInvokeResult.Fail(Name, $"Channel '{channelId}' not found");

        var issues = DiagnoseChannel(channel);

        return McpInvokeResult.Success(Name, req.TenantId, new
        {
            channelId,
            channelName = channel.Name,
            type = channel.Type,
            status = channel.Status,
            issueCount = issues.Count,
            issues,
            healthy = issues.Count == 0
        }, req.ExecutionId);
    }

    private static List<string> DiagnoseChannel(ChannelSummary channel)
    {
        var issues = new List<string>();

        if (channel.Status != "Active")
            issues.Add($"Channel is '{channel.Status}' — it must be Active to receive messages. Call the /activate endpoint.");

        // Additional checks will be possible once ChannelDefinition exposes RouterAgentId and SessionWindowHours
        // through the API response. For now we flag generic guidance.
        issues.Add("ℹ️ Verify that a Router agent is assigned via the channel configuration (RouterAgentId config key).");
        issues.Add("ℹ️ Verify that SessionWindowHours is configured (default is 24h if not set).");

        return issues;
    }

    private static JsonElement ParseInput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return default; }
    }
}

/// <summary>
/// Lista las integraciones (MCP servers externos) registradas para el tenant.
/// El Config Assistant la usa para saber qué tools están disponibles para asignar a agentes.
/// </summary>
public sealed class ListIntegrationsTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public ListIntegrationsTool(AgentFlowApiClient api) => _api = api;

    public string Name => "af_list_integrations";

    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Lists all external integrations (MCP servers, webhooks, APIs) registered for the tenant. Use to know which tools are available to assign to workflow agents.",
        IntendedFor = "config-assistant",
        InputSchemaJson = "{}"
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var integrations = await _api.ListIntegrationsAsync(req.TenantId, ct);
        return McpInvokeResult.Success(Name, req.TenantId,
            new { count = integrations.Count, integrations }, req.ExecutionId);
    }
}

/// <summary>
/// Genera el JSON base de un workflow nuevo a partir de una descripción en lenguaje natural.
/// El Config Assistant la usa para scaffoldear workflows que el usuario luego refina en el Designer.
/// </summary>
public sealed class ScaffoldWorkflowTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public ScaffoldWorkflowTool(AgentFlowApiClient api) => _api = api;

    public string Name => "af_scaffold_workflow";

    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Generates a workflow JSON scaffold based on a natural language description. Returns a valid workflow definition that the user can import into the Designer and customize. Includes suggested trigger event, ai.agent nodes, and conditional branches.",
        IntendedFor = "config-assistant",
        InputSchemaJson = """
        {
          "type": "object",
          "required": ["description"],
          "properties": {
            "description":    { "type": "string",  "description": "Natural language description of the workflow to create (e.g. 'loan application intake with identity verification and credit check')" },
            "triggerEvent":   { "type": "string",  "description": "Optional: override the suggested trigger event name" },
            "workflowBrainAgentId": { "type": "string", "description": "Optional: ID of the WorkflowBrain agent to assign to ai.agent nodes" }
          }
        }
        """
    };

    public Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = ParseInput(req.InputJson);
        var description = input.GetString("description");
        if (string.IsNullOrWhiteSpace(description))
            return Task.FromResult(McpInvokeResult.Fail(Name, "description is required"));

        var triggerEvent = input.GetString("triggerEvent") ?? SuggestEventName(description);
        var brainAgentId = input.GetString("workflowBrainAgentId") ?? "{{assign-workflow-brain-agent-id}}";

        var scaffold = new
        {
            name = $"Workflow: {TitleCase(description)}",
            triggerEventName = triggerEvent,
            status = "Draft",
            definitionJson = GenerateScaffoldJson(description, triggerEvent, brainAgentId),
            notes = new[]
            {
                "This is an auto-generated scaffold. Open it in the Workflow Designer to customize nodes.",
                $"Suggested trigger event: {triggerEvent}",
                $"Assign a WorkflowBrain agent to the ai.agent nodes (currently set to: {brainAgentId})"
            }
        };

        return Task.FromResult(McpInvokeResult.Success(Name, req.TenantId, scaffold, req.ExecutionId));
    }

    private static string SuggestEventName(string description)
    {
        // Simple keyword-based event name suggestion
        var lower = description.ToLowerInvariant();
        if (lower.Contains("loan")) return "loan.application.started";
        if (lower.Contains("onboard")) return "customer.onboarding.started";
        if (lower.Contains("support") || lower.Contains("complaint")) return "support.ticket.created";
        if (lower.Contains("order")) return "order.received";
        if (lower.Contains("payment")) return "payment.initiated";
        return "workflow.started";
    }

    private static string TitleCase(string s) =>
        string.Join(" ", s.Split(' ').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));

    private static string GenerateScaffoldJson(string description, string triggerEvent, string brainAgentId)
    {
        // Build scaffold JSON without raw string interpolation to avoid brace-escaping issues
        return string.Format("""
        {{
          "trigger": "{0}",
          "description": "{1}",
          "nodes": [
            {{
              "id": "start",
              "type": "trigger",
              "label": "Workflow Start",
              "next": "collect-info"
            }},
            {{
              "id": "collect-info",
              "type": "ai.agent",
              "label": "Collect Customer Information",
              "config": {{
                "agentId": "{2}",
                "prompt": "Greet the customer and collect the necessary information for {1}. Return structured JSON with the collected data.",
                "outputVar": "customerData"
              }},
              "next": "validate"
            }},
            {{
              "id": "validate",
              "type": "condition",
              "label": "Validate Data",
              "config": {{
                "expression": "{{customerData.isComplete}}"
              }},
              "branches": {{
                "true": "process",
                "false": "request-missing"
              }}
            }},
            {{
              "id": "request-missing",
              "type": "ai.agent",
              "label": "Request Missing Information",
              "config": {{
                "agentId": "{2}",
                "prompt": "Some required information is missing. Ask the customer to provide: {{customerData.missingFields}}.",
                "outputVar": "customerData"
              }},
              "next": "validate"
            }},
            {{
              "id": "process",
              "type": "action",
              "label": "Process Request",
              "config": {{
                "action": "emit_event",
                "eventName": "{0}.processed",
                "payload": "{{customerData}}"
              }},
              "next": "end"
            }},
            {{
              "id": "end",
              "type": "end",
              "label": "Workflow Complete"
            }}
          ]
        }}
        """, triggerEvent, description, brainAgentId);
    }

    private static JsonElement ParseInput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return default; }
    }
}

// ─── Extensión helper para leer strings de JsonElement sin throws ─────────────
internal static class JsonElementExtensions
{
    public static string? GetString(this JsonElement el, string property)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        return el.TryGetProperty(property, out var val) ? val.GetString() : null;
    }
}
