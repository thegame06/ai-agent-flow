# 🎯 SESSION MANAGEMENT - RECOMENDACIONES ARQUITECTÓNICAS

**Autor**: AgentFlow Master Architect  
**Fecha**: 2026-02-22  
**Status**: PROPUESTA ARQUITECTÓNICA  
**Prioridad**: 🔴 CRÍTICO para producción

---

## 📊 ANÁLISIS DEL ESTADO ACTUAL

### ❌ Gaps Críticos Detectados

```
┌─────────────────────────────────────────────────────────────┐
│ PROBLEMA                │ IMPACTO          │ SEVERIDAD      │
├─────────────────────────────────────────────────────────────┤
│ No hay sesiones         │ Sin continuidad  │ 🔴 CRÍTICO     │
│ conversacionales        │ entre mensajes   │                │
├─────────────────────────────────────────────────────────────┤
│ Token tracking          │ Costos no        │ 🟠 ALTO        │
│ estimado (no real)      │ rastreables      │                │
├─────────────────────────────────────────────────────────────┤
│ Working Memory se       │ Pérdida de       │ 🟠 ALTO        │
│ borra post-execution    │ contexto         │                │
├─────────────────────────────────────────────────────────────┤
│ ChatHistory no          │ No hay multi-    │ 🟡 MEDIO       │
│ persiste                │ turn chats       │                │
├─────────────────────────────────────────────────────────────┤
│ No hay SessionId        │ Dificultad para  │ 🟡 MEDIO       │
│ semántico               │ agrupar          │                │
└─────────────────────────────────────────────────────────────┘
```

---

## 🏗️ ARQUITECTURA PROPUESTA: SESSION LAYER

### 1. ConversationThread Aggregate

**Inspirado en**: OpenAI Assistants API (Threads), LangChain Memory

#### Nuevo Aggregate Root

