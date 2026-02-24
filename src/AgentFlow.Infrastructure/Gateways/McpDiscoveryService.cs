using AgentFlow.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

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
                
                // Simulation: In a real implementation, we would call gateway.ListToolsAsync(server.Name)
                var discoveredTools = new List<(string Name, string Description, string Schema)>
                {
                    ( $"{server.Name}_GetStatus", $"Get status for {server.Name}", "{}" ),
                    ( $"{server.Name}_ExecuteQuery", $"Execute domain query on {server.Name}", "{\"query\": \"string\"}" )
                };

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
}
