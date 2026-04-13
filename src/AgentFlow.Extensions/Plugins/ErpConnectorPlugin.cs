using AgentFlow.Abstractions;

namespace AgentFlow.Extensions.Plugins;

public sealed class ErpConnectorPlugin : IToolPlugin
{
    public string ExtensionId => "connector.erp.reference";
    public string Name => "ERP Connector";
    public string Description => "Reference ERP connector plugin for inventory and order synchronization.";
    public string Version => "1.0.0";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.High;
    public string InputSchemaJson => """{"type":"object","properties":{"operation":{"type":"string"}},"required":["operation"]}""";
    public string? OutputSchemaJson => """{"type":"object","properties":{"status":{"type":"string"},"source":{"type":"string"}}}""";
    public IReadOnlyList<string> RequiredPermissions => ["erp.read", "erp.write"];
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(ExtensionHealthStatus.Healthy("ERP bridge healthy (simulated)."));
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default) => Task.FromResult(ToolResult.Success("{\"status\":\"ok\",\"source\":\"erp\"}"));
}
