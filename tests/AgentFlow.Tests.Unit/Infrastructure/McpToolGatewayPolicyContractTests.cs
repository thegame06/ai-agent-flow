using AgentFlow.Abstractions;
using AgentFlow.Infrastructure.Gateways;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http;

namespace AgentFlow.Tests.Unit.Infrastructure;

public sealed class McpToolGatewayPolicyContractTests
{
    [Fact]
    public async Task ExecuteAsync_Allows_WhenActionIsAllowedAndPermissionsPresent()
    {
        var gateway = CreateGateway();
        var context = BuildContext(new Dictionary<string, string>
        {
            ["mcp.action"] = "records.read",
            ["mcp.policy.allow_actions"] = "records.read",
            ["permissions"] = "tool:read,tool:execute:low"
        });

        var result = await gateway.ExecuteAsync("crm", "search-records", context, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAsync_Denies_WhenMissingRequiredPermissions()
    {
        var gateway = CreateGateway();
        var context = BuildContext(new Dictionary<string, string>
        {
            ["mcp.action"] = "files.upload",
            ["mcp.policy.allow_actions"] = "files.upload",
            ["permissions"] = "tool:create"
        });

        var result = await gateway.ExecuteAsync("crm", "upload-file", context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("MCP_POLICY_DENIED", result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_DenyOverrides_WhenAllowAndDenyConflict()
    {
        var gateway = CreateGateway();
        var context = BuildContext(new Dictionary<string, string>
        {
            ["mcp.action"] = "records.read",
            ["mcp.policy.allow_actions"] = "records.read",
            ["mcp.policy.deny_actions"] = "records.read",
            ["permissions"] = "tool:read,tool:execute:low"
        });

        var result = await gateway.ExecuteAsync("crm", "search-records", context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("MCP_POLICY_DENIED", result.ErrorCode);
    }

    private static McpToolGateway CreateGateway()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Servers:0:Name"] = "crm",
                ["Mcp:Servers:0:Transport"] = "Http",
                ["Mcp:Servers:0:Url"] = "http://127.0.0.1:9/mcp/invoke",
                ["Mcp:Servers:0:Security:Mode"] = "Open",
                ["Mcp:Servers:0:Security:EnableAuditLogs"] = "true"
            })
            .Build();

        return new McpToolGateway(
            configuration,
            new InMemoryTenantMcpSettingsStore(),
            new McpToolActionCatalog(),
            NullLogger<McpToolGateway>.Instance,
            new HttpClient(new StubHttpHandler()));
    }

    private static ToolExecutionContext BuildContext(IReadOnlyDictionary<string, string> metadata)
        => new()
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            ExecutionId = "exec-1",
            StepId = "step-1",
            CorrelationId = "corr-1",
            InputJson = "{}",
            Metadata = metadata
        };

    private sealed class InMemoryTenantMcpSettingsStore : ITenantMcpSettingsStore
    {
        public Task<TenantMcpSettings> GetAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(new TenantMcpSettings
            {
                TenantId = tenantId,
                Enabled = true,
                Runtime = "MicrosoftAgentFramework",
                AllowedServers = []
            });

        public Task<TenantMcpSettings> SaveAsync(TenantMcpSettings settings, CancellationToken ct = default)
            => Task.FromResult(settings);
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            });
    }
}
