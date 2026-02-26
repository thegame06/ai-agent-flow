using AgentFlow.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
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
    private readonly HttpClient _httpClient = new();

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

        // 4. MCP Protocol Execution (HTTP transport only in production runtime)
        if (!string.Equals(server.Transport, "Http", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(server.Url))
        {
            return ToolResult.Failure(
                "MCP_TRANSPORT_UNSUPPORTED",
                $"Server {serverName} has unsupported transport '{server.Transport}'. Configure Http transport with Url.");
        }

        try
        {
            var requestPayload = new
            {
                tool = toolName,
                tenantId = context.TenantId,
                userId = context.UserId,
                executionId = context.ExecutionId,
                stepId = context.StepId,
                inputJson = context.InputJson,
                metadata = context.Metadata
            };

            var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
            };

            // Optional auth header from env/secret reference
            if (!string.IsNullOrWhiteSpace(server.Security.AuthSecretName))
            {
                var token = Environment.GetEnvironmentVariable(server.Security.AuthSecretName);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }

            var startedAt = DateTime.UtcNow;
            var response = await _httpClient.SendAsync(request, ct);
            var durationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MCP HTTP call failed for {ServerName}.{ToolName}: {StatusCode} {Body}",
                    serverName, toolName, response.StatusCode, responseBody);
                return ToolResult.Failure("MCP_HTTP_ERROR", $"{(int)response.StatusCode}: {responseBody}", durationMs);
            }

            return ToolResult.Success(responseBody, durationMs);
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
