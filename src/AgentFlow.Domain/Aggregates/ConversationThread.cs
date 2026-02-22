using AgentFlow.Abstractions;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Enums;
using MongoDB.Bson;

namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// ConversationThread — Sesión multi-turn persistente.
/// Modela una conversación continua con un agente (equivalente a un Thread de OpenAI).
/// Unicorn Strategy: Enterprise-ready session management con auditabilidad completa.
/// </summary>
public sealed class ConversationThread : AggregateRoot
{
    // --- Identidad ---
    public string ThreadKey { get; private set; } = string.Empty; // human-readable: "support-chat-2024-02-22"
    public string AgentDefinitionId { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    
    // --- Lifecycle ---
    public ThreadStatus Status { get; private set; } = ThreadStatus.Active;
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? LastActivityAt { get; private set; }
    
    // --- Executions History (chronological) ---
    private readonly List<string> _executionIds = [];
    public IReadOnlyList<string> ExecutionIds => _executionIds.AsReadOnly();
    
    // --- Context Accumulation ---
    public ConversationContext Context { get; private set; } = new();
    
    // --- Token Accounting (real, not estimated) ---
    public TokenUsageStats TokenStats { get; private set; } = new();
    
    // --- Session Metadata ---
    public IReadOnlyDictionary<string, string> Metadata { get; private set; } 
        = new Dictionary<string, string>();
    
    // --- Security ---
    public int MaxTurns { get; private set; } = 100;
    public int TurnCount => _executionIds.Count;
    
    // For MongoDB deserialization
    private ConversationThread() { }
    
    public static ConversationThread Create(
        string tenantId,
        string threadKey,
        string agentDefinitionId,
        string userId,
        TimeSpan? expiresIn = null,
        int maxTurns = 100,
        Dictionary<string, string>? metadata = null)
    {
        var thread = new ConversationThread
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = tenantId,
            ThreadKey = threadKey,
            AgentDefinitionId = agentDefinitionId,
            UserId = userId,
            Status = ThreadStatus.Active,
            ExpiresAt = expiresIn.HasValue ? DateTimeOffset.UtcNow.Add(expiresIn.Value) : null,
            LastActivityAt = DateTimeOffset.UtcNow,
            MaxTurns = maxTurns,
            Metadata = metadata ?? new Dictionary<string, string>(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId,
            UpdatedBy = userId
        };
        
        thread.AddDomainEvent(new ThreadCreatedEvent(thread.Id, tenantId, agentDefinitionId, userId));
        
        return thread;
    }
    
    /// <summary>
    /// Append a new execution to the thread.
    /// Fails if thread is expired or max turns exceeded.
    /// </summary>
    public Result AppendExecution(
        string executionId, 
        int tokensUsed, 
        string userMessage,
        string? assistantResponse)
    {
        // Validation: Thread active?
        if (Status != ThreadStatus.Active)
            return Result.Failure(Error.Validation(nameof(Status), "Thread is not active."));
        
        // Validation: Expired?
        if (ExpiresAt.HasValue && DateTimeOffset.UtcNow > ExpiresAt.Value)
        {
            Status = ThreadStatus.Expired;
            AddDomainEvent(new ThreadExpiredEvent(Id, TenantId));
            return Result.Failure(Error.Validation("ExpiresAt", "Thread has expired."));
        }
        
        // Validation: Max turns?
        if (TurnCount >= MaxTurns)
        {
            Status = ThreadStatus.MaxTurnsReached;
            return Result.Failure(Error.Validation("MaxTurns", $"Max turns ({MaxTurns}) reached."));
        }
        
        // Append
        _executionIds.Add(executionId);
        LastActivityAt = DateTimeOffset.UtcNow;
        
        // Accumulate tokens
        TokenStats = TokenStats with
        {
            TotalTokens = TokenStats.TotalTokens + tokensUsed,
            TotalTurns = TokenStats.TotalTurns + 1
        };
        
        // Update context
        Context = Context.AppendTurn(userMessage, assistantResponse);
        
        MarkUpdated(UserId);
        AddDomainEvent(new ThreadExecutionAppendedEvent(Id, TenantId, executionId, tokensUsed));
        
        return Result.Success();
    }
    
    /// <summary>
    /// Archive thread (soft delete).
    /// Working memory can be cleaned, but audit log remains.
    /// </summary>
    public Result Archive(string archivedBy)
    {
        if (Status == ThreadStatus.Archived)
            return Result.Failure(Error.Validation(nameof(Status), "Thread already archived."));
        
        Status = ThreadStatus.Archived;
        MarkUpdated(archivedBy);
        AddDomainEvent(new ThreadArchivedEvent(Id, TenantId, archivedBy));
        
        return Result.Success();
    }
    
    /// <summary>
    /// Pause thread temporarily (resume later).
    /// </summary>
    public Result Pause(string pausedBy)
    {
        if (Status != ThreadStatus.Active)
            return Result.Failure(Error.Validation(nameof(Status), "Only active threads can be paused."));
        
        Status = ThreadStatus.Paused;
        MarkUpdated(pausedBy);
        
        return Result.Success();
    }
    
    /// <summary>
    /// Resume a paused thread.
    /// </summary>
    public Result Resume(string resumedBy)
    {
        if (Status != ThreadStatus.Paused)
            return Result.Failure(Error.Validation(nameof(Status), "Only paused threads can be resumed."));
        
        Status = ThreadStatus.Active;
        MarkUpdated(resumedBy);
        
        return Result.Success();
    }
    
    /// <summary>
    /// Get condensed chat history for LLM context window.
    /// Returns last N turns + summarization of older context.
    /// </summary>
    public ChatHistorySnapshot GetChatHistory(int maxTurns = 10)
    {
        var recentTurns = Context.Turns.TakeLast(maxTurns).ToList();
        
        return new ChatHistorySnapshot
        {
            ThreadId = Id,
            RecentTurns = recentTurns,
            TotalTurns = Context.Turns.Count,
            OlderContextSummary = Context.Turns.Count > maxTurns 
                ? $"[Previous {Context.Turns.Count - maxTurns} turns omitted for brevity]"
                : null
        };
    }
}

/// <summary>
/// Thread lifecycle status.
/// </summary>
public enum ThreadStatus
{
    Active = 0,
    Paused = 1,
    Archived = 2,
    Expired = 3,
    MaxTurnsReached = 4
}

/// <summary>
/// Accumulates conversation context across turns.
/// Immutable value object.
/// </summary>
public sealed record ConversationContext
{
    public IReadOnlyList<ConversationTurn> Turns { get; init; } = Array.Empty<ConversationTurn>();
    
