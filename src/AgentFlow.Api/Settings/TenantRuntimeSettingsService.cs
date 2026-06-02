using AgentFlow.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Settings;

public interface ITenantRuntimeSettingsService : ITenantRuntimeSettingsReader
{
    Task<TenantRuntimeSettings> SaveAsync(string tenantId, TenantRuntimeSettings settings, string userId, CancellationToken ct = default);
}

public sealed class TenantRuntimeSettingsService : ITenantRuntimeSettingsService
{
    private readonly IMongoCollection<TenantSettingsDocument> _collection;

    public TenantRuntimeSettingsService(IMongoDatabase database)
    {
        _collection = database.GetCollection<TenantSettingsDocument>("tenant_settings");
    }

    public async Task<TenantRuntimeSettings> GetAsync(string tenantId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync(ct)
            ?? TenantSettingsDocument.Default(tenantId, "system");
        return ToModel(doc);
    }

    public async Task<TenantRuntimeSettings> SaveAsync(string tenantId, TenantRuntimeSettings settings, string userId, CancellationToken ct = default)
    {
        var current = await _collection.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var doc = new TenantSettingsDocument
        {
            Id = tenantId,
            TenantId = tenantId,
            TenantName = settings.TenantName,
            DefaultApiVersion = settings.DefaultApiVersion,
            EnforceRbac = settings.EnforceRbac,
            PromptInjectionGuard = settings.PromptInjectionGuard,
            SandboxDangerousTools = settings.SandboxDangerousTools,
            AuditLogging = settings.AuditLogging,
            MaxStepsPerExecution = settings.MaxStepsPerExecution,
            TimeoutPerStepSeconds = settings.TimeoutPerStepSeconds,
            MaxTokensPerExecution = settings.MaxTokensPerExecution,
            MaxConcurrentExecutions = settings.MaxConcurrentExecutions,
            OtlpExport = settings.OtlpExport,
            OtlpEndpoint = settings.OtlpEndpoint,
            ExecutionReplay = settings.ExecutionReplay,
            LlmDecisionLogging = settings.LlmDecisionLogging,
            UpdatedAt = now,
            UpdatedBy = userId
        };

        await _collection.ReplaceOneAsync(x => x.TenantId == tenantId, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return ToModel(doc);
    }

    private static TenantRuntimeSettings ToModel(TenantSettingsDocument doc) => new()
    {
        TenantName = doc.TenantName,
        DefaultApiVersion = doc.DefaultApiVersion,
        EnforceRbac = doc.EnforceRbac,
        PromptInjectionGuard = doc.PromptInjectionGuard,
        SandboxDangerousTools = doc.SandboxDangerousTools,
        AuditLogging = doc.AuditLogging,
        MaxStepsPerExecution = doc.MaxStepsPerExecution,
        TimeoutPerStepSeconds = doc.TimeoutPerStepSeconds,
        MaxTokensPerExecution = doc.MaxTokensPerExecution,
        MaxConcurrentExecutions = doc.MaxConcurrentExecutions,
        OtlpExport = doc.OtlpExport,
        OtlpEndpoint = doc.OtlpEndpoint,
        ExecutionReplay = doc.ExecutionReplay,
        LlmDecisionLogging = doc.LlmDecisionLogging
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
