using AgentFlow.Infrastructure.Gateways;

namespace AgentFlow.Tests.Unit.Infrastructure;

public sealed class McpDiscoveryServiceTests
{
    [Theory]
    // Server.Url is the invoke endpoint; the catalog is the sibling /tools path.
    [InlineData("http://localhost:3501/invoke", "http://localhost:3501/tools")]
    [InlineData("http://localhost:3502/invoke/", "http://localhost:3502/tools")]
    [InlineData("https://mcp.example.com/api/mcp/invoke", "https://mcp.example.com/api/mcp/tools")]
    // Host-only URL: append instead of chopping the authority.
    [InlineData("http://localhost:3501", "http://localhost:3501/tools")]
    [InlineData("http://localhost:3501/", "http://localhost:3501/tools")]
    public void BuildToolsUrl_DerivesCatalogFromInvokeUrl(string invokeUrl, string expected)
    {
        Assert.Equal(expected, McpDiscoveryService.BuildToolsUrl(invokeUrl));
    }
}
