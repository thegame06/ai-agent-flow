using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/settings")]
[Authorize]
public sealed class TenantSettingsController : ControllerBase
{
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IMongoCollection<TenantSettingsDocument> _collection;

    public TenantSettingsController(ITenantContextAccessor tenantContext, IMongoDatabase database)
    {
        _tenantContext = tenantContext;
        _collection = database.GetCollection<TenantSettingsDocument>("tenant_settings");
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromRoute] string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var doc = await _collection.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync(ct)
                  ?? TenantSettingsDocument.Default(tenantId, context.UserId);

        return Ok(ToDto(doc));
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromRoute] string tenantId, [FromBody] SaveTenantSettingsRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var now = DateTimeOffset.UtcNow;
        var doc = new TenantSettingsDocument
        {
            Id = tenantId,
            TenantId = tenantId,
            TenantName = request.TenantName,
            DefaultApiVersion = request.DefaultApiVersion,
            EnforceRbac = request.EnforceRbac,
            PromptInjectionGuard = request.PromptInjectionGuard,
            SandboxDangerousTools = request.SandboxDangerousTools,
            AuditLogging = request.AuditLogging,
            MaxStepsPerExecution = request.MaxStepsPerExecution,
            TimeoutPerStepSeconds = request.TimeoutPerStepSeconds,
            MaxTokensPerExecution = request.MaxTokensPerExecution,
            MaxConcurrentExecutions = request.MaxConcurrentExecutions,
            OtlpExport = request.OtlpExport,
            OtlpEndpoint = request.OtlpEndpoint,
            ExecutionReplay = request.ExecutionReplay,
            LlmDecisionLogging = request.LlmDecisionLogging,
            UpdatedAt = now,
            UpdatedBy = context.UserId
        };

        await _collection.ReplaceOneAsync(x => x.TenantId == tenantId, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return Ok(ToDto(doc));
    }

    private static object ToDto(TenantSettingsDocument d) => new
    {
        d.TenantName,
        d.DefaultApiVersion,
        d.EnforceRbac,
        d.PromptInjectionGuard,
        d.SandboxDangerousTools,
        d.AuditLogging,
        d.MaxStepsPerExecution,
        d.TimeoutPerStepSeconds,
        d.MaxTokensPerExecution,
        d.MaxConcurrentExecutions,
        d.OtlpExport,
        d.OtlpEndpoint,
        d.ExecutionReplay,
        d.LlmDecisionLogging,
        d.UpdatedAt,
        d.UpdatedBy
    };

    private sealed class TenantSettingsDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;

        public string TenantName { get; set; } = "Tenant";
        public string DefaultApiVersion { get; set; } = "v1";
        public bool EnforceRbac { get; set; } = true;
        public bool PromptInjectionGuard { get; set; } = true;
        public bool SandboxDangerousTools { get; set; } = true;
        public bool AuditLogging { get; set; } = true;

        public int MaxStepsPerExecution { get; set; } = 25;
        public int TimeoutPerStepSeconds { get; set; } = 30;
        public int MaxTokensPerExecution { get; set; } = 100000;
        public int MaxConcurrentExecutions { get; set; } = 10;

        public bool OtlpExport { get; set; } = true;
        public string OtlpEndpoint { get; set; } = "http://localhost:4317";
        public bool ExecutionReplay { get; set; } = true;
        public bool LlmDecisionLogging { get; set; } = true;

        public DateTimeOffset? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }

        public static TenantSettingsDocument Default(string tenantId, string userId) => new()
        {
            Id = tenantId,
            TenantId = tenantId,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = userId
        };
    }
}

public sealed class SaveTenantSettingsRequest
{
    public string TenantName { get; set; } = "Tenant";
    public string DefaultApiVersion { get; set; } = "v1";
    public bool EnforceRbac { get; set; } = true;
    public bool PromptInjectionGuard { get; set; } = true;
    public bool SandboxDangerousTools { get; set; } = true;
    public bool AuditLogging { get; set; } = true;

    public int MaxStepsPerExecution { get; set; } = 25;
    public int TimeoutPerStepSeconds { get; set; } = 30;
    public int MaxTokensPerExecution { get; set; } = 100000;
    public int MaxConcurrentExecutions { get; set; } = 10;

    public bool OtlpExport { get; set; } = true;
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
    public bool ExecutionReplay { get; set; } = true;
    public bool LlmDecisionLogging { get; set; } = true;
}
