using AgentFlow.Abstractions.Workflow;
using AgentFlow.Abstractions;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Workflow;
using AgentFlow.Api.AuthProfiles;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Extensions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/studio/workflows")]
[Authorize]
public sealed class WorkflowStudioController : ControllerBase
{
    private readonly IWorkflowStudioStore _store;
    private readonly IWorkflowTriggerService _triggerService;
    private readonly IWorkflowAuditService _audit;
    private readonly IWorkflowSecurityPolicyService _policy;
    private readonly IChannelDefinitionRepository _channelRepo;
    private readonly ITenantConnectionStore _connectionStore;
    private readonly IExtensionRegistry _extensionRegistry;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IIntentRoutingStore _intentRoutingStore;
    private readonly IRuntimeModelProfileStore _runtimeProfiles;

    public WorkflowStudioController(
        IWorkflowStudioStore store,
        IWorkflowTriggerService triggerService,
        IWorkflowAuditService audit,
        IWorkflowSecurityPolicyService policy,
        IChannelDefinitionRepository channelRepo,
        ITenantConnectionStore connectionStore,
        IExtensionRegistry extensionRegistry,
        ITenantContextAccessor tenantContext,
        IAgentDefinitionRepository agentRepo,
        IIntentRoutingStore intentRoutingStore,
        IRuntimeModelProfileStore runtimeProfiles)
    {
        _store = store;
        _triggerService = triggerService;
        _audit = audit;
        _policy = policy;
        _channelRepo = channelRepo;
        _connectionStore = connectionStore;
        _extensionRegistry = extensionRegistry;
        _tenantContext = tenantContext;
        _agentRepo = agentRepo;
        _intentRoutingStore = intentRoutingStore;
        _runtimeProfiles = runtimeProfiles;
    }

    [HttpGet("catalog/activities")]
    public async Task<IActionResult> GetActivities(CancellationToken ct)
    {
        if (!CanAccessCatalog()) return Forbid();
        return Ok(await _store.GetActivitiesAsync(ct));
    }

