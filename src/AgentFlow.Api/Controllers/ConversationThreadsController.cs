using AgentFlow.Abstractions;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Api.Controllers;

/// <summary>
/// Conversation Threads API - Multi-turn conversation management.
/// Similar to OpenAI Assistants API: Threads + Messages.
/// </summary>
[ApiController]
[Route("api/v1/tenants/{tenantId}/threads")]
public sealed class ConversationThreadsController : ControllerBase
{
    private readonly IConversationThreadRepository _threadRepo;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentExecutor _executor;
    private readonly ILogger<ConversationThreadsController> _logger;
    
    public ConversationThreadsController(
        IConversationThreadRepository threadRepo,
        IAgentDefinitionRepository agentRepo,
        IAgentExecutor executor,
        ILogger<ConversationThreadsController> logger)
    {
        _threadRepo = threadRepo;
        _agentRepo = agentRepo;
        _executor = executor;
        _logger = logger;
    }
    
    /// <summary>
    /// Create a new conversation thread.
    /// Similar to OpenAI Assistants API: POST /threads
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateThread(
        [FromRoute] string tenantId,
        [FromBody] CreateThreadRequest request,
        CancellationToken ct)
    {
        // Validate agent exists
        var agent = await _agentRepo.GetByIdAsync(request.AgentId, tenantId, ct);
        if (agent is null)
            return NotFound($"Agent '{request.AgentId}' not found");
        
        // Generate thread key if not provided
        var threadKey = request.ThreadKey ?? GenerateThreadKey(agent, GetUserId());
        
        // Determine TTL from agent config or request
        var ttl = request.ExpiresIn ?? agent.Session.DefaultThreadTtl;
        var maxTurns = request.MaxTurns ?? agent.Session.MaxTurnsPerThread;
        
        var thread = ConversationThread.Create(
            tenantId: tenantId,
            threadKey: threadKey,
            agentDefinitionId: agent.Id,
            userId: GetUserId(),
            expiresIn: ttl,
            maxTurns: maxTurns,
            metadata: request.Metadata
        );
        
        var result = await _threadRepo.InsertAsync(thread, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        
        return Ok(new ThreadResponse
        {
            ThreadId = thread.Id,
            ThreadKey = thread.ThreadKey,
            AgentId = thread.AgentDefinitionId,
            Status = thread.Status.ToString(),
            ExpiresAt = thread.ExpiresAt,
            MaxTurns = thread.MaxTurns,
            TurnCount = thread.TurnCount,
            CreatedAt = thread.CreatedAt
        });
    }
    
    /// <summary>
    /// Send a message to a thread (continue conversation).
    /// Similar to OpenAI Assistants API: POST /threads/{threadId}/messages + Run
    /// </summary>
    [HttpPost("{threadId}/messages")]
    public async Task<IActionResult> SendMessage(
        [FromRoute] string tenantId,
        [FromRoute] string threadId,
        [FromBody] SendMessageRequest request,
        CancellationToken ct)
    {
        // Load thread
        var thread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return NotFound("Thread not found or expired");
        
        // Security: Verify ownership
        if (thread.UserId != GetUserId())
            return Forbid("You do not own this thread");
        
        // Execute agent with thread context
        var executionRequest = new AgentExecutionRequest
        {
            TenantId = tenantId,
            AgentKey = thread.AgentDefinitionId,
            UserId = GetUserId(),
            UserMessage = request.Message,
            ContextJson = request.Context,
            CorrelationId = thread.Id,
            ThreadId = thread.Id,
            Priority = ExecutionPriority.Normal
        };
        
        var executionResult = await _executor.ExecuteAsync(executionRequest, ct);
        
        if (executionResult.Status != ExecutionStatus.Completed)
        {
            return Ok(new MessageResponse
            {
                ExecutionId = executionResult.ExecutionId,
                AssistantResponse = executionResult.FinalResponse ?? "Execution failed",
                TokensUsed = executionResult.TotalTokensUsed,
                TotalTurns = thread.TurnCount,
                Status = executionResult.Status.ToString(),
                Error = executionResult.ErrorMessage
            });
        }
        
        // Thread turn is persisted by AgentExecutionEngine when ThreadId is provided.
        // Reload to return fresh counters.
        var updatedThread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);

        return Ok(new MessageResponse
        {
            ExecutionId = executionResult.ExecutionId,
            AssistantResponse = executionResult.FinalResponse ?? "",
            TokensUsed = executionResult.TotalTokensUsed,
            TotalTurns = updatedThread?.TurnCount ?? thread.TurnCount,
            Status = "Completed"
        });
    }
    
