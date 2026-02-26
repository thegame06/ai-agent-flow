using AgentFlow.Abstractions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/mcp")]
[Authorize]
public sealed class McpController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IMcpToolGateway _gateway;

    public McpController(IConfiguration configuration, ITenantContextAccessor tenantContext, IMcpToolGateway gateway)
    {
        _configuration = configuration;
        _tenantContext = tenantContext;
        _gateway = gateway;
    }

    [HttpGet("servers")]
    public IActionResult GetServers()
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var servers = _configuration.GetSection("Mcp:Servers").Get<List<McpServerDto>>() ?? new();
        return Ok(servers.Select(s => new
        {
            s.Name,
            s.Transport,
            s.Url,
            SecurityMode = s.Security?.Mode ?? "Open"
        }));
    }

    [HttpGet("servers/{serverName}/tools")]
    public async Task<IActionResult> GetTools(string serverName, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var servers = _configuration.GetSection("Mcp:Servers").Get<List<McpServerDto>>() ?? new();
        var server = servers.FirstOrDefault(s => string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase));
        if (server is null) return NotFound(new { message = $"MCP server '{serverName}' not configured." });

        if (!string.Equals(server.Transport, "Http", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(server.Url))
            return BadRequest(new { message = "Only Http MCP transport is supported in this endpoint." });

        var toolsUrl = BuildToolsUrl(server.Url);
        using var http = new HttpClient();
        var response = await http.GetAsync(toolsUrl, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, new { message = "MCP tools discovery failed.", body });

        return Content(body, "application/json", Encoding.UTF8);
    }

    [HttpPost("servers/{serverName}/invoke")]
    public async Task<IActionResult> Invoke(string serverName, [FromBody] InvokeMcpRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        if (string.IsNullOrWhiteSpace(request.ToolName))
            return BadRequest(new { message = "toolName is required" });

        var result = await _gateway.ExecuteAsync(
            serverName,
            request.ToolName,
            new ToolExecutionContext
            {
                TenantId = context.TenantId,
                UserId = context.UserId,
                ExecutionId = request.ExecutionId ?? $"mcp-ui-{Guid.NewGuid():N}",
                StepId = request.StepId ?? "mcp-ui-step",
                CorrelationId = request.CorrelationId ?? Guid.NewGuid().ToString("N"),
                InputJson = string.IsNullOrWhiteSpace(request.InputJson) ? "{}" : request.InputJson,
                Metadata = request.Metadata ?? new Dictionary<string, string>()
            },
            ct);

        return Ok(new
        {
            result.IsSuccess,
            result.OutputJson,
            result.ErrorCode,
            result.ErrorMessage,
            result.DurationMs,
            result.TokenUsage
        });
    }

    private static string BuildToolsUrl(string invokeUrl)
    {
        var trimmed = invokeUrl.TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        if (idx <= 0) return trimmed + "/tools";
        return trimmed.Substring(0, idx) + "/tools";
    }

    private sealed class McpServerDto
    {
        public string Name { get; set; } = string.Empty;
        public string Transport { get; set; } = "Http";
        public string? Url { get; set; }
        public McpServerSecurityDto? Security { get; set; }
    }

    private sealed class McpServerSecurityDto
    {
        public string Mode { get; set; } = "Open";
    }
}

public sealed class InvokeMcpRequest
{
    public string ToolName { get; set; } = string.Empty;
    public string? InputJson { get; set; }
    public string? ExecutionId { get; set; }
    public string? StepId { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
