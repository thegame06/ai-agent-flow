using AgentFlow.Api.Controllers.DTOs;
using AgentFlow.Api.AuthProfiles;
using AgentFlow.Extensions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Repositories;
using AgentFlow.Domain.ValueObjects;
using AgentFlow.Security;
using AgentFlow.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/agents")]
[AllowAnonymous] // 🔧 Development mode - remove in production
public sealed class AgentsController : ControllerBase
{
    private const string GlobalConversationGuardrails = @"

[GLOBAL_GUARDRAILS_V1]
- No repitas la misma pregunta de aclaracion en turnos consecutivos.
- Si el cliente envia mensajes cortos o fragmentados (ej: ""hola"", ""si"", ""ok"", ""mmm""), agrupa contexto y haz una sola pregunta util.
- Evita exponer mensajes tecnicos internos, errores de herramientas o configuracion.
- Si una herramienta no esta disponible, ofrece alternativa segura (continuar manualmente o escalar a humano) sin detalles tecnicos.
- Si el cliente expresa cierre (""ya me voy"", ""bye"", ""nada ya""), responde cierre breve y no insistas.
[/GLOBAL_GUARDRAILS_V1]";

    private readonly IAgentDefinitionRepository _agentRepository;
    private readonly IRuntimeModelProfileStore _runtimeProfiles;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IExtensionRegistry _extensionRegistry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(
        IAgentDefinitionRepository agentRepository,
        IRuntimeModelProfileStore runtimeProfiles,
        ITenantContextAccessor tenantContext,
        IExtensionRegistry extensionRegistry,
        IConfiguration configuration,
        ILogger<AgentsController> logger)
    {
        _agentRepository = agentRepository;
        _runtimeProfiles = runtimeProfiles;
        _tenantContext = tenantContext;
        _extensionRegistry = extensionRegistry;
        _configuration = configuration;
        _logger = logger;
    }

