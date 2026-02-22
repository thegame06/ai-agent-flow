using AgentFlow.Application.Memory;

namespace AgentFlow.Infrastructure.Memory;

public sealed class NullVectorMemory : IVectorMemory
{
    public Task<string> StoreEmbeddingAsync(string agentId, string tenantId, string content, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        return Task.FromResult(Guid.NewGuid().ToString());
    }

    public Task<IReadOnlyList<VectorMemoryResult>> SearchAsync(string agentId, string tenantId, string query, int topK = 5, float minScore = 0.75f, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<VectorMemoryResult>>(new List<VectorMemoryResult>());
    }

    public Task DeleteAsync(string agentId, string tenantId, string embeddingId, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
