using AgentFlow.Abstractions;

namespace AgentFlow.Infrastructure.Gateways;

/// <summary>
/// Versioned MCP action catalog. Actions are normalized (for example, records.read, files.upload)
/// and map to minimum risk + required permissions.
/// </summary>
public sealed class McpToolActionCatalog : IMcpToolActionCatalog
{
    public const string CurrentVersion = "2026.04.13";

    private static readonly Dictionary<string, McpToolActionDescriptor> Actions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["records.read"] = new()
            {
                Action = "records.read",
                RiskLevel = ToolRiskLevel.Low,
                RequiredPermissions = ["tool:read", "tool:execute:low"]
            },
            ["files.upload"] = new()
            {
                Action = "files.upload",
                RiskLevel = ToolRiskLevel.Medium,
                RequiredPermissions = ["tool:create", "tool:execute:medium"]
            },
            ["files.delete"] = new()
            {
                Action = "files.delete",
                RiskLevel = ToolRiskLevel.High,
                RequiredPermissions = ["tool:delete", "tool:execute:high"]
            },
            ["tools.execute"] = new()
            {
                Action = "tools.execute",
                RiskLevel = ToolRiskLevel.Medium,
                RequiredPermissions = ["tool:execute:medium"]
            }
        };

    public string Version => CurrentVersion;

    public bool TryResolve(string action, out McpToolActionDescriptor descriptor)
        => Actions.TryGetValue(action, out descriptor!);
}
