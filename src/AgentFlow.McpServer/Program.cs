using AgentFlow.McpServer;
using AgentFlow.McpServer.Auth;
using AgentFlow.McpServer.Tools;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure (shared with AgentFlow.Api) ───────────────────────────────
builder.Services.AddAgentFlowMcpInfrastructure(builder.Configuration);

// ── MCP tool handlers ─────────────────────────────────────────────────────────
builder.Services.AddScoped<IAgentFlowMcpTool, ListWorkflowsTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, TriggerWorkflowTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, GetSessionContextTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, ListAgentsTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, GetAgentTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, DiagnoseWorkflowTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, DiagnoseChannelTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, ScaffoldWorkflowTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, ListIntegrationsTool>();
builder.Services.AddScoped<ToolDispatcher>();

// ── Auth ──────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<McpApiKeyValidator>();

var app = builder.Build();

app.UseMiddleware<McpApiKeyMiddleware>();

// ═════════════════════════════════════════════════════════════════════════════
// GET /tools  — Tool catalog discovery (agents call this at startup)
// ═════════════════════════════════════════════════════════════════════════════
app.MapGet("/tools", (ToolDispatcher dispatcher) =>
    Results.Ok(dispatcher.GetCatalog()));

// ═════════════════════════════════════════════════════════════════════════════
// POST /invoke  — Execute a specific tool
// Body: McpInvokeRequest { tool, tenantId, executionId, inputJson, metadata }
// ═════════════════════════════════════════════════════════════════════════════
app.MapPost("/invoke", async (McpInvokeRequest req, ToolDispatcher dispatcher, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Tool))
        return Results.BadRequest(new { error = "tool is required" });

    if (string.IsNullOrWhiteSpace(req.TenantId))
        return Results.BadRequest(new { error = "tenantId is required" });

    var result = await dispatcher.InvokeAsync(req, ct);

    return result.Ok
        ? Results.Ok(result)
        : Results.UnprocessableEntity(new { error = result.Error, tool = req.Tool });
});

// ── Health ────────────────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    service = "agentflow-mcp-server",
    version = "1.0.0",
    timestamp = DateTimeOffset.UtcNow
}));

app.Run();
