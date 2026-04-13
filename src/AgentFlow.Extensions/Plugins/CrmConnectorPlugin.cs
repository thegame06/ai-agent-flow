using AgentFlow.Abstractions;

namespace AgentFlow.Extensions.Plugins;

public sealed class CrmConnectorPlugin : IToolPlugin
{
    public string ExtensionId => "connector.crm.reference";
    public string Name => "CRM Connector";
    public string Description => "Reference CRM connector plugin for customer lookups and account enrichment.";
    public string Version => "1.0.0";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Medium;
    public string InputSchemaJson => """{"type":"object","properties":{"customerId":{"type":"string"}},"required":["customerId"]}""";
    public string? OutputSchemaJson => """{"type":"object","properties":{"status":{"type":"string"},"source":{"type":"string"}}}""";
    public IReadOnlyList<string> RequiredPermissions => ["crm.read"];
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(ExtensionHealthStatus.Healthy("CRM endpoint reachable (simulated)."));
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) => Task.FromResult(ToolResult.Success("{\"status\":\"ok\",\"source\":\"crm\"}"));
}
