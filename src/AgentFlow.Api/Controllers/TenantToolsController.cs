using AgentFlow.Extensions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/tools")]
[Authorize]
public sealed class TenantToolsController : ControllerBase
{
    private readonly IExtensionRegistry _registry;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IMongoCollection<ToolToggleDocument> _collection;

    public TenantToolsController(IExtensionRegistry registry, ITenantContextAccessor tenantContext, IMongoDatabase database)
    {
        _registry = registry;
        _tenantContext = tenantContext;
        _collection = database.GetCollection<ToolToggleDocument>("tenant_tool_toggles");
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var toggles = await _collection.Find(x => x.TenantId == tenantId).ToListAsync(ct);
        var toggleMap = toggles.ToDictionary(x => x.ToolName, x => x.Enabled, StringComparer.OrdinalIgnoreCase);

        var tools = _registry.GetTools();
        var rows = new List<object>();

        foreach (var t in tools)
        {
            var health = await t.CheckHealthAsync(ct);
            rows.Add(new
            {
                t.Name,
                t.Version,
                t.Description,
                RiskLevel = t.RiskLevel.ToString(),
                Enabled = toggleMap.TryGetValue(t.Name, out var enabled) ? enabled : true,
                Health = health.IsHealthy ? "Healthy" : "Unhealthy",
                health.Message,
                health.CheckedAt,
                t.InputSchemaJson,
                t.OutputSchemaJson
            });
        }

        return Ok(rows);
    }

    [HttpPut("{toolName}/enabled")]
    public async Task<IActionResult> SetEnabled([FromRoute] string tenantId, [FromRoute] string toolName, [FromBody] SetToolEnabledRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var exists = _registry.GetTool(toolName) != null;
        if (!exists) return NotFound(new { message = $"Tool '{toolName}' not found" });

        var doc = new ToolToggleDocument
        {
            Id = $"{tenantId}:{toolName}",
            TenantId = tenantId,
            ToolName = toolName,
            Enabled = request.Enabled,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = context.UserId
        };

        await _collection.ReplaceOneAsync(
            x => x.TenantId == tenantId && x.ToolName == toolName,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return Ok(new { toolName, request.Enabled, doc.UpdatedAt, doc.UpdatedBy });
    }

    private sealed class ToolToggleDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }
}

public sealed class SetToolEnabledRequest
{
    public bool Enabled { get; set; }
}
