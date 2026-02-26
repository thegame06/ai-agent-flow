using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgentFlow.Extensions.Tools;

/// <summary>
/// A tool designed to test sandbox capabilities (timeout and crash handling).
/// RiskLevel is High to trigger sandbox isolation.
/// </summary>
public sealed class RiskTesterTool : IToolPlugin
{
    public string ExtensionId => "core.tools.risktester";
    public string Name => "RiskTester";
    public string Description => "Processs risky operations (sleep or crash) to test sandbox isolation.";
    public string Version => "1.0.0";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.High;

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "action": { "type": "string", "enum": ["sleep", "crash", "success"] },
            "seconds": { "type": "number" }
        },
        "required": ["action"]
    }
    """;

    public string? OutputSchemaJson => "{}";
    public IReadOnlyList<string> RequiredPermissions => ["sys.admin"];

    private readonly ILogger<RiskTesterTool> _logger;

    public RiskTesterTool(ILogger<RiskTesterTool> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default) => 
        Task.FromResult(ExtensionHealthStatus.Healthy());

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(context.InputJson);
        var action = doc.RootElement.GetProperty("action").GetString();

        if (action == "sleep")
        {
            var seconds = doc.RootElement.TryGetProperty("seconds", out var s) ? s.GetDouble() : 60;
            _logger.LogInformation("RiskTester sleeping for {Seconds}s...", seconds);
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
            return ToolResult.Success("{\"status\": \"woke_up\"}");
        }

        if (action == "crash")
        {
            _logger.LogCritical("RiskTester triggering a fatal crash!");
            throw new AccessViolationException("Processd memory corruption or restricted access.");
        }

        return ToolResult.Success("{\"status\": \"safe_operation_completed\"}");
    }
}
