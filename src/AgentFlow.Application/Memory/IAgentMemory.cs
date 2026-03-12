using AgentFlow.Domain.Enums;

namespace AgentFlow.Application.Memory;

/// <summary>
/// Working memory - per execution, short-lived.
/// Backed by Redis. Expires after TTL.
/// </summary>
public interface IWorkingMemory
{
    Task<string?> GetAsync(string executionId, string key, CancellationToken ct = default);
    Task SetAsync(string executionId, string key, string value, TimeSpan? ttl = null, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(string executionId, CancellationToken ct = default);
    Task ClearAsync(string executionId, CancellationToken ct = default);
}

/// <summary>
/// Long-term memory - persisted per agent across executions.
/// Backed by MongoDB. Structured data.
/// </summary>
public interface ILongTermMemory
{
    Task<string?> RememberAsync(string agentId, string tenantId, string key, CancellationToken ct = default);
    Task StoreAsync(string agentId, string tenantId, string key, string value, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryEntry>> SearchByKeyPatternAsync(string agentId, string tenantId, string pattern, CancellationToken ct = default);
    Task ForgetAsync(string agentId, string tenantId, string key, CancellationToken ct = default);
}

/// <summary>
/// Vector memory - semantic similarity search.
/// Backed by a vector DB (Qdrant, Azure AI Search, etc.).
/// </summary>
public interface IVectorMemory
{
    Task<string> StoreEmbeddingAsync(
        string agentId,
        string tenantId,
        string content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<VectorMemoryResult>> SearchAsync(
        string agentId,
        string tenantId,
        string query,
        int topK = 5,
        float minScore = 0.75f,
        CancellationToken ct = default);

    Task DeleteAsync(string agentId, string tenantId, string embeddingId, CancellationToken ct = default);
}

/// <summary>
/// Audit memory - immutable, append-only.
/// Every decision the LLM made, every tool called, every input/output.
/// Backed by MongoDB with WORM semantics (write-once, read-many).
/// </summary>
public interface IAuditMemory
{
    Task RecordAsync(AuditEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> GetForExecutionAsync(string executionId, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> GetForAgentAsync(string agentId, string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(string tenantId, int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> GetByCorrelationAsync(string tenantId, string correlationId, int limit = 200, CancellationToken ct = default);
}

/// <summary>
/// Memory facade - unified entry point for the engine.
/// Orchestrates all memory types based on agent configuration.
/// </summary>
public interface IAgentMemoryService
{
    IWorkingMemory Working { get; }
    ILongTermMemory LongTerm { get; }
    IVectorMemory Vector { get; }
    IAuditMemory Audit { get; }

    Task<string> BuildContextSummaryAsync(
        string agentId,
        string executionId,
        string tenantId,
        string currentQuery,
        CancellationToken ct = default);
}

// --- DTOs ---

public sealed record MemoryEntry
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public DateTimeOffset StoredAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string AgentId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
}

public sealed record VectorMemoryResult
{
    public string EmbeddingId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public float Score { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset StoredAt { get; init; }
}

public sealed record AuditEntry
{
    public string Id { get; init; } = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
    public string ExecutionId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public AuditEventType EventType { get; init; }
    public string EventJson { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string CorrelationId { get; init; } = string.Empty;
}

public enum AuditEventType
{
    ExecutionStarted,
    ExecutionCompleted,
    ExecutionFailed,
    ExecutionCancelled,
    ThinkStep,
    PlanStep,
    ToolInvoked,
    ToolSucceeded,
    ToolFailed,
    MemoryRead,
    MemoryWritten,
    SecurityViolation,
    HandoffRequested,
    HandoffCompleted,
    HandoffFailed,
    RoutingDecision
}
