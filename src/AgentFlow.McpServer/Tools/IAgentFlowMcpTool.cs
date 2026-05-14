namespace AgentFlow.McpServer.Tools;

/// <summary>
/// Contrato que implementa cada tool del AgentFlow MCP Server.
/// El ToolDispatcher descubre e invoca las implementaciones por nombre.
/// </summary>
public interface IAgentFlowMcpTool
{
    /// <summary>Nombre único de la tool (snake_case). Ej: af_list_workflows</summary>
    string Name { get; }

    McpToolDescriptor Descriptor { get; }

    Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest request, CancellationToken ct);
}
