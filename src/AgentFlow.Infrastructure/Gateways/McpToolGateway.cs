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
    private readonly ITenantMcpSettingsStore _tenantMcpSettingsStore;
    private readonly ILogger<McpToolGateway> _logger;
    private readonly HttpClient _httpClient = new();

    public McpToolGateway(
        IConfiguration configuration,
        ITenantMcpSettingsStore tenantMcpSettingsStore,
        ILogger<McpToolGateway> logger)
    {
        _configuration = configuration;
        _tenantMcpSettingsStore = tenantMcpSettingsStore;
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

        var tenantMcp = await _tenantMcpSettingsStore.GetAsync(context.TenantId, ct);
        if (!tenantMcp.Enabled)
            return ToolResult.Failure("MCP_DISABLED", "MCP is disabled for this tenant. Enable it first.");

        if (!string.Equals(tenantMcp.Runtime, "MicrosoftAgentFramework", StringComparison.OrdinalIgnoreCase))
            return ToolResult.Failure("MCP_RUNTIME_UNSUPPORTED", "MCP runtime must be MicrosoftAgentFramework.");

        if (tenantMcp.AllowedServers.Count > 0 &&
            !tenantMcp.AllowedServers.Any(x => string.Equals(x, serverName, StringComparison.OrdinalIgnoreCase)))
            return ToolResult.Failure("MCP_SERVER_NOT_ALLOWED", $"Server {serverName} is not enabled for this tenant.");

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

        if (server.Security.EnableAuditLogs)
            _logger.LogInformation("MCP_AUDIT: Executing remote call. Request: {PayloadLength} bytes", context.InputJson?.Length ?? 0);

        if (!string.Equals(server.Transport, "Http", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(server.Url))
        {
            return ToolResult.Failure(
                "MCP_TRANSPORT_UNSUPPORTED",
                $"Server {serverName} has unsupported transport '{server.Transport}'. Configure Http transport with Url.");
        }

        var attempts = Math.Max(1, tenantMcp.RetryCount + 1);
        var timeoutSeconds = Math.Clamp(tenantMcp.TimeoutSeconds, 5, 120);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

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

                using var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(server.Security.AuthSecretName))
                {
                    var token = Environment.GetEnvironmentVariable(server.Security.AuthSecretName);
                    if (!string.IsNullOrWhiteSpace(token))
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                var startedAt = DateTime.UtcNow;
                var response = await _httpClient.SendAsync(request, timeoutCts.Token);
                var durationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var isTransient = (int)response.StatusCode >= 500;
                    _logger.LogWarning("MCP HTTP call failed (attempt {Attempt}/{Attempts}) for {ServerName}.{ToolName}: {StatusCode} {Body}",
                        attempt, attempts, serverName, toolName, response.StatusCode, responseBody);

                    if (!isTransient || attempt == attempts)
                        return ToolResult.Failure("MCP_HTTP_ERROR", $"{(int)response.StatusCode}: {responseBody}", durationMs);

                    continue;
                }

                return ToolResult.Success(responseBody, durationMs);
            }
            catch (OperationCanceledException ex)
            {
                lastException = ex;
                if (attempt == attempts)
                    return ToolResult.Failure("TIMEOUT", "Remote MCP tool execution timed out.");
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Transient MCP network error (attempt {Attempt}/{Attempts})", attempt, attempts);
                if (attempt == attempts)
                    return ToolResult.Failure("MCP_NETWORK_ERROR", $"Remote MCP connectivity failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ERROR: MCP Tool execution failed for {ServerName}.{ToolName}", serverName, toolName);
                return ToolResult.Failure("MCP_ERROR", $"Remote execution failed: {ex.Message}");
            }
        }

        return ToolResult.Failure("MCP_ERROR", $"Remote execution failed: {lastException?.Message ?? "unknown error"}");
    }
}