    /// <summary>
    /// Get thread history with chat turns.
    /// </summary>
    [HttpGet("{threadId}/history")]
    public async Task<IActionResult> GetHistory(
        [FromRoute] string tenantId,
        [FromRoute] string threadId,
        [FromQuery] int maxTurns = 50,
        CancellationToken ct = default)
    {
        var thread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return NotFound(new { message = "Thread not found or expired." });
        
        // Security: Verify ownership
        if (thread.UserId != GetUserId())
            return Forbid(new { message = "Thread access denied." });
        
        try
        {
            var history = thread.GetChatHistory(maxTurns);
            
            return Ok(new ThreadHistoryResponse
            {
                ThreadId = thread.Id,
                ThreadKey = thread.ThreadKey,
                Turns = history.RecentTurns.Select(t => new TurnDto
                {
                    UserMessage = t.UserMessage,
                    AssistantResponse = t.AssistantResponse,
                    Timestamp = t.Timestamp
                }).ToList(),
                TotalTurns = history.TotalTurns,
                OlderContextSummary = history.OlderContextSummary,
                TokenStats = new TokenStatsDto
                {
                    TotalTokens = thread.TokenStats?.TotalTokens ?? 0,
                    TotalTurns = thread.TokenStats?.TotalTurns ?? 0,
                    AverageTokensPerTurn = thread.TokenStats?.AverageTokensPerTurn ?? 0
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load thread history for {ThreadId}", threadId);
            return StatusCode(500, new { message = "Failed to load thread history.", error = ex.Message });
        }
    }
    
    /// <summary>
    /// Get thread details.
    /// </summary>
    [HttpGet("{threadId}")]
    public async Task<IActionResult> GetThread(
        [FromRoute] string tenantId,
        [FromRoute] string threadId,
        CancellationToken ct)
    {
        var thread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return NotFound();
        
        // Security: Verify ownership
        if (thread.UserId != GetUserId())
            return Forbid();
        
        return Ok(new ThreadResponse
        {
            ThreadId = thread.Id,
            ThreadKey = thread.ThreadKey,
            AgentId = thread.AgentDefinitionId,
            Status = thread.Status.ToString(),
            ExpiresAt = thread.ExpiresAt,
            MaxTurns = thread.MaxTurns,
            TurnCount = thread.TurnCount,
            CreatedAt = thread.CreatedAt,
            LastActivityAt = thread.LastActivityAt
        });
    }
    
    /// <summary>
    /// List threads for current user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListThreads(
        [FromRoute] string tenantId,
        [FromQuery] string? agentId = null,
        CancellationToken ct = default)
    {
        IReadOnlyList<ConversationThread> threads;
        
        if (!string.IsNullOrEmpty(agentId))
        {
            threads = await _threadRepo.GetByAgentAsync(agentId, tenantId, ct: ct);
        }
        else
        {
            threads = await _threadRepo.GetActiveByUserAsync(GetUserId(), tenantId, ct);
        }
        
        var response = threads.Select(t => new ThreadResponse
        {
            ThreadId = t.Id,
            ThreadKey = t.ThreadKey,
            AgentId = t.AgentDefinitionId,
            Status = t.Status.ToString(),
            ExpiresAt = t.ExpiresAt,
            MaxTurns = t.MaxTurns,
            TurnCount = t.TurnCount,
            CreatedAt = t.CreatedAt,
            LastActivityAt = t.LastActivityAt
        }).ToList();
        
        return Ok(response);
    }
    
    /// <summary>
    /// Archive a thread.
    /// </summary>
    [HttpPost("{threadId}/archive")]
    public async Task<IActionResult> ArchiveThread(
        [FromRoute] string tenantId,
        [FromRoute] string threadId,
        CancellationToken ct)
    {
        var thread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return NotFound();
        
        // Security: Verify ownership
        if (thread.UserId != GetUserId())
            return Forbid();
        
        var result = thread.Archive(GetUserId());
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        
        await _threadRepo.UpdateAsync(thread, ct);
        
        return Ok(new { message = "Thread archived successfully" });
    }
    
    /// <summary>
    /// Delete a thread (GDPR compliance).
    /// </summary>
    [HttpDelete("{threadId}")]
    public async Task<IActionResult> DeleteThread(
        [FromRoute] string tenantId,
        [FromRoute] string threadId,
        CancellationToken ct)
    {
        var thread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return NotFound();
        
        // Security: Verify ownership
        if (thread.UserId != GetUserId())
            return Forbid();
        
        var result = await _threadRepo.DeleteAsync(threadId, tenantId, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        
        return NoContent();
    }
    
    // --- Helpers ---
    
    private string GetUserId()
    {
        // TODO: Extract from JWT claims
        return HttpContext.Items["UserId"] as string ?? "demo-user";
    }
    
    private static string GenerateThreadKey(AgentDefinition agent, string userId)
    {
        var pattern = agent.Session.ThreadKeyPattern;
        var timestamp = DateTimeOffset.UtcNow;
        
        return pattern
            .Replace("{agentName}", agent.Name.ToLowerInvariant().Replace(" ", "-"))
            .Replace("{userId}", userId)
            .Replace("{date}", timestamp.ToString("yyyy-MM-dd"))
            .Replace("{time}", timestamp.ToString("HHmmss"))
            .Replace("{guid}", Guid.NewGuid().ToString("N")[..8]);
    }
}

// --- DTOs ---

public sealed record CreateThreadRequest
{
    public required string AgentId { get; init; }
    public string? ThreadKey { get; init; }
    public TimeSpan? ExpiresIn { get; init; }
    public int? MaxTurns { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record SendMessageRequest
{
    public required string Message { get; init; }
    public string? Context { get; init; }
}

public sealed record ThreadResponse
{
    public required string ThreadId { get; init; }
    public required string ThreadKey { get; init; }
    public required string AgentId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public int MaxTurns { get; init; }
    public int TurnCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }
}

public sealed record MessageResponse
{
    public required string ExecutionId { get; init; }
    public required string AssistantResponse { get; init; }
    public int TokensUsed { get; init; }
    public int TotalTurns { get; init; }
    public string Status { get; init; } = "Completed";
    public string? Error { get; init; }
}

public sealed record ThreadHistoryResponse
{
    public required string ThreadId { get; init; }
    public required string ThreadKey { get; init; }
    public required List<TurnDto> Turns { get; init; }
    public int TotalTurns { get; init; }
    public string? OlderContextSummary { get; init; }
    public required TokenStatsDto TokenStats { get; init; }
}

public sealed record TurnDto
{
    public required string UserMessage { get; init; }
    public string? AssistantResponse { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

public sealed record TokenStatsDto
{
    public int TotalTokens { get; init; }
    public int TotalTurns { get; init; }
    public int AverageTokensPerTurn { get; init; }
}
