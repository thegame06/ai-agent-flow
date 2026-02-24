using AgentFlow.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgentFlow.Infrastructure.Gateways;

/// <summary>
/// Implementation of the MCP Gateway for interacting with remote tool servers.
/// Includes multi-tenancy validation, audit logging, and security enforcement.
/// </summary>
public sealed class McpToolGateway : IMcpToolGateway
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<McpToolGateway> _logger;

    public McpToolGateway(IConfiguration configuration, ILogger<McpToolGateway> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(
        string serverName, 
        string toolName, 
        ToolExecutionContext context, 
        CancellationToken ct = default)
    {
        _logger.LogInformation("MCP [EXEC]: Tenant {TenantId} calling {ToolName} on {ServerName}", 
            context.TenantId, toolName, serverName);

        // --- SECURITY PARAMETERS ADJUSTMENT ---
        
        // 1. Resolve Server config
        var servers = _configuration.GetSection("Mcp:Servers").Get<List<McpServerConfig>>() ?? new List<McpServerConfig>();
        var server = servers.FirstOrDefault(s => s.Name == serverName);

        if (server == null)
        {
            _logger.LogWarning("SECURITY ALERT: MCP Server {ServerName} not found in configuration.", serverName);
            return ToolResult.Failure("MCP_SECURITY", $"Server {serverName} is not a trusted MCP endpoint.");
        }

        // 2. Multi-tenancy Isolation Check
        if (server.Security.Mode == "Restricted" || server.Security.Mode == "Enterprise")
        {
            if (!server.Security.AllowedTenants.Contains(context.TenantId))
            {
                _logger.LogWarning("SECURITY DENIED: Tenant {TenantId} is not authorized for MCP Server {ServerName}", 
                    context.TenantId, serverName);
                return ToolResult.Failure("MCP_FORBIDDEN", $"Tenant {context.TenantId} does not have cross-boundary access to {serverName}.");
            }
        }

        // 3. Governance: Audit Log Persistence (Simulation)
        if (server.Security.EnableAuditLogs)
        {
            _logger.LogInformation("MCP_AUDIT: Executing remote call. Request: {PayloadLength} bytes", context.InputJson?.Length ?? 0);
        }

        // 4. MCP Protocol Execution (Placeholder for actual SSE/Stdio/Http transport)
        try
        {
            // Simulation of remote tool execution
            await Task.Delay(200, ct); 

            // Guru Tip: Always validate remote output schema before returning to the Brain
            var simulatedOutput = new { 
                status = "success", 
                server = serverName, 
                processedAt = DateTime.UtcNow,
                data = "Result from remote MCP server"
            };

            return ToolResult.Success(JsonSerializer.Serialize(simulatedOutput), 150);
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Failure("TIMEOUT", "Remote MCP tool execution timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR: MCP Tool execution failed for {ServerName}.{ToolName}", serverName, toolName);
            return ToolResult.Failure("MCP_ERROR", $"Remote execution failed: {ex.Message}");
        }
    }
}
