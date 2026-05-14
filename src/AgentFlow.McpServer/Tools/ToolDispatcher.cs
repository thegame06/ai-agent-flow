namespace AgentFlow.McpServer.Tools;

/// <summary>
/// Resuelve y enruta las llamadas /invoke a la tool correcta.
/// Construido como scoped para que las tools puedan tener dependencias scoped.
/// </summary>
public sealed class ToolDispatcher
{
    private readonly Dictionary<string, IAgentFlowMcpTool> _tools;

    public ToolDispatcher(IEnumerable<IAgentFlowMcpTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<McpToolDescriptor> GetCatalog() =>
        _tools.Values.Select(t => t.Descriptor).ToList();

    public async Task<McpInvokeResult> InvokeAsync(McpInvokeRequest request, CancellationToken ct)
    {
        if (!_tools.TryGetValue(request.Tool, out var tool))
            return McpInvokeResult.Fail(request.Tool, $"Unknown tool '{request.Tool}'. Call GET /tools to list available tools.");

        try
        {
            return await tool.ExecuteAsync(request, ct);
        }
        catch (Exception ex)
        {
            return McpInvokeResult.Fail(request.Tool, $"Tool execution failed: {ex.Message}");
        }
    }
}
