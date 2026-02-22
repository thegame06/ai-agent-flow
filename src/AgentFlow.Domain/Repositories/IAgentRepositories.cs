using AgentFlow.Domain.Aggregates;
using AgentFlow.Abstractions;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Repositories;

public interface IAgentDefinitionRepository : IRepository<AgentDefinition>
{
    Task<AgentDefinition?> GetByNameAsync(string name, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentDefinition>> GetPublishedAsync(string tenantId, CancellationToken ct = default);
    Task<int> CountAsync(string tenantId, CancellationToken ct = default);
}

public interface IAgentExecutionRepository : IRepository<AgentExecution>
{
    Task<IReadOnlyList<AgentExecution>> GetByAgentIdAsync(string agentId, string tenantId, int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<AgentExecution>> GetByStatusAsync(ExecutionStatus status, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentExecution>> GetRunningAsync(string tenantId, CancellationToken ct = default);
    Task<long> GetExecutionCountForDayAsync(string tenantId, DateTimeOffset date, CancellationToken ct = default);
    Task<Result> AppendStepAsync(string executionId, string tenantId, Domain.ValueObjects.AgentStep step, CancellationToken ct = default);
}

public interface IToolDefinitionRepository : IRepository<ToolDefinition>
{
    Task<ToolDefinition?> GetByNameAndVersionAsync(string name, string version, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ToolDefinition>> GetPlatformToolsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ToolDefinition>> GetAvailableForTenantAsync(string tenantId, CancellationToken ct = default);
}

public interface IToolExecutionLogRepository
{
    Task<Result> AppendAsync(Aggregates.ToolExecutionLog log, CancellationToken ct = default);
    Task<IReadOnlyList<Aggregates.ToolExecutionLog>> GetByExecutionIdAsync(string executionId, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<Aggregates.ToolExecutionLog>> GetByAgentIdAsync(string agentId, string tenantId, int limit = 100, CancellationToken ct = default);
}

public interface ITenantRepository : IRepository<Tenant>
{
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);
}
