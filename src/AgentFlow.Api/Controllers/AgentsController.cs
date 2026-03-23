using AgentFlow.Api.Controllers.DTOs;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Repositories;
using AgentFlow.Domain.ValueObjects;
using AgentFlow.Security;
using AgentFlow.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/agents")]
[AllowAnonymous] // 🔧 Development mode - remove in production
public sealed class AgentsController : ControllerBase
{
    private readonly IAgentDefinitionRepository _agentRepository;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(
        IAgentDefinitionRepository agentRepository,
        ITenantContextAccessor tenantContext,
        ILogger<AgentsController> logger)
    {
        _agentRepository = agentRepository;
        _tenantContext = tenantContext;
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
        CancellationToken ct = default)
    {
        // 🔧 Development mode: Allow anonymous access
        var ctx = _tenantContext.Current;
        if (ctx != null && ctx.TenantId != tenantId && !ctx.IsPlatformAdmin) 
            return Forbid();

        var agents = await _agentRepository.GetAllAsync(tenantId, skip, limit, ct);

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

        var brain = new BrainConfiguration
        {
            ModelId = request.Brain.PrimaryModel,
            Provider = request.Brain.Provider,
            SystemPromptTemplate = request.Brain.SystemPrompt,
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
            EnableThreads = request.Session.EnableThreads,
            DefaultThreadTtl = TimeSpan.FromHours(request.Session.DefaultThreadTtlHours),
            MaxTurnsPerThread = request.Session.MaxTurnsPerThread,
            ContextWindowSize = request.Session.ContextWindowSize,
            AutoCreateThread = request.Session.AutoCreateThread,
            EnableSummarization = request.Session.EnableSummarization,
            ThreadKeyPattern = request.Session.ThreadKeyPattern,
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

        var existing = await _agentRepository.GetByIdAsync(id, tenantId, ct);
        if (existing is null) return NotFound();

        // Build value objects from DTO
        var brain = new BrainConfiguration
        {
            ModelId = request.Brain.PrimaryModel,
            Provider = request.Brain.Provider,
            SystemPromptTemplate = request.Brain.SystemPrompt,
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

        // Use the domain's Update method (validates invariants)
        var updateResult = existing.Update(
            request.Name,
            request.Description,
            brain,
            loop,
            memory,
            session: null,
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
            EnableThreads = agent.Session.EnableThreads,
            DefaultThreadTtlHours = (int)agent.Session.DefaultThreadTtl.TotalHours,
            MaxTurnsPerThread = agent.Session.MaxTurnsPerThread,
            ContextWindowSize = agent.Session.ContextWindowSize,
            AutoCreateThread = agent.Session.AutoCreateThread,
            EnableSummarization = agent.Session.EnableSummarization,
            ThreadKeyPattern = agent.Session.ThreadKeyPattern,
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
        Config = step.Config,
        Position = new WorkflowPosition { X = step.Position.X, Y = step.Position.Y },
        Connections = step.Connections,
    };

    private static PlannerType ParsePlannerType(string? value)
        => Enum.TryParse<PlannerType>(value, true, out var parsed) ? parsed : PlannerType.ReAct;

    private static RuntimeMode ParseRuntimeMode(string? value)
        => Enum.TryParse<RuntimeMode>(value, true, out var parsed) ? parsed : RuntimeMode.Autonomous;
}
