using AgentFlow.Abstractions;
using AgentFlow.Api.AuthProfiles;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

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
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly IChannelDefinitionRepository _channelRepo;
    private readonly IAgentExecutor _executor;
    private readonly IRuntimeModelProfileStore _runtimeProfiles;
    private readonly ILogger<ConversationThreadsController> _logger;
    
    public ConversationThreadsController(
        IConversationThreadRepository threadRepo,
        IAgentDefinitionRepository agentRepo,
        IChannelSessionRepository sessionRepo,
        IChannelDefinitionRepository channelRepo,
        IAgentExecutor executor,
        IRuntimeModelProfileStore runtimeProfiles,
        ILogger<ConversationThreadsController> logger)
    {
        _threadRepo = threadRepo;
        _agentRepo = agentRepo;
        _sessionRepo = sessionRepo;
        _channelRepo = channelRepo;
        _executor = executor;
        _runtimeProfiles = runtimeProfiles;
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
        if (!TryGetUserId(out var userId, out var authFailure))
            return authFailure;

        // Validate agent exists
        var agent = await _agentRepo.GetByIdAsync(request.AgentId, tenantId, ct);
        if (agent is null)
            return NotFound($"Agent '{request.AgentId}' not found");
        
        // Generate thread key if not provided
        var threadKey = request.ThreadKey ?? GenerateThreadKey(agent, userId);
        
        // Determine TTL from agent config or request
        var ttl = request.ExpiresIn ?? agent.Session.DefaultThreadTtl;
        var maxTurns = request.MaxTurns ?? agent.Session.MaxTurnsPerThread;
        
        var thread = ConversationThread.Create(
            tenantId: tenantId,
            threadKey: threadKey,
            agentDefinitionId: agent.Id,
            userId: userId,
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
            CreatedAt = thread.CreatedAt,
            LastActivityAt = thread.LastActivityAt,
            Metadata = thread.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
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
        if (!TryGetUserId(out var userId, out var authFailure))
            return authFailure;

        // Load thread
        var thread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return NotFound("Thread not found or expired");
        
        // Security: Verify ownership
        if (thread.UserId != userId)
            return Forbid("You do not own this thread");
        
        // Execute agent with thread context
        var agent = await _agentRepo.GetByIdAsync(thread.AgentDefinitionId, tenantId, ct);
        if (agent is null)
            return NotFound($"Agent '{thread.AgentDefinitionId}' not found");

        var metadata = new Dictionary<string, string>();
        ApplyRuntimeProfileMetadata(agent, metadata);

        var executionRequest = new AgentExecutionRequest
        {
            TenantId = tenantId,
            AgentKey = thread.AgentDefinitionId,
            UserId = userId,
            UserMessage = request.Message,
            ContextJson = request.Context,
            CorrelationId = thread.Id,
            ThreadId = thread.Id,
            Priority = ExecutionPriority.Normal,
            Metadata = metadata
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
        if (!TryGetUserId(out var userId, out var authFailure))
            return authFailure;

        var thread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return NotFound(new { message = "Thread not found or expired." });
        
        // Security: Verify ownership
        if (thread.UserId != userId)
            return Forbid("Thread access denied.");
        
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
        if (!TryGetUserId(out var userId, out var authFailure))
            return authFailure;

        var thread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return NotFound();
        
        // Security: Verify ownership
        if (thread.UserId != userId)
            return Forbid();
        
        return Ok(await MapThreadAsync(thread, ct));
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
        if (!TryGetUserId(out var userId, out var authFailure))
            return authFailure;

        IReadOnlyList<ConversationThread> threads;
        
        if (!string.IsNullOrEmpty(agentId))
        {
            threads = (await _threadRepo.GetByAgentAsync(agentId, tenantId, ct: ct))
                .Where(t => t.UserId == userId)
                .ToList();
        }
        else
        {
            threads = await _threadRepo.GetActiveByUserAsync(userId, tenantId, ct);
        }
        
        var response = new List<ThreadResponse>(threads.Count);
        foreach (var thread in threads)
        {
            response.Add(await MapThreadAsync(thread, ct));
        }
        
        return Ok(response);
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetInboxMetrics(
        [FromRoute] string tenantId,
        [FromQuery] string? agentId = null,
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId, out var authFailure))
            return authFailure;

        IReadOnlyList<ConversationThread> threads = string.IsNullOrWhiteSpace(agentId)
            ? await _threadRepo.GetActiveByUserAsync(userId, tenantId, ct)
            : (await _threadRepo.GetByAgentAsync(agentId, tenantId, ct: ct)).Where(t => t.UserId == userId).ToList();

        var now = DateTimeOffset.UtcNow;
        var durations = threads
            .Where(t => t.LastActivityAt.HasValue && t.LastActivityAt.Value >= t.CreatedAt)
            .Select(t => (t.LastActivityAt!.Value - t.CreatedAt).TotalMinutes)
            .ToList();
        var avgFirstResponseMinutes = durations.Count > 0 ? Math.Round(durations.Average(), 1) : 0;

        var resolvedCount = threads.Count(t => t.Status == ThreadStatus.Archived);
        var resolutionRate = threads.Count > 0 ? Math.Round((double)resolvedCount * 100 / threads.Count, 1) : 0;

        var slaBreaches = 0;
        var threadsByAssignee = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var backlogByChannel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var backlogByStatus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var thread in threads)
        {
            var metadata = thread.Metadata;
            if (metadata.TryGetValue("slaDueAt", out var slaRaw) &&
                DateTimeOffset.TryParse(slaRaw, out var dueAt) &&
                dueAt < now)
            {
                slaBreaches++;
            }

            var assignee = metadata.TryGetValue("assignedTo", out var assignedTo) && !string.IsNullOrWhiteSpace(assignedTo)
                ? assignedTo
                : "unassigned";
            threadsByAssignee[assignee] = threadsByAssignee.TryGetValue(assignee, out var count) ? count + 1 : 1;

            var channel = metadata.TryGetValue("channel", out var channelValue) && !string.IsNullOrWhiteSpace(channelValue)
                ? channelValue
                : "unknown";
            backlogByChannel[channel] = backlogByChannel.TryGetValue(channel, out var channelCount) ? channelCount + 1 : 1;

            var statusKey = thread.Status.ToString();
            backlogByStatus[statusKey] = backlogByStatus.TryGetValue(statusKey, out var statusCount) ? statusCount + 1 : 1;
        }

        var assignedBuckets = threadsByAssignee.Where(kv => !kv.Key.Equals("unassigned", StringComparison.OrdinalIgnoreCase)).ToList();
        var threadsPerAgent = assignedBuckets.Count > 0 ? Math.Round(assignedBuckets.Average(kv => kv.Value), 2) : 0;

        return Ok(new InboxMetricsResponse
        {
            TotalThreads = threads.Count,
            AvgFirstResponseMinutes = avgFirstResponseMinutes,
            ResolutionRatePercent = resolutionRate,
            SlaBreaches = slaBreaches,
            ThreadsPerAgent = threadsPerAgent,
            ThreadsByAssignee = threadsByAssignee,
            BacklogByChannel = backlogByChannel,
            BacklogByStatus = backlogByStatus
        });
    }

    [HttpPost("{threadId}/inbox")]
    public async Task<IActionResult> UpdateInbox(
        [FromRoute] string tenantId,
        [FromRoute] string threadId,
        [FromBody] UpdateThreadInboxRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId, out var authFailure))
            return authFailure;

        var thread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return NotFound();

        if (thread.UserId != userId)
            return Forbid();

        var metadataUpdates = new Dictionary<string, string?>();

        if (request.AssignedTo is not null)
            metadataUpdates["assignedTo"] = request.AssignedTo;
        if (request.SlaDueAt is not null)
            metadataUpdates["slaDueAt"] = request.SlaDueAt.Value.ToString("O");
        if (request.InternalNote is not null)
            metadataUpdates["internalNote"] = request.InternalNote;
        if (request.Channel is not null)
            metadataUpdates["channel"] = request.Channel;
        if (request.Tags is not null)
            metadataUpdates["tags"] = string.Join(",", request.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));

        if (metadataUpdates.Count > 0)
        {
            var metadataResult = thread.UpdateMetadata(metadataUpdates, userId);
            if (!metadataResult.IsSuccess)
                return BadRequest(metadataResult.Error);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<ThreadStatus>(request.Status, true, out var parsed))
                return BadRequest($"Invalid status '{request.Status}'.");

            var statusResult = thread.SetStatus(parsed, userId);
            if (!statusResult.IsSuccess)
                return BadRequest(statusResult.Error);
        }

        await _threadRepo.UpdateAsync(thread, ct);
        return Ok(await MapThreadAsync(thread, ct));
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
        if (!TryGetUserId(out var userId, out var authFailure))
            return authFailure;

        var thread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return NotFound();
        
        // Security: Verify ownership
        if (thread.UserId != userId)
            return Forbid();
        
        var result = thread.Archive(userId);
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
        if (!TryGetUserId(out var userId, out var authFailure))
            return authFailure;

        var thread = await _threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return NotFound();
        
        // Security: Verify ownership
        if (thread.UserId != userId)
            return Forbid();
        
        var result = await _threadRepo.DeleteAsync(threadId, tenantId, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        
        return NoContent();
    }
    
    // --- Helpers ---
    
    private bool TryGetUserId(out string userId, out IActionResult failureResult)
    {
        userId = string.Empty;
        failureResult = Unauthorized();

        var user = HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        userId =
            user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("oid");

        if (string.IsNullOrWhiteSpace(userId))
        {
            failureResult = Forbid();
            return false;
        }

        return true;
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

    private async Task<ThreadResponse> MapThreadAsync(ConversationThread thread, CancellationToken ct)
    {
        var metadata = thread.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value);
        if (!metadata.ContainsKey("channel") || !metadata.ContainsKey("assignedTo"))
        {
            var session = await _sessionRepo.GetByIdAsync(thread.Id, thread.TenantId, ct);
            if (session is not null)
            {
                if (!metadata.ContainsKey("assignedTo") && !string.IsNullOrWhiteSpace(session.AgentId))
                    metadata["assignedTo"] = session.AgentId;

                if (!metadata.ContainsKey("channel"))
                {
                    var channel = await _channelRepo.GetByIdAsync(session.ChannelId, thread.TenantId, ct);
                    if (channel is not null)
                        metadata["channel"] = channel.Type.ToString();
                }
            }
        }

        return new ThreadResponse
        {
            ThreadId = thread.Id,
            ThreadKey = thread.ThreadKey,
            AgentId = thread.AgentDefinitionId,
            Status = thread.Status.ToString(),
            ExpiresAt = thread.ExpiresAt,
            MaxTurns = thread.MaxTurns,
            TurnCount = thread.TurnCount,
            CreatedAt = thread.CreatedAt,
            LastActivityAt = thread.LastActivityAt,
            Metadata = metadata
        };
    }

    private void ApplyRuntimeProfileMetadata(AgentDefinition agentDef, Dictionary<string, string> metadata)
    {
        var profileId = agentDef.Session.RuntimeModelProfileId;
        RuntimeModelProfile? profile = null;
        var profileSource = "runtime-default";

        if (!string.IsNullOrWhiteSpace(profileId))
        {
            profile = _runtimeProfiles.Get(agentDef.TenantId, profileId!);
            if (profile is not null)
                profileSource = "agent-explicit";
        }

        profile ??= _runtimeProfiles.GetDefault(agentDef.TenantId, agentDef.Session.RuntimeKind.ToString());
        if (profile is null)
        {
            metadata["runtimeModelProfileSource"] = "none";
            return;
        }

        metadata["runtimeModelProfileId"] = profile.Id;
        metadata["runtimeModelProfileSource"] = profileSource;
        profile.ApplyExecutionMetadata(metadata);
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
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record UpdateThreadInboxRequest
{
    public string? AssignedTo { get; init; }
    public string? Status { get; init; }
    public List<string>? Tags { get; init; }
    public DateTimeOffset? SlaDueAt { get; init; }
    public string? InternalNote { get; init; }
    public string? Channel { get; init; }
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

public sealed record InboxMetricsResponse
{
    public int TotalThreads { get; init; }
    public double AvgFirstResponseMinutes { get; init; }
    public double ResolutionRatePercent { get; init; }
    public int SlaBreaches { get; init; }
    public double ThreadsPerAgent { get; init; }
    public Dictionary<string, int> ThreadsByAssignee { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> BacklogByChannel { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> BacklogByStatus { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