```csharp
namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// ConversationThread — Sesión multi-turn persistente.
/// Modela una conversación continua con un agente (equivalente a un Thread de OpenAI).
/// Unicorn Strategy: Enterprise-ready session management.
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
    
    private ConversationThread() { }
    
    public static ConversationThread Create(
        string tenantId,
        string threadKey,
        string agentDefinitionId,
        string userId,
        TimeSpan? expiresIn = null,
        int maxTurns = 100)
    {
        return new ConversationThread
        {
            TenantId = tenantId,
            ThreadKey = threadKey,
            AgentDefinitionId = agentDefinitionId,
            UserId = userId,
            Status = ThreadStatus.Active,
            ExpiresAt = expiresIn.HasValue ? DateTimeOffset.UtcNow.Add(expiresIn.Value) : null,
            LastActivityAt = DateTimeOffset.UtcNow,
            MaxTurns = maxTurns,
            CreatedBy = userId,
            UpdatedBy = userId
        };
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
            return Result.Failure(Error.DomainError("Thread is not active."));
        
        // Validation: Expired?
        if (ExpiresAt.HasValue && DateTimeOffset.UtcNow > ExpiresAt.Value)
        {
            Status = ThreadStatus.Expired;
            AddDomainEvent(new ThreadExpiredEvent(Id, TenantId));
            return Result.Failure(Error.DomainError("Thread has expired."));
        }
        
        // Validation: Max turns?
        if (TurnCount >= MaxTurns)
        {
            Status = ThreadStatus.MaxTurnsReached;
            return Result.Failure(Error.DomainError($"Max turns ({MaxTurns}) reached."));
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
        AddDomainEvent(new ThreadExecutionAppendedEvent(Id, TenantId, executionId));
        
        return Result.Success();
    }
    
    /// <summary>
    /// Archive thread (soft delete).
    /// Working memory can be cleaned, but audit log remains.
    /// </summary>
    public Result Archive(string archivedBy)
    {
        if (Status == ThreadStatus.Archived)
            return Result.Failure(Error.DomainError("Thread already archived."));
        
        Status = ThreadStatus.Archived;
        MarkUpdated(archivedBy);
        AddDomainEvent(new ThreadArchivedEvent(Id, TenantId, archivedBy));
        
        return Result.Success();
    }
    
    /// <summary>
    /// Get condensed chat history for LLM context window.
    /// Returns last N turns + summarization of older context.
    /// </summary>
    public ChatHistorySnapshot GetChatHistory(int maxTurns = 10)
    {
        return new ChatHistorySnapshot
        {
            ThreadId = Id,
            RecentTurns = Context.Turns.TakeLast(maxTurns).ToList(),
            TotalTurns = Context.Turns.Count,
            OlderContextSummary = Context.Turns.Count > maxTurns 
                ? $"[Previous {Context.Turns.Count - maxTurns} turns omitted for brevity]"
                : null
        };
    }
}

public enum ThreadStatus { Active, Paused, Archived, Expired, MaxTurnsReached }

public sealed record ConversationContext
{
    private readonly List<ConversationTurn> _turns = [];
    public IReadOnlyList<ConversationTurn> Turns => _turns.AsReadOnly();
    
    public ConversationContext AppendTurn(string userMessage, string? assistantResponse)
    {
        var newTurns = _turns.ToList();
        newTurns.Add(new ConversationTurn
        {
            UserMessage = userMessage,
            AssistantResponse = assistantResponse,
            Timestamp = DateTimeOffset.UtcNow
        });
        
        return new ConversationContext { _turns = newTurns };
    }
}

public sealed record ConversationTurn
{
    public required string UserMessage { get; init; }
    public string? AssistantResponse { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

public sealed record TokenUsageStats
{
    public int TotalTokens { get; init; }
    public int TotalTurns { get; init; }
    public decimal EstimatedCostUSD { get; init; }
    
    public int AverageTokensPerTurn => TotalTurns > 0 ? TotalTokens / TotalTurns : 0;
}

public sealed record ChatHistorySnapshot
{
    public required string ThreadId { get; init; }
    public required IReadOnlyList<ConversationTurn> RecentTurns { get; init; }
    public int TotalTurns { get; init; }
    public string? OlderContextSummary { get; init; }
}

// --- Domain Events ---
public record ThreadExpiredEvent(string ThreadId, string TenantId) : DomainEvent;
public record ThreadArchivedEvent(string ThreadId, string TenantId, string ArchivedBy) : DomainEvent;
public record ThreadExecutionAppendedEvent(string ThreadId, string TenantId, string ExecutionId) : DomainEvent;
```

---

### 2. Repository & Infrastructure

```csharp
namespace AgentFlow.Domain.Repositories;

public interface IConversationThreadRepository
{
    Task<ConversationThread?> GetByIdAsync(string threadId, string tenantId, CancellationToken ct = default);
    Task<ConversationThread?> GetByKeyAsync(string threadKey, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationThread>> GetActiveByUserAsync(string userId, string tenantId, CancellationToken ct = default);
    Task<Result> InsertAsync(ConversationThread thread, CancellationToken ct = default);
    Task<Result> UpdateAsync(ConversationThread thread, CancellationToken ct = default);
}
```

```csharp
namespace AgentFlow.Infrastructure.Persistence;

/// <summary>
/// MongoDB implementation with TTL index on ExpiresAt.
/// Auto-cleanup of expired threads.
/// </summary>
public sealed class MongoConversationThreadRepository : IConversationThreadRepository
{
    private readonly IMongoCollection<ConversationThread> _collection;
    
    public MongoConversationThreadRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ConversationThread>("conversation_threads");
        
        // TTL Index: Auto-delete expired threads
        _collection.Indexes.CreateOne(new CreateIndexModel<ConversationThread>(
            Builders<ConversationThread>.IndexKeys.Ascending(x => x.ExpiresAt),
            new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }
        ));
        
        // Performance index
        _collection.Indexes.CreateOne(new CreateIndexModel<ConversationThread>(
            Builders<ConversationThread>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.ThreadKey)
        ));
    }
    
    // Implementation omitted for brevity...
}
```

