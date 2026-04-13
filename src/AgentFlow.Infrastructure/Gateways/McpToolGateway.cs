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
    private readonly IMcpToolActionCatalog _actionCatalog;
    private readonly ILogger<McpToolGateway> _logger;
    private readonly HttpClient _httpClient;

    public McpToolGateway(
        IConfiguration configuration,
        ITenantMcpSettingsStore tenantMcpSettingsStore,
        IMcpToolActionCatalog actionCatalog,
        ILogger<McpToolGateway> logger,
        HttpClient? httpClient = null)
    {
        _configuration = configuration;
        _tenantMcpSettingsStore = tenantMcpSettingsStore;
        _actionCatalog = actionCatalog;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
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

        var policy = EvaluatePolicy(serverName, toolName, context);
        _logger.LogInformation(
            "MCP_POLICY_AUDIT: tenant={Tenant} server={Server} tool={Tool} action={Action} risk={Risk} requiredPermissions={RequiredPermissions} decision={Decision} version={Version}",
            policy.Tenant,
            policy.Server,
            policy.Tool,
            policy.Action,
            policy.RiskLevel,
            string.Join(",", policy.RequiredPermissions),
            policy.Decision,
            policy.PolicyVersion);

        if (policy.Decision == McpPolicyDecision.Deny)
            return ToolResult.Failure("MCP_POLICY_DENIED", $"Policy denied action '{policy.Action}' for tool '{toolName}'.");

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

    private McpToolPolicyContract EvaluatePolicy(string serverName, string toolName, ToolExecutionContext context)
    {
        var action = ResolveAction(toolName, context.Metadata);
        if (!_actionCatalog.TryResolve(action, out var descriptor))
        {
            return new McpToolPolicyContract
            {
                Tenant = context.TenantId,
                Server = serverName,
                Tool = toolName,
                Action = action,
                RiskLevel = ToolRiskLevel.Critical,
                RequiredPermissions = [],
                Decision = McpPolicyDecision.Deny,
                PolicyVersion = _actionCatalog.Version
            };
        }

        var precedence = ResolvePrecedence(context.Metadata);
        var allowActions = ParseCsvSet(context.Metadata, "mcp.policy.allow_actions");
        var denyActions = ParseCsvSet(context.Metadata, "mcp.policy.deny_actions");
        var principalPermissions = ParseCsvSet(context.Metadata, "permissions");
        var missingPermissions = descriptor.RequiredPermissions
            .Where(p => !principalPermissions.Contains(p))
            .ToArray();

        var allowMatch = allowActions.Contains(action);
        var denyMatch = denyActions.Contains(action);
        var hasPermissionGap = missingPermissions.Length > 0;

        var decision = precedence switch
        {
            McpPolicyPrecedence.AllowOverrides when allowMatch => McpPolicyDecision.Allow,
            McpPolicyPrecedence.AllowOverrides when denyMatch || hasPermissionGap => McpPolicyDecision.Deny,
            McpPolicyPrecedence.AllowOverrides => McpPolicyDecision.Deny, // default deny
            _ when denyMatch || hasPermissionGap => McpPolicyDecision.Deny,
            _ when allowMatch => McpPolicyDecision.Allow,
            _ => McpPolicyDecision.Deny // default deny
        };

        return new McpToolPolicyContract
        {
            Tenant = context.TenantId,
            Server = serverName,
            Tool = toolName,
            Action = descriptor.Action,
            RiskLevel = descriptor.RiskLevel,
            RequiredPermissions = descriptor.RequiredPermissions,
            Decision = decision,
            PolicyVersion = _actionCatalog.Version
        };
    }

    private static string ResolveAction(string toolName, IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("mcp.action", out var action) && !string.IsNullOrWhiteSpace(action))
            return action.Trim();

        return "tools.execute";
    }

    private static McpPolicyPrecedence ResolvePrecedence(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("mcp.policy.precedence", out var mode) &&
            mode.Equals("allow-overrides", StringComparison.OrdinalIgnoreCase))
            return McpPolicyPrecedence.AllowOverrides;

        return McpPolicyPrecedence.DenyOverrides;
    }

    private static HashSet<string> ParseCsvSet(IReadOnlyDictionary<string, string> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
