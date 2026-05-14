using AgentFlow.Abstractions;
using AgentFlow.Api.Controllers;
using AgentFlow.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AgentFlow.Tests.Unit.Api;

public sealed class McpControllerTests
{
    [Fact]
    public async Task Invoke_ForOpenServer_AddsDefaultPolicyAndEffectivePermissions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Servers:0:Name"] = "local-test",
                ["Mcp:Servers:0:Transport"] = "Http",
                ["Mcp:Servers:0:Url"] = "http://127.0.0.1:3501/invoke",
                ["Mcp:Servers:0:Security:Mode"] = "Open"
            })
            .Build();

        var tenantContext = new TenantContextAccessor();
        tenantContext.Set(new TenantContext
        {
            TenantId = "tenant-1",
            UserId = "admin-user-1",
            Roles = ["admin"],
            Permissions = [AgentFlowPermissions.ToolRead]
        });

        var gateway = new CapturingMcpToolGateway();
        var controller = new McpController(configuration, tenantContext, gateway);

        var result = await controller.Invoke(
            "local-test",
            new InvokeMcpRequest { ToolName = "health_check", InputJson = "{}" },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        Assert.NotNull(gateway.Context);
        Assert.Equal("tools.execute,records.read,files.upload", gateway.Context.Metadata["mcp.policy.allow_actions"]);
        Assert.Contains(AgentFlowPermissions.ToolExecuteMedium, gateway.Context.Metadata["permissions"]);
    }

    private sealed class CapturingMcpToolGateway : IMcpToolGateway
    {
        public ToolExecutionContext? Context { get; private set; }

        public Task<ToolResult> ExecuteAsync(
            string serverName,
            string toolName,
            ToolExecutionContext context,
            CancellationToken ct = default)
        {
            Context = context;
            return Task.FromResult(ToolResult.Success("{\"ok\":true}"));
        }
    }
}