---

### 3. Enhanced AgentExecutionEngine Integration

#### Modificaciones en `AgentExecutionEngine.cs`

```csharp
public sealed class AgentExecutionEngine : IAgentExecutor
{
    private readonly IConversationThreadRepository _threadRepo; // ✅ NEW
    
    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken ct = default)
    {
        // --- 0. Thread Management ---
        ConversationThread? thread = null;
        
        if (!string.IsNullOrEmpty(request.ThreadId))
        {
            thread = await _threadRepo.GetByIdAsync(request.ThreadId, request.TenantId, ct);
            
            if (thread is null)
            {
                return AgentExecutionResult.FromError(
                    "ThreadNotFound", 
                    $"Thread '{request.ThreadId}' not found or expired.");
            }
        }
        
        // --- 1. Load Agent Definition ---
        var agentDef = await _agentRepo.GetByIdAsync(request.AgentKey, request.TenantId, ct);
        // ... (existing code)
        
        // --- 2. Create Execution (with ThreadId linkage) ---
        var execution = AgentExecution.Create(
            tenantId: request.TenantId,
            agentDefinitionId: agentDef.Id.ToString(),
            triggeredBy: request.UserId,
            input: new ExecutionInput { UserMessage = request.UserMessage },
            maxIterations: agentDef.LoopConfig.MaxIterations,
            correlationId: request.CorrelationId ?? Guid.NewGuid().ToString(),
            parentExecutionId: request.ParentExecutionId,
            threadId: thread?.Id, // ✅ NEW: Link to thread
            priority: (AgentFlow.Abstractions.ExecutionPriority)(int)request.Priority);
        
        // ... (existing execution logic)
        
        // --- 5. Post-Execution: Update Thread ---
        if (thread is not null && execution.Status == ExecutionStatus.Completed)
        {
            var appendResult = thread.AppendExecution(
                executionId: execution.Id,
                tokensUsed: execution.Output?.TotalTokensUsed ?? 0, // ✅ Real tokens
                userMessage: request.UserMessage,
                assistantResponse: execution.Output?.FinalResponse
            );
            
            if (appendResult.IsSuccess)
            {
                await _threadRepo.UpdateAsync(thread, ct);
            }
        }
        
        return MapToResult(execution, agentDef);
    }
}
```

---

### 4. SemanticKernelBrain: ChatHistory Persistence

#### Modificación para usar Thread Context

