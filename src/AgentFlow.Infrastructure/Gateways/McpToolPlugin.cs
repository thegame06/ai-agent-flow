using AgentFlow.Abstractions;

namespace AgentFlow.Infrastructure.Gateways;

/// <summary>
/// A "Ghost Tool" that proxies calls to a remote MCP (Model Context Protocol) server.
/// This allows remote tools to be governed by the same local policies as native tools.
/// </summary>
public sealed class McpToolPlugin : IToolPlugin
{
    private readonly IMcpToolGateway _gateway;
    private readonly string _serverName;
    private readonly string _toolName;
    private readonly string _description;
    private readonly string _inputSchema;
    private readonly ToolRiskLevel _riskLevel;

    public McpToolPlugin(
        IMcpToolGateway gateway,
        string serverName,
        string toolName,
        string description,
        string inputSchema,
        ToolRiskLevel riskLevel = ToolRiskLevel.Medium)
    {
        _gateway = gateway;
        _serverName = serverName;
        _toolName = toolName;
        _description = description;
        _inputSchema = inputSchema;
        _riskLevel = riskLevel;
    }

    public string Name => $"{_serverName}_{_toolName}";
    public string Description => $"[MCP] {_description}";
    public string InputSchemaJson => _inputSchema;
    public string? OutputSchemaJson => null;
    public ToolRiskLevel RiskLevel => _riskLevel;
    public IReadOnlyList<string> RequiredPermissions => new List<string> { "Mcp.Invoke" };

    public string ExtensionId => $"mcp.{_serverName}.{_toolName}";
    public string Version => "1.0.0";

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default)
    {
        // Add MCP-specific metadata to context if needed
        return await _gateway.ExecuteAsync(_serverName, _toolName, context, ct);
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        // Future: Check if MCP server is reachable
        return Task.FromResult(ExtensionHealthStatus.Healthy());
    }
}
