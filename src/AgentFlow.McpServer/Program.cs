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
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceResolvePartyTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceAssertActiveSessionTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceSearchInventoryTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceCreateSaleTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceCreateOrderTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceCreateInvoiceTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceSearchCustomersTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceUpdateCustomerTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceSearchSalesTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceCalculateSaleTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceUpdateInvoiceStatusTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceSendInvoiceWhatsAppTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceSendConversationMessageTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceCloseConversationTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceUpsertInventoryItemTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceAdjustInventoryTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceSearchInventoryMovementsTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceSearchCategoriesTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceCreateCategoryTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceUpdateCategoryTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceDeleteCategoryTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceSearchBranchesTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceCreateBranchTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceUpdateBranchTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceDeleteBranchTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceGetSaleTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceUpdateSaleTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceSearchOrdersTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceGetOrderTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceUpdateOrderTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceGetStoreSettingsTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceUpdateStoreSettingsTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceSearchInvoicesTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceGetInvoiceTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceUpdateInvoiceTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceGetInvoicePdfTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceGetCustomerTool>();
builder.Services.AddScoped<IAgentFlowMcpTool, CommerceDeleteCustomerTool>();
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
