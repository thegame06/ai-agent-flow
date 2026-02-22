using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgentFlow.Extensions.Tools;

/// <summary>
/// A deterministic tool for basic arithmetic.
/// Example of a "Low" risk tool that runs in-process.
/// </summary>
public sealed class CalculatorTool : IToolPlugin
{
    public string ExtensionId => "core.tools.calculator";
    public string Name => "Calculator";
    public string Description => "Perform basic math operations (add, subtract, multiply, divide).";
    public string Version => "1.0.0";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "op": { "type": "string", "enum": ["add", "subtract", "multiply", "divide"] },
            "a": { "type": "number" },
            "b": { "type": "number" }
        },
        "required": ["op", "a", "b"]
    }
    """;

    public string? OutputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "result": { "type": "number" }
        }
    }
    """;

    public IReadOnlyList<string> RequiredPermissions => [];

    private readonly ILogger<CalculatorTool> _logger;

    public CalculatorTool(ILogger<CalculatorTool> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default) => 
        Task.FromResult(ExtensionHealthStatus.Healthy());

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default)
    {
        _logger.LogDebug("CalculatorTool executing with input: {Input}", context.InputJson);

        try
        {
            using var doc = JsonDocument.Parse(context.InputJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("op", out var opProp) || 
                !root.TryGetProperty("a", out var aProp) || 
                !root.TryGetProperty("b", out var bProp))
            {
                return ToolResult.Failure("CALC001", "Missing parameters 'op', 'a', or 'b'.");
            }

            string op = opProp.GetString()?.ToLower() ?? "";
            double a = aProp.GetDouble();
            double b = bProp.GetDouble();

            double result = op switch
            {
                "add" => a + b,
                "subtract" => a - b,
                "multiply" => a * b,
                "divide" => b != 0 ? a / b : throw new DivideByZeroException(),
                _ => throw new ArgumentException($"Unknown operation: {op}")
            };

            return ToolResult.Success(JsonSerializer.Serialize(new { result }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CalculatorTool error");
            return ToolResult.Failure("CALC_ERR", ex.Message);
        }
    }
}
