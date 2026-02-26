using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;
using AgentFlow.Infrastructure.Gateways;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentFlow.Tests.Integration.Orchestration;

public class MafAndMcpContractsTests
{
    [Fact]
    public async Task MafBrain_WhenDisabled_ReturnsCheckpoint_InsteadOfFakeAnswer()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Brains:MAF:Enabled"] = "false"
            })
            .Build();

        var brain = new MafBrain(config, NullLogger<MafBrain>.Instance);

        var result = await brain.ThinkAsync(new ThinkContext
        {
            TenantId = "tenant-1",
            ExecutionId = "exec-1",
            UserMessage = "do something",
            SystemPrompt = "system",
            Iteration = 1,
            AvailableTools = new List<AvailableToolDescriptor>()
        });

        Assert.Equal(ThinkDecision.Checkpoint, result.Decision);
        Assert.Contains("disabled", result.Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.FinalAnswer);
    }

    [Fact]
    public async Task McpToolGateway_WithNonHttpTransport_ReturnsUnsupportedTransport()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Servers:0:Name"] = "demo",
                ["Mcp:Servers:0:Transport"] = "Stdio",
                ["Mcp:Servers:0:Security:Mode"] = "Open"
            })
            .Build();

        var gateway = new McpToolGateway(config, NullLogger<McpToolGateway>.Instance);

        var result = await gateway.ExecuteAsync(
            "demo",
            "anyTool",
            new ToolExecutionContext
            {
                TenantId = "tenant-1",
                UserId = "u1",
                ExecutionId = "exec-1",
                StepId = "step-1",
                CorrelationId = "corr-1",
                InputJson = "{}"
            });

        Assert.False(result.IsSuccess);
        Assert.Equal("MCP_TRANSPORT_UNSUPPORTED", result.ErrorCode);
    }
}