    // ─────────────────────────────────────────────
    // GET  /api/v1/tenants/{tenantId}/agents
    // List all agents (lightweight for DataGrid)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAgents(
        [FromRoute] string tenantId,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? runtimeKind = null,
        CancellationToken ct = default)
    {
        // 🔧 Development mode: Allow anonymous access
        var ctx = _tenantContext.Current;
        if (ctx != null && ctx.TenantId != tenantId && !ctx.IsPlatformAdmin) 
            return Forbid();

        var agents = await _agentRepository.GetAllAsync(tenantId, skip, limit, ct);
        if (!string.IsNullOrWhiteSpace(runtimeKind)
            && Enum.TryParse<AgentRuntimeKind>(runtimeKind, true, out var parsedRuntime))
        {
            agents = agents
                .Where(a => a.Session.RuntimeKind == parsedRuntime)
                .ToList();
        }

        var result = agents.Select(a => new AgentListItemDto
        {
            Id = a.Id,
            Name = a.Name,
            Description = a.Description,
            Status = a.Status.ToString(),
            Version = a.Version,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt,
            Tags = a.Tags,
            StepsCount = a.WorkflowSteps.Count,
            ToolsCount = a.AuthorizedTools.Count,
            PrimaryModel = a.Brain.ModelId,
            Provider = a.Brain.Provider,
            IsSystemAgent = a.IsSystemAgent,
            SystemRole = a.SystemRole == AgentSystemRole.Custom ? null : a.SystemRole.ToString(),
            RuntimeKind = a.Session.RuntimeKind.ToString(),
        });

        return Ok(result);
    }

    // ─────────────────────────────────────────────
    // GET  /api/v1/tenants/{tenantId}/agents/{id}
    // Full detail for the Designer
    // ─────────────────────────────────────────────
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAgent(
        [FromRoute] string tenantId,
        [FromRoute] string id,
        CancellationToken ct = default)
    {
        // 🔧 Development mode: Allow anonymous access
        var ctx = _tenantContext.Current;
        if (ctx != null && ctx.TenantId != tenantId && !ctx.IsPlatformAdmin)
            return Forbid();

        var agent = await _agentRepository.GetByIdAsync(id, tenantId, ct);
        if (agent is null) return NotFound();

        var dto = MapToDetailDto(agent);
        return Ok(dto);
    }

    [HttpGet("tool-catalog")]
    public async Task<IActionResult> GetToolCatalog(
        [FromRoute] string tenantId,
        CancellationToken ct = default)
    {
        var ctx = _tenantContext.Current;
        if (ctx != null && ctx.TenantId != tenantId && !ctx.IsPlatformAdmin)
            return Forbid();

        var catalog = new Dictionary<string, AgentToolCatalogItemDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in _extensionRegistry.GetTools())
        {
            var item = ToCatalogItem(tool);
            catalog[item.ToolId] = item;
        }

        foreach (var server in _configuration.GetSection("Mcp:Servers").Get<List<McpServerConfig>>() ?? [])
        {
            foreach (var item in await DiscoverMcpCatalogAsync(server, ct))
                catalog[item.ToolId] = item;
        }

        return Ok(catalog.Values.OrderBy(x => x.ToolName).ThenBy(x => x.ToolId).ToList());
    }

    // ─────────────────────────────────────────────
    // POST /api/v1/tenants/{tenantId}/agents
    // Create from Designer
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> CreateAgent(
        [FromRoute] string tenantId,
        [FromBody] AgentDesignerDto request,
        CancellationToken ct = default)
    {
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin) return Forbid();
        if (!TryValidateRuntimeProfile(tenantId, request.Session.RuntimeKind, request.Session.RuntimeModelProfileId, out var createValidationError))
            return BadRequest(new { error = createValidationError });

        var brain = new BrainConfiguration
        {
            ModelId = request.Brain.PrimaryModel,
            ReasoningModelCandidatesCsv = BuildReasoningCandidatesCsv(request.Brain.FallbackModel, request.Brain.ReasoningModelCandidatesCsv),
            Provider = request.Brain.Provider,
            SystemPromptTemplate = MergeGlobalGuardrails(request.Brain.SystemPrompt),
            Temperature = request.Brain.Temperature,
            MaxResponseTokens = request.Brain.MaxResponseTokens,
        };

        var loop = new AgentLoopConfig
        {
            MaxIterations = request.Loop.MaxSteps,
            ToolCallTimeout = TimeSpan.FromMilliseconds(request.Loop.TimeoutPerStepMs),
            MaxRetries = request.Loop.MaxRetries,
            AllowParallelToolCalls = request.Loop.AllowParallelToolCalls,
            PlannerType = ParsePlannerType(request.Loop.PlannerType),
            RuntimeMode = ParseRuntimeMode(request.Loop.RuntimeMode),
            HitlConfig = new HumanInTheLoopConfig { Enabled = request.Loop.RequireHumanApproval }
        };

        var memory = new MemoryConfig
        {
            EnableWorkingMemory = request.Memory.WorkingMemory,
            EnableLongTermMemory = request.Memory.LongTermMemory,
            EnableVectorMemory = request.Memory.VectorMemory,
        };

        var session = new SessionConfig
        {
            RuntimeKind = ParseAgentRuntimeKind(request.Session.RuntimeKind),
            RuntimeModelProfileId = NormalizeOptional(request.Session.RuntimeModelProfileId),
            EnableThreads = request.Session.EnableThreads,
            DefaultThreadTtl = TimeSpan.FromHours(request.Session.DefaultThreadTtlHours),
            MaxTurnsPerThread = request.Session.MaxTurnsPerThread,
            ContextWindowSize = request.Session.ContextWindowSize,
            AutoCreateThread = request.Session.AutoCreateThread,
            EnableSummarization = request.Session.EnableSummarization,
            ThreadKeyPattern = request.Session.ThreadKeyPattern,
            CustomerSafeFallbackMessage = request.Session.CustomerSafeFallbackMessage,
        };

        var agentResult = AgentDefinition.Create(
            tenantId,
            request.Name,
            request.Description,
            brain,
            loop,
            memory,
            session: session,
            workflowSteps: request.Steps.Select(MapWorkflowStep).ToList().AsReadOnly(),
            ownerUserId: ctx.UserId);

        if (!agentResult.IsSuccess)
            return BadRequest(agentResult.Error);

        var agent = agentResult.Value!;

        // Bind tools
        foreach (var toolDto in request.Tools)
        {
            var bindResult = agent.AddTool(new ToolBinding
            {
                ToolId = toolDto.ToolId,
                ToolName = toolDto.ToolName,
                ToolVersion = toolDto.Version,
                GrantedPermissions = toolDto.Permissions,
            });

            if (!bindResult.IsSuccess)
                return BadRequest(bindResult.Error);
        }

        var persistResult = await _agentRepository.InsertAsync(agent, ct);
        if (!persistResult.IsSuccess)
            return StatusCode(500, persistResult.Error);

        _logger.LogInformation(
            "Agent created: {AgentId} by {UserId} in tenant {TenantId}",
            agent.Id, ctx.UserId, tenantId);

        var dto = MapToDetailDto(agent);
        return CreatedAtAction(nameof(GetAgent), new { tenantId, id = agent.Id }, dto);
    }

    // ─────────────────────────────────────────────
    // PUT  /api/v1/tenants/{tenantId}/agents/{id}
    // Update from Designer — replaces full config
    // ─────────────────────────────────────────────
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAgent(
        [FromRoute] string tenantId,
        [FromRoute] string id,
        [FromBody] AgentDesignerDto request,
        CancellationToken ct = default)
    {
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin) return Forbid();
        if (!TryValidateRuntimeProfile(tenantId, request.Session.RuntimeKind, request.Session.RuntimeModelProfileId, out var updateValidationError))
            return BadRequest(new { error = updateValidationError });

        var existing = await _agentRepository.GetByIdAsync(id, tenantId, ct);
        if (existing is null) return NotFound();

        // Build value objects from DTO
        var brain = new BrainConfiguration
        {
            ModelId = request.Brain.PrimaryModel,
            ReasoningModelCandidatesCsv = BuildReasoningCandidatesCsv(request.Brain.FallbackModel, request.Brain.ReasoningModelCandidatesCsv),
            Provider = request.Brain.Provider,
            SystemPromptTemplate = MergeGlobalGuardrails(request.Brain.SystemPrompt),
            Temperature = request.Brain.Temperature,
            MaxResponseTokens = request.Brain.MaxResponseTokens,
        };

        var loop = new AgentLoopConfig
        {
            MaxIterations = request.Loop.MaxSteps,
            ToolCallTimeout = TimeSpan.FromMilliseconds(request.Loop.TimeoutPerStepMs),
            MaxRetries = request.Loop.MaxRetries,
            AllowParallelToolCalls = request.Loop.AllowParallelToolCalls,
            PlannerType = ParsePlannerType(request.Loop.PlannerType),
            RuntimeMode = ParseRuntimeMode(request.Loop.RuntimeMode),
            HitlConfig = new HumanInTheLoopConfig { Enabled = request.Loop.RequireHumanApproval }
        };

        var memory = new MemoryConfig
        {
            EnableWorkingMemory = request.Memory.WorkingMemory,
            EnableLongTermMemory = request.Memory.LongTermMemory,
            EnableVectorMemory = request.Memory.VectorMemory,
        };

        var tools = request.Tools
            .Select(t => new ToolBinding
            {
                ToolId = t.ToolId,
                ToolName = t.ToolName,
                ToolVersion = t.Version,
                GrantedPermissions = t.Permissions,
            })
            .ToList()
            .AsReadOnly();
        var session = new SessionConfig
        {
            RuntimeKind = ParseAgentRuntimeKind(request.Session.RuntimeKind),
            RuntimeModelProfileId = NormalizeOptional(request.Session.RuntimeModelProfileId),
            EnableThreads = request.Session.EnableThreads,
            DefaultThreadTtl = TimeSpan.FromHours(request.Session.DefaultThreadTtlHours),
            MaxTurnsPerThread = request.Session.MaxTurnsPerThread,
            ContextWindowSize = request.Session.ContextWindowSize,
            AutoCreateThread = request.Session.AutoCreateThread,
            EnableSummarization = request.Session.EnableSummarization,
            ThreadKeyPattern = request.Session.ThreadKeyPattern,
            CustomerSafeFallbackMessage = request.Session.CustomerSafeFallbackMessage,
        };

        // Use the domain's Update method (validates invariants)
        var updateResult = existing.Update(
            request.Name,
            request.Description,
            brain,
            loop,
            memory,
            session: session,
            workflowSteps: request.Steps.Select(MapWorkflowStep).ToList().AsReadOnly(),
            tools: tools,
            tags: request.Tags.ToList().AsReadOnly(),
            updatedBy: ctx.UserId);

        if (!updateResult.IsSuccess)
            return BadRequest(updateResult.Error);

        var persistResult = await _agentRepository.UpdateAsync(existing, ct);
        if (!persistResult.IsSuccess)
            return StatusCode(500, persistResult.Error);

        _logger.LogInformation(
            "Agent updated: {AgentId} by {UserId} in tenant {TenantId}",
            id, ctx.UserId, tenantId);

        var dto = MapToDetailDto(existing);
        return Ok(dto);
    }

    // ─────────────────────────────────────────────
    // POST /api/v1/tenants/{tenantId}/agents/{id}/publish
    // Publish agent (Draft → Published)
    // ─────────────────────────────────────────────
    [HttpPost("{id}/publish")]
    public async Task<IActionResult> PublishAgent(
        [FromRoute] string tenantId,
        [FromRoute] string id,
        CancellationToken ct = default)
    {
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin) return Forbid();

        var agent = await _agentRepository.GetByIdAsync(id, tenantId, ct);
        if (agent is null) return NotFound();

        var publishResult = agent.Publish(ctx.UserId);
        if (!publishResult.IsSuccess)
            return BadRequest(publishResult.Error);

        var persistResult = await _agentRepository.UpdateAsync(agent, ct);
        if (!persistResult.IsSuccess)
            return StatusCode(500, persistResult.Error);

        _logger.LogInformation(
            "Agent published: {AgentId} by {UserId} in tenant {TenantId}",
            id, ctx.UserId, tenantId);

        return Ok(new { id, status = "Published" });
    }

    // ─────────────────────────────────────────────
    // DELETE /api/v1/tenants/{tenantId}/agents/{id}
    // Soft delete
    // ─────────────────────────────────────────────
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAgent(
        [FromRoute] string tenantId,
        [FromRoute] string id,
        CancellationToken ct = default)
    {
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin) return Forbid();

        var result = await _agentRepository.DeleteAsync(id, tenantId, ct);
        if (!result.IsSuccess)
            return NotFound();

        _logger.LogInformation(
            "Agent deleted: {AgentId} by {UserId} in tenant {TenantId}",
            id, ctx.UserId, tenantId);

        return NoContent();
    }

    // ─────────────────────────────────────────────
    // POST /api/v1/tenants/{tenantId}/agents/{id}/clone
    // Clone an existing agent as new Draft
    // ─────────────────────────────────────────────
    [HttpPost("{id}/clone")]
    public async Task<IActionResult> CloneAgent(
        [FromRoute] string tenantId,
        [FromRoute] string id,
        [FromBody] CloneAgentRequest request,
        CancellationToken ct = default)
    {
        var ctx = _tenantContext.Current!;
        if (ctx.TenantId != tenantId && !ctx.IsPlatformAdmin) return Forbid();

        var source = await _agentRepository.GetByIdAsync(id, tenantId, ct);
        if (source is null) return NotFound();

        var cloneResult = AgentDefinition.Clone(
            source,
            request.NewName,
            request.NewDescription,
            ctx.UserId);

        if (!cloneResult.IsSuccess)
            return BadRequest(cloneResult.Error);

        var cloned = cloneResult.Value!;
        var persistResult = await _agentRepository.InsertAsync(cloned, ct);
        if (!persistResult.IsSuccess)
            return StatusCode(500, persistResult.Error);

        _logger.LogInformation(
            "Agent cloned: {SourceId} → {ClonedId} by {UserId} in tenant {TenantId}",
            id, cloned.Id, ctx.UserId, tenantId);

        var dto = MapToDetailDto(cloned);
        return CreatedAtAction(nameof(GetAgent), new { tenantId, id = cloned.Id }, dto);
    }

    // ─────────────────────────────────────────────
    // PRIVATE: Map aggregate → DTO
    // ─────────────────────────────────────────────

    private static AgentDetailDto MapToDetailDto(AgentDefinition agent) => new()
    {
        Id = agent.Id,
        Name = agent.Name,
        Description = agent.Description,
        Status = agent.Status.ToString(),
        Version = agent.Version,
        CreatedAt = agent.CreatedAt,
        UpdatedAt = agent.UpdatedAt,
        OwnerUserId = agent.OwnerUserId,
        Tags = agent.Tags,
        Brain = new BrainConfigDto
        {
            PrimaryModel = agent.Brain.ModelId,
            FallbackModel = (agent.Brain.ReasoningModelCandidatesCsv ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty,
            ReasoningModelCandidatesCsv = agent.Brain.ReasoningModelCandidatesCsv ?? string.Empty,
            Provider = agent.Brain.Provider,
            SystemPrompt = agent.Brain.SystemPromptTemplate,
            Temperature = agent.Brain.Temperature,
            MaxResponseTokens = agent.Brain.MaxResponseTokens,
        },
        Loop = new LoopConfigDto
        {
            MaxSteps = agent.LoopConfig.MaxIterations,
            TimeoutPerStepMs = (int)agent.LoopConfig.ToolCallTimeout.TotalMilliseconds,
            MaxTokensPerExecution = 100000,
            MaxRetries = agent.LoopConfig.MaxRetries,
            EnablePromptInjectionGuard = true,
            EnablePIIProtection = true,
            RequireHumanApproval = agent.LoopConfig.HitlConfig.Enabled,
            HumanApprovalThreshold = agent.LoopConfig.HitlConfig.ConfidenceThresholdToReview.ToString("F2"),
            AllowParallelToolCalls = agent.LoopConfig.AllowParallelToolCalls,
            PlannerType = agent.LoopConfig.PlannerType.ToString(),
            RuntimeMode = agent.LoopConfig.RuntimeMode.ToString(),
        },
        Memory = new MemoryConfigDto
        {
            WorkingMemory = agent.Memory.EnableWorkingMemory,
            LongTermMemory = agent.Memory.EnableLongTermMemory,
            VectorMemory = agent.Memory.EnableVectorMemory,
            AuditMemory = true, // Always true (invariant)
        },
        Session = new SessionConfigDto
        {
            RuntimeKind = agent.Session.RuntimeKind.ToString(),
            RuntimeModelProfileId = agent.Session.RuntimeModelProfileId,
            EnableThreads = agent.Session.EnableThreads,
            DefaultThreadTtlHours = (int)agent.Session.DefaultThreadTtl.TotalHours,
            MaxTurnsPerThread = agent.Session.MaxTurnsPerThread,
            ContextWindowSize = agent.Session.ContextWindowSize,
            AutoCreateThread = agent.Session.AutoCreateThread,
            EnableSummarization = agent.Session.EnableSummarization,
            ThreadKeyPattern = agent.Session.ThreadKeyPattern,
            CustomerSafeFallbackMessage = agent.Session.CustomerSafeFallbackMessage,
        },
        Steps = agent.WorkflowSteps.Select(s => new DesignerStepDto
        {
            Id = s.Id,
            Type = s.Type,
            Label = s.Label,
            Description = s.Description,
            Config = s.Config.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            Position = new PositionDto { X = s.Position.X, Y = s.Position.Y },
            Connections = s.Connections,
        }).ToList(),
        Tools = agent.AuthorizedTools
            .Select(t => new ToolBindingDto
            {
                ToolId = t.ToolId,
                ToolName = t.ToolName,
                Version = t.ToolVersion,
                Permissions = t.GrantedPermissions,
            }).ToList(),
    };

    private static WorkflowStep MapWorkflowStep(DesignerStepDto step) => new()
    {
        Id = step.Id,
        Type = step.Type,
        Label = step.Label,
        Description = step.Description,
        Config = NormalizeConfig(step.Config),
        Position = new WorkflowPosition { X = step.Position.X, Y = step.Position.Y },
        Connections = step.Connections,
    };

    private static IReadOnlyDictionary<string, object> NormalizeConfig(IReadOnlyDictionary<string, object>? config)
    {
        if (config is null) return new Dictionary<string, object>();

        return config.ToDictionary(
            kvp => kvp.Key,
            kvp => NormalizeConfigValue(kvp.Value));
    }

    private static object NormalizeConfigValue(object? value)
    {
        if (value is null) return string.Empty;

        return value switch
        {
            JsonElement element => NormalizeJsonElement(element),
            JsonDocument document => NormalizeJsonElement(document.RootElement),
            IReadOnlyDictionary<string, object> typedDictionary => NormalizeConfig(typedDictionary),
            IDictionary<string, object> dictionary => dictionary.ToDictionary(
                kvp => kvp.Key,
                kvp => NormalizeConfigValue(kvp.Value)),
            IEnumerable<object> values when value is not string => values.Select(NormalizeConfigValue).ToList(),
            _ => value
        };
    }

    private static object NormalizeJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(property => property.Name, property => NormalizeJsonElement(property.Value)),
        JsonValueKind.Array => element.EnumerateArray()
            .Select(NormalizeJsonElement)
            .ToList(),
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
        JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => element.GetRawText()
    };

    private static PlannerType ParsePlannerType(string? value)
        => Enum.TryParse<PlannerType>(value, true, out var parsed) ? parsed : PlannerType.ReAct;

    private static RuntimeMode ParseRuntimeMode(string? value)
        => Enum.TryParse<RuntimeMode>(value, true, out var parsed) ? parsed : RuntimeMode.Autonomous;

    private static AgentRuntimeKind ParseAgentRuntimeKind(string? value)
        => Enum.TryParse<AgentRuntimeKind>(value, true, out var parsed) ? parsed : AgentRuntimeKind.Text;

    private static string MergeGlobalGuardrails(string? prompt)
    {
        var basePrompt = string.IsNullOrWhiteSpace(prompt)
            ? "Eres un asistente empresarial."
            : prompt.Trim();

        if (basePrompt.Contains("[GLOBAL_GUARDRAILS_V1]", StringComparison.Ordinal))
            return basePrompt;

        return $"{basePrompt}\n{GlobalConversationGuardrails}";
    }

    private static string BuildReasoningCandidatesCsv(string? fallbackModel, string? explicitCandidatesCsv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        void add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            foreach (var item in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (set.Add(item))
                    ordered.Add(item);
            }
        }

        add(fallbackModel);
        add(explicitCandidatesCsv);
        return string.Join(",", ordered);
    }

    private static AgentToolCatalogItemDto ToCatalogItem(IToolPlugin tool)
    {
        if (TryParseMcpExtensionId(tool.ExtensionId, out var serverName, out var toolName))
        {
            return new AgentToolCatalogItemDto
            {
                ToolId = $"mcp:{serverName}:{toolName}",
                ToolName = toolName,
                Version = tool.Version,
                RiskLevel = tool.RiskLevel.ToString(),
                Description = tool.Description,
                Source = $"mcp:{serverName}"
            };
        }

        return new AgentToolCatalogItemDto
        {
            ToolId = string.IsNullOrWhiteSpace(tool.ExtensionId) ? tool.Name : tool.ExtensionId,
            ToolName = tool.Name,
            Version = tool.Version,
            RiskLevel = tool.RiskLevel.ToString(),
            Description = tool.Description,
            Source = "extension"
        };
    }

    private async Task<IReadOnlyList<AgentToolCatalogItemDto>> DiscoverMcpCatalogAsync(McpServerConfig server, CancellationToken ct)
    {
        if (!string.Equals(server.Transport, "Http", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(server.Url))
            return [];

        try
        {
            var toolsUrl = BuildMcpToolsUrl(server.Url);
            using var http = new HttpClient();
            using var response = await http.GetAsync(toolsUrl, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var riskLevel = server.Security?.DefaultRiskLevel ?? "Low";
            var items = new List<AgentToolCatalogItemDto>();

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                var toolName = entry.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(toolName))
                    continue;

                var description = entry.TryGetProperty("description", out var descriptionProp)
                    ? descriptionProp.GetString() ?? string.Empty
                    : string.Empty;

                items.Add(new AgentToolCatalogItemDto
                {
                    ToolId = $"mcp:{server.Name}:{toolName}",
                    ToolName = toolName,
                    Version = "1.0.0",
                    RiskLevel = riskLevel,
                    Description = description,
                    Source = $"mcp:{server.Name}"
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover MCP tools for server {ServerName}", server.Name);
            return [];
        }
    }

    private static bool TryParseMcpExtensionId(string extensionId, out string serverName, out string toolName)
    {
        serverName = string.Empty;
        toolName = string.Empty;

        if (!extensionId.StartsWith("mcp.", StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = extensionId["mcp.".Length..];
        var separatorIndex = suffix.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= suffix.Length - 1)
            return false;

        serverName = suffix[..separatorIndex];
        toolName = suffix[(separatorIndex + 1)..];
        return !string.IsNullOrWhiteSpace(serverName) && !string.IsNullOrWhiteSpace(toolName);
    }

    private static string BuildMcpToolsUrl(string invokeUrl)
    {
        var trimmed = invokeUrl.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash <= 0)
            return trimmed + "/tools";
        return trimmed[..lastSlash] + "/tools";
    }

    private sealed class McpServerConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Transport { get; set; } = "Http";
        public string? Url { get; set; }
        public McpServerSecurityConfig? Security { get; set; }
    }

    private sealed class McpServerSecurityConfig
    {
        public string DefaultRiskLevel { get; set; } = "Low";
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool TryValidateRuntimeProfile(string tenantId, string? runtimeKindRaw, string? runtimeModelProfileId, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(runtimeModelProfileId))
            return true;

        if (!Enum.TryParse<AgentRuntimeKind>(runtimeKindRaw, true, out var runtimeKind))
        {
            error = $"runtimeKind '{runtimeKindRaw}' no es valido.";
            return false;
        }

        var profile = _runtimeProfiles.Get(tenantId, runtimeModelProfileId.Trim());
        if (profile is null)
        {
            error = $"El perfil de runtime '{runtimeModelProfileId}' no existe para este tenant.";
            return false;
        }

        if (!string.Equals(profile.RuntimeKind, runtimeKind.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            error = $"El perfil '{runtimeModelProfileId}' es de runtime '{profile.RuntimeKind}' y no coincide con '{runtimeKind}'.";
            return false;
        }

        return true;
    }
}
