using AgentFlow.Application.Memory;

namespace AgentFlow.Application.Memory;

public sealed class AgentMemoryService : IAgentMemoryService
{
    public IWorkingMemory Working { get; }
    public ILongTermMemory LongTerm { get; }
    public IVectorMemory Vector { get; }
    public IAuditMemory Audit { get; }

    public AgentMemoryService(
        IWorkingMemory working,
        ILongTermMemory longTerm,
        IVectorMemory vector,
        IAuditMemory audit)
    {
        Working = working;
        LongTerm = longTerm;
        Vector = vector;
        Audit = audit;
    }

    public async Task<string> BuildContextSummaryAsync(
        string agentId,
        string executionId,
        string tenantId,
        string currentQuery,
        CancellationToken ct = default)
    {
        // 1. Fetch Working Memory
        var working = await Working.GetAllAsync(executionId, ct);
        
        // 2. Fetch relevant Long-Term Memory (simple version for now: fetch all)
        // In real apps, we would only fetch relevant parts or use a better strategy
        
        // 3. Vector Memory search (if query is provided)
        var vectorHits = string.IsNullOrWhiteSpace(currentQuery) 
            ? "" 
            : await SearchVectorMemoryFormattedAsync(agentId, tenantId, currentQuery, ct);

        // 4. Combine into a prompt-friendly summary
        var summary = $"""
            [WORKING MEMORY]
            {(working.Any() ? string.Join("\n", working.Select(x => $"{x.Key}: {x.Value}")) : "None")}

            [RELEVANT LTM]
            {vectorHits}
            """;

        return summary;
    }

    private async Task<string> SearchVectorMemoryFormattedAsync(string agentId, string tenantId, string query, CancellationToken ct)
    {
        try 
        {
            var hits = await Vector.SearchAsync(agentId, tenantId, query, ct: ct);
            if (!hits.Any()) return "No relevant semantic context found.";

            return string.Join("\n---\n", hits.Select(h => h.Content));
        }
        catch (Exception)
        {
            // Vector search failing shouldn't crash the agent loop
            return "Vector search unavailable.";
        }
    }
}
