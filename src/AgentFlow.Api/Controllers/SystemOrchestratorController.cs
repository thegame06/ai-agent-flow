using System.Text.Json;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Workflow;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/system-orchestrator")]
[Authorize]
public sealed class SystemOrchestratorController : ControllerBase
{
    private readonly IWorkflowStudioStore _workflowStore;
    private readonly IIntentRoutingStore _routingStore;
    private readonly IAgentDefinitionRepository _agentRepository;
    private readonly IChannelDefinitionRepository _channelRepository;
    private readonly ITenantConnectionStore _connectionStore;
    private readonly ITenantContextAccessor _tenantContext;

    public SystemOrchestratorController(
        IWorkflowStudioStore workflowStore,
        IIntentRoutingStore routingStore,
        IAgentDefinitionRepository agentRepository,
        IChannelDefinitionRepository channelRepository,
        ITenantConnectionStore connectionStore,
        ITenantContextAccessor tenantContext)
    {
        _workflowStore = workflowStore;
        _routingStore = routingStore;
        _agentRepository = agentRepository;
        _channelRepository = channelRepository;
        _connectionStore = connectionStore;
        _tenantContext = tenantContext;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var workflows = await _workflowStore.GetDefinitionsAsync(tenantId, ct);
        var events = await _workflowStore.GetEventsAsync(ct);
        var rules = await _routingStore.GetRulesAsync(tenantId, ct);
        var registryAgents = await _routingStore.GetAgentsAsync(tenantId, ct);
        var agents = await _agentRepository.GetAllAsync(tenantId, 0, 200, ct);
        var channels = await _channelRepository.GetAllAsync(tenantId, ct);
        var connections = await _connectionStore.GetConnectionsAsync(tenantId, ct);

        var workflowSummaries = workflows.Select(workflow =>
        {
            var intents = ReadIntentLabels(workflow.DefinitionJson);
            var syncedRules = rules
                .Where(rule => string.Equals(rule.ConditionsJson is null ? null : ReadJsonString(rule.ConditionsJson, "workflowId"), workflow.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return new WorkflowOrchestrationSummary(
                workflow.Id,
                workflow.Name,
                workflow.Status.ToString(),
                workflow.TriggerEventName,
                intents,
                syncedRules.Count,
                intents.Count == 0 ? "Sin intenciones" : syncedRules.Count >= intents.Count ? "Sincronizado" : "Pendiente de sincronizar");
        }).ToList();

        var channelSummaries = channels.Select(channel =>
        {
            channel.Config.TryGetValue("ConnectionId", out var connectionId);
            channel.Config.TryGetValue("DefaultAgentId", out var defaultAgentId);
            channel.Config.TryGetValue("AgentId", out var agentId);

            return new ChannelOrchestrationSummary(
                channel.Id,
                channel.Name,
                channel.Type.ToString(),
                channel.Status.ToString(),
                string.IsNullOrWhiteSpace(connectionId) ? null : connectionId,
                string.IsNullOrWhiteSpace(defaultAgentId) ? agentId : defaultAgentId,
                EventForChannel(channel.Type));
        }).ToList();

        var connectionSummaries = new List<ConnectionOrchestrationSummary>();
        foreach (var connection in connections)
        {
            var secret = await _connectionStore.GetSecretAsync(tenantId, connection.Id, ct);
            connectionSummaries.Add(new ConnectionOrchestrationSummary(
                connection.Id,
                connection.Name,
                connection.Type.ToString(),
                connection.ConnectorId,
                secret is not null || connection.Type == TenantConnectionType.Storage,
                CapabilitiesForConnection(connection.Type)));
        }

        var gaps = BuildGaps(workflowSummaries, channelSummaries, connectionSummaries, agents, rules);

        return Ok(new SystemOrchestratorStatusResponse(
            new SystemAgentDescriptor(
                "annonai-system-orchestrator",
                "Annonai System Orchestrator",
                "Agente de sistema intocable para diagnosticar eventos, intenciones, canales, integraciones y reglas de enrutamiento.",
                Locked: true,
                ConfigurableBy: "platform-admin",
                Capabilities: new[]
                {
                    "event.catalog",
                    "intent.routing.diagnostics",
                    "workflow.guidance",
                    "integration.readiness",
                    "channel.trigger.mapping"
                }),
            events.Select(e => new EventDescriptor(e.EventName, e.DisplayName, e.Entity, e.Description)).ToList(),
            workflowSummaries,
            channelSummaries,
            connectionSummaries,
            registryAgents.Select(a => new AgentRegistrySummary(a.AgentId, a.AgentType, a.Enabled, a.ExternalReplyAllowed, a.Capabilities)).ToList(),
            gaps));
    }

    private static IReadOnlyList<string> ReadIntentLabels(string definitionJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(definitionJson);
            if (!doc.RootElement.TryGetProperty("start", out var start)) return Array.Empty<string>();
            if (!start.TryGetProperty("intents", out var intents)) return Array.Empty<string>();
            if (intents.ValueKind != JsonValueKind.Array) return Array.Empty<string>();

            return intents
                .EnumerateArray()
                .Select(intent => ReadJsonString(intent, "label") ?? ReadJsonString(intent, "id"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? ReadJsonString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ReadJsonString(doc.RootElement, property);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadJsonString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string EventForChannel(ChannelType type) => type switch
    {
        ChannelType.Voice or ChannelType.CallCenter => "connect.call.received",
        ChannelType.Email => "connect.message.received",
        ChannelType.WhatsApp or ChannelType.WebChat or ChannelType.Telegram or ChannelType.Slack => "connect.message.received",
        _ => "connect.message.received"
    };

    private static IReadOnlyList<string> CapabilitiesForConnection(TenantConnectionType type) => type switch
    {
        TenantConnectionType.Messaging => new[] { "Mensajes", "Voz", "Call center", "Callbacks" },
        TenantConnectionType.Storage => new[] { "Archivos", "Drive", "Excel", "Evidencias" },
        TenantConnectionType.Mcp => new[] { "Tools MCP", "Discovery", "Tool calls" },
        TenantConnectionType.Rest => new[] { "API", "Webhook", "HTTP" },
        TenantConnectionType.Sheets => new[] { "Sheets", "Excel", "Lectura/escritura" },
        _ => new[] { "Conexion" }
    };

    private static IReadOnlyList<string> BuildGaps(
        IReadOnlyList<WorkflowOrchestrationSummary> workflows,
        IReadOnlyList<ChannelOrchestrationSummary> channels,
        IReadOnlyList<ConnectionOrchestrationSummary> connections,
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<IntentRoutingRule> rules)
    {
        var gaps = new List<string>();

        if (workflows.Count == 0) gaps.Add("Crea al menos un workflow para modelar la logica de negocio.");
        if (workflows.Any(w => w.IntentLabels.Count > w.SyncedRoutingRules)) gaps.Add("Sincroniza las intenciones pendientes con intent-routing.");
        if (channels.All(c => !string.Equals(c.Status, "Active", StringComparison.OrdinalIgnoreCase))) gaps.Add("Activa al menos un canal para disparar eventos del sistema.");
        if (channels.Any(c => string.IsNullOrWhiteSpace(c.DefaultAgentId))) gaps.Add("Asigna un agente por defecto a cada canal operativo.");
        if (connections.Count == 0) gaps.Add("Configura integraciones base como Twilio, Storage, REST o MCP desde Marketplace.");
        if (connections.Any(c => !c.Ready)) gaps.Add("Completa los secrets o credenciales de integraciones pendientes.");
        if (agents.All(a => a.Status != AgentStatus.Published)) gaps.Add("Publica al menos un agente para usarlo en canales y nodos de Workflow Studio.");
        if (rules.Count == 0) gaps.Add("No hay reglas de intencion activas; el orquestador no puede enrutar por intencion todavia.");

        return gaps.Count == 0 ? new[] { "Orquestacion lista para prueba end-to-end." } : gaps;
    }
}

public sealed record SystemOrchestratorStatusResponse(
    SystemAgentDescriptor SystemAgent,
    IReadOnlyList<EventDescriptor> Events,
    IReadOnlyList<WorkflowOrchestrationSummary> Workflows,
    IReadOnlyList<ChannelOrchestrationSummary> Channels,
    IReadOnlyList<ConnectionOrchestrationSummary> Connections,
    IReadOnlyList<AgentRegistrySummary> AgentRegistry,
    IReadOnlyList<string> Gaps);

public sealed record SystemAgentDescriptor(
    string Id,
    string Name,
    string Description,
    bool Locked,
    string ConfigurableBy,
    IReadOnlyList<string> Capabilities);

public sealed record EventDescriptor(string EventName, string DisplayName, string Entity, string Description);

public sealed record WorkflowOrchestrationSummary(
    string Id,
    string Name,
    string Status,
    string TriggerEventName,
    IReadOnlyList<string> IntentLabels,
    int SyncedRoutingRules,
    string Readiness);

public sealed record ChannelOrchestrationSummary(
    string Id,
    string Name,
    string Type,
    string Status,
    string? ConnectionId,
    string? DefaultAgentId,
    string SystemEvent);

public sealed record ConnectionOrchestrationSummary(
    string Id,
    string Name,
    string Type,
    string ConnectorId,
    bool Ready,
    IReadOnlyList<string> Capabilities);

public sealed record AgentRegistrySummary(
    string AgentId,
    string AgentType,
    bool Enabled,
    bool ExternalReplyAllowed,
    IReadOnlyList<string> Capabilities);
