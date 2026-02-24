using AgentFlow.Abstractions;

namespace AgentFlow.Infrastructure.Gateways;

public sealed class McpServerConfig
{
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = "Stdio"; // Stdio, Http
    public string? Command { get; set; }
    public string[]? Arguments { get; set; }
    public string? Url { get; set; }
    public McpSecurityConfig Security { get; set; } = new();
}

public sealed class McpSecurityConfig
{
    public string Mode { get; set; } = "Restricted"; // Open, Restricted, Enterprise
    public List<string> AllowedTenants { get; set; } = new(); 
    public ToolRiskLevel DefaultRiskLevel { get; set; } = ToolRiskLevel.Medium;
    public bool EnableAuditLogs { get; set; } = true;
    public string? AuthSecretName { get; set; } 
}