```csharp
public sealed class SemanticKernelBrain : IAgentBrain
{
    public async Task<ThinkResult> ThinkAsync(ThinkContext context, CancellationToken ct = default)
    {
        var history = new ChatHistory();
        
        // System prompt
        history.AddSystemMessage(BuildSystemPrompt(context));
        
        // ✅ NEW: Load previous turns from Thread (if available)
        if (context.ThreadSnapshot is not null)
        {
            foreach (var turn in context.ThreadSnapshot.RecentTurns)
            {
                history.AddUserMessage(turn.UserMessage);
                if (!string.IsNullOrEmpty(turn.AssistantResponse))
                    history.AddAssistantMessage(turn.AssistantResponse);
            }
        }
        
        // ✅ FALLBACK: Use execution steps (current approach)
        else
        {
            foreach (var stepObj in context.History.TakeLast(10))
            {
                if (stepObj is Domain.ValueObjects.AgentStep step)
                {
                    if (step.LlmPrompt is not null)
                        history.AddUserMessage(step.LlmPrompt);
                    if (step.LlmResponse is not null)
                        history.AddAssistantMessage(step.LlmResponse);
                }
            }
        }
        
        // Current message
        history.AddUserMessage(BuildUserMessage(context));
        
        // ... (rest of the method)
        
        // ✅ NEW: Extract REAL token usage from metadata
        var response = await _chatCompletion.GetChatMessageContentAsync(
            history, settings, _kernel, ct);
        
        var tokensUsed = ExtractRealTokenUsage(response.Metadata);
        
        return ParseThinkResult(response.Content ?? "{}", tokensUsed);
    }
    
    /// <summary>
    /// Extract REAL token usage from LLM response metadata.
    /// OpenAI returns: metadata["usage"] = { prompt_tokens, completion_tokens, total_tokens }
    /// </summary>
    private int ExtractRealTokenUsage(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null) return 0;
        
        // OpenAI format
        if (metadata.TryGetValue("usage", out var usageObj) && usageObj is Dictionary<string, object> usage)
        {
            if (usage.TryGetValue("total_tokens", out var totalObj) && totalObj is int total)
                return total;
        }
        
        // Anthropic format
        if (metadata.TryGetValue("token_usage", out var anthropicUsage) 
            && anthropicUsage is Dictionary<string, object> anthUsage)
        {
            var inputTokens = anthUsage.TryGetValue("input_tokens", out var inp) && inp is int inpInt ? inpInt : 0;
            var outputTokens = anthUsage.TryGetValue("output_tokens", out var outp) && outp is int outpInt ? outpInt : 0;
            return inputTokens + outputTokens;
        }
        
        return 0; // Unknown format, fallback to estimation
    }
}
```

#### Updated ThinkContext Contract

```csharp
namespace AgentFlow.Abstractions;

public sealed record ThinkContext
{
    public required string ExecutionId { get; init; }
    public required string TenantId { get; init; }
    public required string UserMessage { get; init; }
    public required string SystemPrompt { get; init; }
    public required int Iteration { get; init; }
    public required IReadOnlyList<ToolMetadata> AvailableTools { get; init; }
    public required IReadOnlyList<object> History { get; init; } // AgentStep objects
    public required string WorkingMemoryJson { get; init; }
    
    // ✅ NEW: Thread context for multi-turn conversations
    public ChatHistorySnapshot? ThreadSnapshot { get; init; }
}
```

---

### 5. API Controllers Enhancement

```csharp
namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/threads")]
public sealed class ConversationThreadsController : ControllerBase
{
    private readonly IConversationThreadRepository _repo;
    private readonly IAgentExecutor _executor;
    
    /// <summary>
    /// Create a new conversation thread.
    /// Similar to OpenAI Assistants API: POST /threads
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateThread(
        [FromRoute] string tenantId,
        [FromBody] CreateThreadRequest request)
    {
        var thread = ConversationThread.Create(
            tenantId: tenantId,
            threadKey: request.ThreadKey ?? $"thread-{Guid.NewGuid():N}",
            agentDefinitionId: request.AgentId,
            userId: GetUserId(),
            expiresIn: request.ExpiresIn ?? TimeSpan.FromDays(7),
            maxTurns: request.MaxTurns ?? 100
        );
        
        await _repo.InsertAsync(thread);
        
        return Ok(new ThreadResponse
        {
            ThreadId = thread.Id,
            ThreadKey = thread.ThreadKey,
            Status = thread.Status.ToString(),
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
        [FromBody] SendMessageRequest request)
    {
        var thread = await _repo.GetByIdAsync(threadId, tenantId);
        if (thread is null) return NotFound();
        
        // Execute agent with thread context
        var result = await _executor.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = tenantId,
            AgentKey = thread.AgentDefinitionId,
            UserId = GetUserId(),
            UserMessage = request.Message,
            ThreadId = threadId, // ✅ Pass thread context
            CorrelationId = thread.Id
        });
        
        return Ok(new MessageResponse
        {
            ExecutionId = result.ExecutionId,
            AssistantResponse = result.FinalResponse,
            TokensUsed = result.TotalTokensUsed,
            TotalTurns = thread.TurnCount + 1
        });
    }
    
    /// <summary>
    /// Get thread history with chat turns.
    /// </summary>
    [HttpGet("{threadId}/history")]
    public async Task<IActionResult> GetHistory(
        [FromRoute] string tenantId,
        [FromRoute] string threadId,
        [FromQuery] int maxTurns = 50)
    {
        var thread = await _repo.GetByIdAsync(threadId, tenantId);
        if (thread is null) return NotFound();
        
        var history = thread.GetChatHistory(maxTurns);
        
        return Ok(new ThreadHistoryResponse
        {
            ThreadId = thread.Id,
            Turns = history.RecentTurns.Select(t => new TurnDto
            {
                UserMessage = t.UserMessage,
                AssistantResponse = t.AssistantResponse,
                Timestamp = t.Timestamp
            }).ToList(),
            TotalTurns = history.TotalTurns,
            TokenStats = thread.TokenStats
        });
    }
}
```