    [HttpGet("integrations/status")]
    public async Task<IActionResult> GetIntegrationStatus([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();

        var channels = await _channelRepo.GetAllAsync(tenantId, ct);
        var connections = await _connectionStore.GetConnectionsAsync(tenantId, ct);
        var extensionStates = await _extensionRegistry.GetTenantExtensionStatesAsync(tenantId, ct);

        var items = new List<object>();

        foreach (var channel in channels)
        {
            var authMode = channel.Config.GetValueOrDefault("AuthMode");
            var hasSecret = !string.IsNullOrWhiteSpace(channel.Config.GetValueOrDefault("ApiToken")) ||
                            !string.IsNullOrWhiteSpace(channel.Config.GetValueOrDefault("PhoneNumberId")) ||
                            !string.IsNullOrWhiteSpace(authMode);

            items.Add(new
            {
                key = $"channel:{channel.Type.ToString().ToLowerInvariant()}",
                displayName = channel.Name,
                category = "channel",
                enabled = channel.Status == AgentFlow.Domain.Aggregates.ChannelStatus.Active,
                connected = channel.Status == AgentFlow.Domain.Aggregates.ChannelStatus.Active,
                secretsConfigured = hasSecret,
                capabilities = new[] { "send", "status" },
                detail = channel.Type.ToString()
            });
        }

        foreach (var state in extensionStates)
        {
            items.Add(new
            {
                key = $"extension:{state.Key}",
                displayName = state.Key,
                category = "extension",
                enabled = state.Value,
                connected = true,
                secretsConfigured = true,
                capabilities = new[] { "tool-call" },
                detail = state.Value ? "Enabled" : "Disabled"
            });
        }

        foreach (var connection in connections)
        {
            var secret = await _connectionStore.GetSecretAsync(tenantId, connection.Id, ct);
            var capabilities = connection.Type switch
            {
                TenantConnectionType.Messaging => new[] { "send", "voice", "whatsapp", "sms" },
                TenantConnectionType.Storage => new[] { "read", "write", "lookup" },
                TenantConnectionType.Mcp => new[] { "tool-call", "discovery" },
                TenantConnectionType.Rest => new[] { "http", "webhook" },
                TenantConnectionType.Sheets => new[] { "read", "write", "lookup" },
                _ => new[] { "connect" }
            };

            items.Add(new
            {
                key = $"connection:{connection.Id}",
                displayName = connection.Name,
                category = "connection",
                enabled = true,
                connected = secret is not null || connection.Type == TenantConnectionType.Storage,
                secretsConfigured = secret is not null || connection.Type == TenantConnectionType.Storage,
                capabilities,
                detail = $"{connection.Type}:{connection.ConnectorId}"
            });
        }

        return Ok(items);
    }

    [HttpPut("catalog/activities/{typeName}")]
    public async Task<IActionResult> UpsertActivity([FromRoute] string typeName, [FromBody] UpsertWorkflowActivityRequest request, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var actor = _tenantContext.Current!.UserId;

        var saved = await _store.UpsertActivityAsync(new WorkflowActivityCatalogContract
        {
            TypeName = typeName,
            DisplayName = request.DisplayName,
            Category = request.Category,
            Description = request.Description,
            InputSchema = request.InputSchema,
            OutputSchema = request.OutputSchema,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = actor
        }, ct);
        await _audit.RecordStudioActionAsync("platform", actor, "workflow.catalog.activity.upsert", typeName, new
        {
            request.DisplayName,
            request.Category
        }, HttpContext.TraceIdentifier, ct);

        return Ok(saved);
    }

    [HttpGet("catalog/events")]
    public async Task<IActionResult> GetEvents(CancellationToken ct)
    {
        if (!CanAccessCatalog()) return Forbid();
        return Ok(await _store.GetEventsAsync(ct));
    }

    [HttpPut("catalog/events/{eventName}")]
    public async Task<IActionResult> UpsertEvent([FromRoute] string eventName, [FromBody] UpsertWorkflowEventRequest request, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var actor = _tenantContext.Current!.UserId;

        var saved = await _store.UpsertEventAsync(new WorkflowEventCatalogContract
        {
            EventName = eventName,
            DisplayName = request.DisplayName,
            Entity = request.Entity,
            Description = request.Description,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = actor
        }, ct);
        await _audit.RecordStudioActionAsync("platform", actor, "workflow.catalog.event.upsert", eventName, new
        {
            request.DisplayName,
            request.Entity
        }, HttpContext.TraceIdentifier, ct);

        return Ok(saved);
    }

    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();
        return Ok(await _store.GetTemplatesAsync(tenantId, ct));
    }

    [HttpPut("templates/{templateId}")]
    public async Task<IActionResult> UpsertTemplate([FromRoute] string tenantId, [FromRoute] string templateId, [FromBody] UpsertWorkflowTemplateRequest request, CancellationToken ct)
    {
        if (!CanManageTenant(tenantId)) return Forbid();

        var now = DateTimeOffset.UtcNow;
        var actor = _tenantContext.Current!.UserId;

        var saved = await _store.UpsertTemplateAsync(new WorkflowTemplateContract
        {
            Id = templateId,
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description,
            TriggerEventName = request.TriggerEventName,
            DefinitionJson = request.DefinitionJson,
            CreatedAt = request.CreatedAt ?? now,
            UpdatedAt = now,
            UpdatedBy = actor
        }, ct);
        await _audit.RecordStudioActionAsync(tenantId, actor, "workflow.template.upsert", templateId, new
        {
            request.Name,
            request.TriggerEventName
        }, HttpContext.TraceIdentifier, ct);

        return Ok(saved);
    }

