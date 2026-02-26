using AgentFlow.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace AgentFlow.Infrastructure.Gateways;

/// <summary>
/// Background service that discovers tools from configured MCP servers 
/// and registers them in the global Tool Registry.
/// </summary>
public sealed class McpDiscoveryService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<McpDiscoveryService> _logger;

    public McpDiscoveryService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<McpDiscoveryService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MCP Discovery Service starting...");

        // Wait for the app to be fully started
        try 
        {
            await Task.Delay(1000, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var servers = _configuration.GetSection("Mcp:Servers").Get<List<McpServerConfig>>() ?? new List<McpServerConfig>();
        if (servers.Count() == 0)
        {
            _logger.LogWarning("No MCP servers configured for discovery.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IToolRegistry>();
        var gateway = scope.ServiceProvider.GetRequiredService<IMcpToolGateway>();

        foreach (var server in servers)
        {
            if (stoppingToken.IsCancellationRequested) break;
            
            try
            {
                _logger.LogInformation("Discovering tools from MCP server: {ServerName} ({Transport}, Security: {SecurityMode})", 
                    server.Name, server.Transport, server.Security.Mode);
                
                var discoveredTools = await DiscoverToolsAsync(server, stoppingToken);

                if (discoveredTools.Count == 0)
                {
                    _logger.LogWarning("No MCP tools discovered from {ServerName}. Skipping registration.", server.Name);
                    continue;
                }

                foreach (var toolInfo in discoveredTools)
                {
                    var proxy = new McpToolPlugin(
                        gateway, 
                        server.Name, 
                        toolInfo.Name, 
                        toolInfo.Description, 
                        toolInfo.Schema,
                        server.Security.DefaultRiskLevel);
                    
                    registry.Register(proxy);
                    _logger.LogDebug("Registered MCP Proxy Tool: {ToolName} [Security Policy: {Policy}]", 
                        proxy.Name, server.Security.Mode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover tools from MCP server {ServerName}", server.Name);
            }
        }
    }

    private async Task<List<(string Name, string Description, string Schema)>> DiscoverToolsAsync(
        McpServerConfig server,
        CancellationToken ct)
    {
        if (!string.Equals(server.Transport, "Http", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(server.Url))
            return new();

        using var http = new HttpClient();
        var discoveryUrl = server.Url.TrimEnd('/') + "/tools";

        var response = await http.GetAsync(discoveryUrl, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("MCP discovery call failed for {ServerName}: {StatusCode}", server.Name, response.StatusCode);
            return new();
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var tools = JsonSerializer.Deserialize<List<McpDiscoveredTool>>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new();

        return tools
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => (
                t.Name!,
                string.IsNullOrWhiteSpace(t.Description) ? t.Name! : t.Description!,
                string.IsNullOrWhiteSpace(t.InputSchemaJson) ? "{}" : t.InputSchemaJson!))
            .ToList();
    }

    private sealed class McpDiscoveredTool
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? InputSchemaJson { get; set; }
    }
}