---

### 6. Working Memory Evolution Strategy

#### Propuesta: Hybrid Memory Model

```csharp
public interface IWorkingMemory
{
    // ✅ EXISTING: Per-execution ephemeral memory
    Task SetAsync(string executionId, string key, string value, TimeSpan? ttl = null, CancellationToken ct = default);
    Task<string?> GetAsync(string executionId, string key, CancellationToken ct = default);
    
    // ✅ NEW: Per-thread persistent memory (survives across turns)
    Task SetThreadContextAsync(string threadId, string key, string value, CancellationToken ct = default);
    Task<string?> GetThreadContextAsync(string threadId, string key, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetAllThreadContextAsync(string threadId, CancellationToken ct = default);
    
    // ✅ NEW: Thread memory cleanup (on archive)
    Task ClearThreadAsync(string threadId, CancellationToken ct = default);
}
```

#### Redis Implementation

```csharp
public sealed class RedisMemory : IWorkingMemory
{
    private readonly IDatabase _redis;
    
    // Execution-scoped (existing)
    public async Task SetAsync(string executionId, string key, string value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var redisKey = $"exec:{executionId}:{key}";
        await _redis.StringSetAsync(redisKey, value, ttl ?? TimeSpan.FromHours(1));
    }
    
    // ✅ NEW: Thread-scoped (persistent across turns)
    public async Task SetThreadContextAsync(string threadId, string key, string value, CancellationToken ct = default)
    {
        var redisKey = $"thread:{threadId}:ctx:{key}";
        // NO TTL = persists until explicitly cleared or thread archived
        await _redis.StringSetAsync(redisKey, value);
    }
    
    public async Task<string?> GetThreadContextAsync(string threadId, string key, CancellationToken ct = default)
    {
        var redisKey = $"thread:{threadId}:ctx:{key}";
        return await _redis.StringGetAsync(redisKey);
    }
    
    public async Task ClearThreadAsync(string threadId, CancellationToken ct = default)
    {
        var pattern = $"thread:{threadId}:ctx:*";
        var server = _redis.Multiplexer.GetServer(_redis.Multiplexer.GetEndPoints().First());
        var keys = server.Keys(pattern: pattern).ToArray();
        
        if (keys.Any())
            await _redis.KeyDeleteAsync(keys);
    }
}
```

---

## 📈 MÉTRICAS DE ÉXITO

### KPIs para Validar la Implementación

```yaml
Continuidad Conversacional:
  - Threads activos concurrentes: Target > 1000
  - Average turns per thread: Target > 5
  - Thread abandonment rate: Target < 20%

Token Accuracy:
  - Real vs Estimated variance: Target < 5%
  - Cost attribution accuracy: Target 100%
  - Token overflow incidents: Target = 0

Memory Performance:
  - Thread context retrieval time: Target < 50ms
  - Working memory hit rate: Target > 95%
  - Long-term memory recall accuracy: Target > 90%

Auditability:
  - All executions traceable to thread: Target 100%
  - Token usage logged per turn: Target 100%
  - Context windows reconstructible: Target 100%
```