    [HttpGet]
    public async Task<IActionResult> GetDefinitions([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();
        return Ok(await _store.GetDefinitionsAsync(tenantId, ct));
    }

    [HttpGet("{workflowId}")]
    public async Task<IActionResult> GetDefinition([FromRoute] string tenantId, [FromRoute] string workflowId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();

        var definition = await _store.GetDefinitionAsync(tenantId, workflowId, ct);
        return definition is null ? NotFound() : Ok(definition);
    }

    [HttpPut("{workflowId}")]
    public async Task<IActionResult> UpsertDefinition([FromRoute] string tenantId, [FromRoute] string workflowId, [FromBody] UpsertWorkflowDefinitionRequest request, CancellationToken ct)
    {
        if (!CanManageTenant(tenantId)) return Forbid();

        var now = DateTimeOffset.UtcNow;
        var actor = _tenantContext.Current!.UserId;

        var existing = await _store.GetDefinitionAsync(tenantId, workflowId, ct);
        var version = existing is null ? 1 : Math.Max(existing.Version, request.Version ?? existing.Version);
        var requestedRuntime = string.IsNullOrWhiteSpace(request.RuntimeKind)
            ? existing?.RuntimeKind ?? "Text"
            : request.RuntimeKind;
        if (!RuntimeCompatibilityPolicy.TryParseRuntimeKind(requestedRuntime, out var runtimeKind, out var normalizedRuntime))
            return BadRequest(new { message = $"RuntimeKind inválido: '{requestedRuntime}'. Valores permitidos: Text, Voice, MultimodalRealtime." });

        if (!RuntimeCompatibilityPolicy.IsTriggerEventCompatible(runtimeKind, request.TriggerEventName))
            return BadRequest(new { message = RuntimeCompatibilityPolicy.BuildTriggerError(runtimeKind, request.TriggerEventName) });

        var agentValidationError = await ValidateAiAgentNodesAsync(tenantId, request.DefinitionJson, runtimeKind, ct);
        if (agentValidationError is not null)
            return BadRequest(new { message = agentValidationError });

        var profileValidationError = ValidateRuntimeProfileMetadata(tenantId, normalizedRuntime, request.Metadata);
        if (profileValidationError is not null)
            return BadRequest(new { message = profileValidationError });

        var saved = await _store.UpsertDefinitionAsync(new WorkflowDefinitionContract
        {
            Id = workflowId,
            TenantId = tenantId,
            Name = request.Name,
            TriggerEventName = request.TriggerEventName,
            RuntimeKind = normalizedRuntime,
            Version = version,
            Status = request.Status ?? existing?.Status ?? WorkflowDefinitionStatus.Draft,
            DefinitionJson = request.DefinitionJson,
            Metadata = request.Metadata,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            UpdatedBy = actor
        }, ct);
        await _audit.RecordStudioActionAsync(tenantId, actor, "workflow.definition.upsert", workflowId, new
        {
            request.Name,
            request.TriggerEventName,
            saved.Version,
            saved.Status
        }, HttpContext.TraceIdentifier, ct);

        return Ok(saved);
    }

    [HttpPost("{workflowId}/publish")]
    public async Task<IActionResult> PublishDefinition([FromRoute] string tenantId, [FromRoute] string workflowId, CancellationToken ct)
    {
        if (!CanManageTenant(tenantId)) return Forbid();

        var existing = await _store.GetDefinitionAsync(tenantId, workflowId, ct);
        if (existing is null) return NotFound(new { message = "Workflow definition not found." });
        try
        {
            _policy.ValidateDefinitionOrThrow(existing.DefinitionJson);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        if (!RuntimeCompatibilityPolicy.TryParseRuntimeKind(existing.RuntimeKind, out var runtimeKind, out _))
            return BadRequest(new { message = $"RuntimeKind inválido en workflow: '{existing.RuntimeKind}'." });

        if (!RuntimeCompatibilityPolicy.IsTriggerEventCompatible(runtimeKind, existing.TriggerEventName))
            return BadRequest(new { message = RuntimeCompatibilityPolicy.BuildTriggerError(runtimeKind, existing.TriggerEventName) });

        // Validate that all ai.agent nodes reference custom (non-system) agents and runtime-compatible agents
        var agentValidationError = await ValidateAiAgentNodesAsync(tenantId, existing.DefinitionJson, runtimeKind, ct);
        if (agentValidationError is not null)
            return BadRequest(new { message = agentValidationError });

        var saved = await _store.UpsertDefinitionAsync(existing with
        {
            Status = WorkflowDefinitionStatus.Published,
            Version = existing.Version + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = _tenantContext.Current!.UserId
        }, ct);
        await SyncWorkflowIntentsToRoutingAsync(saved, ct);
        await _audit.RecordStudioActionAsync(tenantId, _tenantContext.Current!.UserId, "workflow.definition.publish", workflowId, new
        {
            saved.Version
        }, HttpContext.TraceIdentifier, ct);

        return Ok(saved);
    }

    [HttpPost("run-event")]
    public async Task<IActionResult> RunEvent([FromRoute] string tenantId, [FromBody] RunWorkflowEventRequest request, CancellationToken ct)
    {
        if (!CanManageTenant(tenantId)) return Forbid();

        try
        {
            _policy.ValidatePayloadOrThrow(request.Payload);
            var execution = await _triggerService.TriggerEventAsync(
                tenantId,
                request.EventName,
                _tenantContext.Current!.UserId,
                request.CorrelationId,
                request.Payload,
                ct);
            await _audit.RecordExecutionActionAsync(
                tenantId,
                _tenantContext.Current!.UserId,
                "workflow.execution.trigger",
                execution.Id,
                execution.WorkflowDefinitionId,
                new { request.EventName, request.CorrelationId },
                HttpContext.TraceIdentifier,
                ct);
            return Ok(execution);
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("No published workflow matches", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { message = ex.Message });
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("executions")]
    public async Task<IActionResult> GetExecutions([FromRoute] string tenantId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();
        return Ok(await _store.GetExecutionsAsync(tenantId, limit, ct));
    }

    [HttpGet("executions/{executionId}/steps")]
    public async Task<IActionResult> GetExecutionSteps([FromRoute] string tenantId, [FromRoute] string executionId, CancellationToken ct = default)
    {
        if (!CanAccessTenant(tenantId)) return Forbid();
        return Ok(await _store.GetStepLogsAsync(tenantId, executionId, ct));
    }

    [HttpPost("executions/{executionId}/retry")]
    public async Task<IActionResult> RetryExecution([FromRoute] string tenantId, [FromRoute] string executionId, CancellationToken ct = default)
    {
        if (!CanManageTenant(tenantId)) return Forbid();

        try
        {
            var retried = await _triggerService.RetryExecutionAsync(
                tenantId,
                executionId,
                _tenantContext.Current!.UserId,
                ct);
            await _audit.RecordExecutionActionAsync(
                tenantId,
                _tenantContext.Current!.UserId,
                "workflow.execution.retry",
                retried.Id,
                retried.WorkflowDefinitionId,
                new { sourceExecutionId = executionId },
                HttpContext.TraceIdentifier,
                ct);
            return Ok(retried);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private bool CanAccessCatalog()
    {
        var context = _tenantContext.Current!;
        return context.IsPlatformAdmin || context.HasPermission(AgentFlowPermissions.AgentRead);
    }

    private bool CanManage()
    {
        var context = _tenantContext.Current!;
        return context.IsPlatformAdmin || context.HasPermission(AgentFlowPermissions.AgentUpdate);
    }

    private bool CanAccessTenant(string tenantId)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(AgentFlowPermissions.AgentRead) || context.IsPlatformAdmin);
    }

    private bool CanManageTenant(string tenantId)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(AgentFlowPermissions.AgentUpdate) || context.IsPlatformAdmin);
    }

    /// <summary>
    /// Parses the workflow definition JSON and validates that all ai.agent nodes
    /// reference agents that exist, are published, and are NOT system agents.
    /// Returns an error message string if invalid, null if OK.
    /// </summary>
    private async Task<string?> ValidateAiAgentNodesAsync(string tenantId, string definitionJson, AgentRuntimeKind workflowRuntimeKind, CancellationToken ct)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(definitionJson); }
        catch { return null; } // malformed JSON is caught by ValidateDefinitionOrThrow

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("activities", out var activities) &&
                !doc.RootElement.TryGetProperty("nodes", out activities) &&
                !doc.RootElement.TryGetProperty("steps", out activities))
                return null;

            if (activities.ValueKind != JsonValueKind.Array) return null;

            // Collect all agentId values from ai.agent nodes
            var agentIds = new List<string>();
            foreach (var node in activities.EnumerateArray())
            {
                string? type = null;
                if (node.TryGetProperty("type", out var typeEl)) type = typeEl.GetString();
                if (!string.Equals(type, "ai.agent", StringComparison.OrdinalIgnoreCase)) continue;

                string? agentId = null;
                if (node.TryGetProperty("config", out var config))
                {
                    if (config.TryGetProperty("agentId", out var agentIdEl))
                        agentId = agentIdEl.GetString();
                }
                if (!string.IsNullOrWhiteSpace(agentId))
                    agentIds.Add(agentId!);
            }

            if (agentIds.Count == 0) return null;

            // Load all published agents for the tenant once
            var published = await _agentRepo.GetPublishedAsync(tenantId, ct);
            var publishedById = published.ToDictionary(a => a.Id.ToString());

            foreach (var agentId in agentIds)
            {
                if (!publishedById.TryGetValue(agentId, out var agent))
                    return $"El agente '{agentId}' referenciado en un nodo ai.agent no existe o no está publicado en este tenant.";

                if (agent.IsSystemAgent)
                    return $"El agente '{agent.Name}' (id: {agentId}) es administrado por el sistema y no puede asignarse a un nodo de workflow. " +
                           "Solo agentes personalizados de tu equipo pueden usarse como WorkflowBrain. " +
                           "Crea un agente nuevo o clona la plantilla Workflow Brain Default.";

                if (!RuntimeCompatibilityPolicy.IsAgentCompatible(workflowRuntimeKind, agent.Session.RuntimeKind))
                    return RuntimeCompatibilityPolicy.BuildAgentRuntimeError(
                        agent.Name,
                        agentId,
                        workflowRuntimeKind,
                        agent.Session.RuntimeKind);
            }
        }

        return null;
    }

    private async Task SyncWorkflowIntentsToRoutingAsync(WorkflowDefinitionContract definition, CancellationToken ct)
    {
        var startIntents = ReadStartIntents(definition.DefinitionJson, definition.TriggerEventName);
        if (startIntents.Count == 0)
            return;

        var targetAgentId = ReadFirstWorkflowAgentId(definition.DefinitionJson);
        if (string.IsNullOrWhiteSpace(targetAgentId))
            return;

        var channels = await _channelRepo.GetAllAsync(definition.TenantId, ct);
        var matchingChannels = channels
            .Where(channel => string.Equals(EventForChannel(channel.Type), definition.TriggerEventName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var channel in matchingChannels)
        {
            var sourceAgentId = channel.RouterAgentId;
            if (string.IsNullOrWhiteSpace(sourceAgentId))
            {
                sourceAgentId = (channel.Config.GetValueOrDefault("IntentAgents")
                    ?? channel.Config.GetValueOrDefault("RoutingAgents")
                    ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
            }
            if (string.IsNullOrWhiteSpace(sourceAgentId))
                sourceAgentId = channel.Config.GetValueOrDefault("DefaultAgentId");

            if (string.IsNullOrWhiteSpace(sourceAgentId))
                continue;

            for (var index = 0; index < startIntents.Count; index++)
            {
                var intent = startIntents[index];
                var rawIntentKey = !string.IsNullOrWhiteSpace(intent.Label) ? intent.Label! : intent.Id;
                var intentKey = NormalizeIntentKey(rawIntentKey);
                if (string.IsNullOrWhiteSpace(intentKey))
                    continue;

                await _intentRoutingStore.UpsertRuleAsync(new IntentRoutingRule
                {
                    Id = $"brain-{definition.Id}-{channel.Type.ToString().ToLowerInvariant()}-{intent.Id}",
                    TenantId = definition.TenantId,
                    IntentKey = intentKey,
                    IntentDescription = intent.Description ?? string.Empty,
                    Category = "Workflow",
                    ExamplePhrases = intent.Examples,
                    SourceAgentId = sourceAgentId,
                    TargetAgentId = targetAgentId,
                    WorkflowDefinitionId = definition.Id,
                    WorkflowName = definition.Name,
                    Priority = 100 + index,
                    Enabled = true,
                    Channel = channel.Type.ToString().ToLowerInvariant(),
                    ConditionsJson = JsonSerializer.Serialize(new
                    {
                        workflowId = definition.Id,
                        workflowName = definition.Name,
                        eventName = definition.TriggerEventName,
                        examples = intent.Examples,
                        description = intent.Description ?? string.Empty,
                        triggerSource = intent.TriggerSource ?? "message",
                        confidenceThreshold = intent.ConfidenceThreshold ?? 0.7
                    }),
                    HandoffPolicyJson = JsonSerializer.Serialize(new { source = "workflow-publish-auto-sync" }),
                    Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, ct);
            }
        }
    }

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

    private static string EventForChannel(ChannelType type) => type switch
    {
        ChannelType.Voice or ChannelType.CallCenter => "connect.call.received",
        ChannelType.Email => "connect.message.received",
        ChannelType.WhatsApp or ChannelType.WebChat or ChannelType.Telegram or ChannelType.Slack => "connect.message.received",
        _ => "connect.message.received"
    };

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

    private string? ValidateRuntimeProfileMetadata(string tenantId, string runtimeKind, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
            return null;
        if (!metadata.TryGetValue("runtimeModelProfileId", out var profileId) || string.IsNullOrWhiteSpace(profileId))
            return null;

        var profile = _runtimeProfiles.Get(tenantId, profileId.Trim());
        if (profile is null)
            return $"El perfil runtime '{profileId}' no existe en este tenant.";

        if (!string.Equals(profile.RuntimeKind, runtimeKind, StringComparison.OrdinalIgnoreCase))
            return $"El perfil runtime '{profileId}' usa runtime '{profile.RuntimeKind}' y no coincide con '{runtimeKind}'.";

        return null;
    }
}

internal sealed record WorkflowStartIntentSnapshot(
    string Id,
    string? Label,
    string? Description,
    IReadOnlyList<string> Examples,
    string? TriggerSource,
    double? ConfidenceThreshold);

public sealed record UpsertWorkflowActivityRequest
{
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = "General";
    public string Description { get; init; } = string.Empty;
    public Dictionary<string, string> InputSchema { get; init; } = new();
    public Dictionary<string, string> OutputSchema { get; init; } = new();
}

public sealed record UpsertWorkflowEventRequest
{
    public string DisplayName { get; init; } = string.Empty;
    public string Entity { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed record UpsertWorkflowTemplateRequest
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string TriggerEventName { get; init; } = string.Empty;
    public string DefinitionJson { get; init; } = "{}";
    public DateTimeOffset? CreatedAt { get; init; }
}

public sealed record UpsertWorkflowDefinitionRequest
{
    public string Name { get; init; } = string.Empty;
    public string TriggerEventName { get; init; } = string.Empty;
    public string RuntimeKind { get; init; } = "Text";
    public string DefinitionJson { get; init; } = "{}";
    public WorkflowDefinitionStatus? Status { get; init; }
    public int? Version { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public sealed record RunWorkflowEventRequest
{
    public string EventName { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public Dictionary<string, object?>? Payload { get; init; }
}