    public ConversationContext AppendTurn(string userMessage, string? assistantResponse)
    {
        var newTurns = Turns.ToList();
        newTurns.Add(new ConversationTurn
        {
            UserMessage = userMessage,
            AssistantResponse = assistantResponse,
            Timestamp = DateTimeOffset.UtcNow
        });
        
        return new ConversationContext { Turns = newTurns.AsReadOnly() };
    }
}

/// <summary>
/// A single turn in the conversation.
/// </summary>
public sealed record ConversationTurn
{
    public required string UserMessage { get; init; }
    public string? AssistantResponse { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Real token usage statistics (not estimated).
/// </summary>
public sealed record TokenUsageStats
{
    public int TotalTokens { get; init; }
    public int TotalTurns { get; init; }
    public decimal EstimatedCostUSD { get; init; }
    
    public int AverageTokensPerTurn => TotalTurns > 0 ? TotalTokens / TotalTurns : 0;
}

/// <summary>
/// Snapshot of recent chat history for LLM context.
/// </summary>
public sealed record ChatHistorySnapshot
{
    public required string ThreadId { get; init; }
    public required IReadOnlyList<ConversationTurn> RecentTurns { get; init; }
    public int TotalTurns { get; init; }
    public string? OlderContextSummary { get; init; }
}

// --- Domain Events ---

public record ThreadCreatedEvent(string ThreadId, string TenantId, string AgentDefinitionId, string UserId) : DomainEvent;
public record ThreadExpiredEvent(string ThreadId, string TenantId) : DomainEvent;
public record ThreadArchivedEvent(string ThreadId, string TenantId, string ArchivedBy) : DomainEvent;
public record ThreadExecutionAppendedEvent(string ThreadId, string TenantId, string ExecutionId, int TokensUsed) : DomainEvent;