---

## 🚀 ROADMAP DE IMPLEMENTACIÓN

### Phase 1: Foundation (Sprint 1-2)
- [ ] Crear `ConversationThread` aggregate
- [ ] Implementar `IConversationThreadRepository`
- [ ] MongoDB infrastructure + TTL indexes
- [ ] Unit tests para thread lifecycle

### Phase 2: Integration (Sprint 3-4)
- [ ] Modificar `AgentExecutionEngine` para thread linkage
- [ ] Update `ThinkContext` con `ThreadSnapshot`
- [ ] Implementar real token tracking en `SemanticKernelBrain`
- [ ] Integration tests

### Phase 3: API Surface (Sprint 5)
- [ ] `ConversationThreadsController`
- [ ] Endpoints: Create, SendMessage, GetHistory
- [ ] OpenAPI spec actualizado
- [ ] Postman collections

### Phase 4: Memory Enhancement (Sprint 6)
- [ ] Thread-scoped working memory
- [ ] Redis implementation
- [ ] Memory migration strategy
- [ ] Performance benchmarks

### Phase 5: Frontend Integration (Sprint 7)
- [ ] Chat UI component (Thread viewer)
- [ ] Message send/receive with threading
- [ ] Token usage display per thread
- [ ] Thread management (create, archive, delete)

---

## 🔐 CONSIDERACIONES DE SEGURIDAD

### Thread Isolation
```csharp
// Validación estricta de tenant + user ownership
public async Task<ConversationThread?> GetByIdAsync(string threadId, string tenantId, CancellationToken ct)
{
    return await _collection.Find(x => 
        x.Id == threadId && 
        x.TenantId == tenantId &&
        x.Deleted == false
    ).FirstOrDefaultAsync(ct);
}
```

### PII in Threads
- **Encrypt at rest**: Thread context debe ser encrypted en MongoDB
- **Retention policy**: Auto-archive threads después de `ExpiresAt`
- **GDPR compliance**: Endpoint DELETE `/threads/{id}` hard-delete

### Thread Hijacking Prevention
```csharp
// Middleware: Validate thread ownership
if (thread.UserId != currentUser.Id && !currentUser.IsAdmin)
{
    return Forbidden("Thread access denied");
}
```

---

## 💰 ANÁLISIS DE COSTO

### Token Savings con Thread Context

**Escenario**: Support chatbot, average 10 turns per session

| Enfoque | Tokens/Turn | Total (10 turns) | Cost @ $0.01/1K |
|---------|-------------|------------------|-----------------|
| **Sin Thread** (cada turn es nuevo) | 500 | 5,000 | $0.050 |
| **Con Thread** (context reutilizado) | 300 | 3,000 | $0.030 |
| **Savings** | -40% | -40% | **$0.020** |

**Escala**: 10,000 sessions/day = **$200/day savings** = **$73K/year**

---

## 🎓 REFERENCIAS

### Frameworks de Referencia

1. **OpenAI Assistants API (Threads)**
   - https://platform.openai.com/docs/assistants/overview
   - Thread-based conversation model
   - Message history persistence

2. **LangChain ConversationChain**
   - https://python.langchain.com/docs/modules/memory/
   - Multiple memory backends
   - Buffer, summary, entity memories

3. **Semantic Kernel ChatHistory**
   - https://learn.microsoft.com/semantic-kernel/
   - Turn-based history management
   - Role-based messages

4. **AutoGen ConversableAgent**
   - https://microsoft.github.io/autogen/
   - Multi-agent conversations
   - Context handoff between agents

---

## ✅ APROBACIÓN REQUERIDA

**CTO Sign-off**: ________________  
**Date**: ________________  

**Lead Architect Sign-off**: ________________  
**Date**: ________________  

---

**Next Steps**: Presentar en Architecture Review Board (ARB) para aprobación final.
