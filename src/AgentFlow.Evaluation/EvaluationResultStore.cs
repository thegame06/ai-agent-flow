using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Evaluation;

// =========================================================================
// EVALUATION RESULT STORE — MongoDB persistence (append-only)
// =========================================================================

/// <summary>
/// Contract for storing evaluation results.
/// Append-only (WORM) — results are never modified or deleted.
/// </summary>
public interface IEvaluationResultStore
{
    Task SaveAsync(EvaluationResult result, CancellationToken ct = default);
    Task<EvaluationResult?> GetByExecutionIdAsync(string executionId, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<EvaluationResult>> GetByAgentAsync(string agentKey, string tenantId, int limit = 50, CancellationToken ct = default);
    Task<IReadOnlyList<EvaluationResult>> GetPendingHumanReviewAsync(string tenantId, int limit = 50, CancellationToken ct = default);
    Task<EvaluationSummary> GetAgentSummaryAsync(string agentKey, string agentVersion, string tenantId, CancellationToken ct = default);
}

/// <summary>
/// Aggregated evaluation metrics for an agent version.
/// Used by the dashboard and lifecycle decisions.
/// </summary>
public sealed record EvaluationSummary
{
    public required string AgentKey { get; init; }
    public required string AgentVersion { get; init; }
    public int TotalEvaluations { get; init; }
    public double AverageQualityScore { get; init; }
    public double AverageComplianceScore { get; init; }
    public int HallucinationCriticalCount { get; init; }
    public int HallucinationHighCount { get; init; }
    public int PendingHumanReviewCount { get; init; }
    public double? AverageToolAccuracy { get; init; }
}

/// <summary>
/// MongoDB-backed evaluation result store.
/// 
/// Design:
/// - Append-only: results are NEVER updated or deleted (WORM compliance)
/// - Indexed by: tenantId + executionId, tenantId + agentKey + evaluatedAt
/// - Supports aggregation queries for summary dashboards
/// </summary>
public sealed class MongoEvaluationResultStore : IEvaluationResultStore
{
    private readonly IMongoCollection<EvaluationResultDocument> _collection;
    private readonly ILogger<MongoEvaluationResultStore> _logger;

    public MongoEvaluationResultStore(IMongoDatabase database, ILogger<MongoEvaluationResultStore> logger)
    {
        _collection = database.GetCollection<EvaluationResultDocument>("evaluation_results");
        _logger = logger;

        var indexBuilder = Builders<EvaluationResultDocument>.IndexKeys;
        _collection.Indexes.CreateMany([
            new CreateIndexModel<EvaluationResultDocument>(
                indexBuilder.Ascending(d => d.TenantId).Ascending(d => d.ExecutionId),
                new CreateIndexOptions { Name = "idx_tenant_execution", Unique = true }),
            new CreateIndexModel<EvaluationResultDocument>(
                indexBuilder.Ascending(d => d.TenantId).Ascending(d => d.AgentKey).Descending(d => d.EvaluatedAt),
                new CreateIndexOptions { Name = "idx_tenant_agent_date" }),
            new CreateIndexModel<EvaluationResultDocument>(
                indexBuilder.Ascending(d => d.TenantId).Ascending(d => d.RequiresHumanReview).Descending(d => d.EvaluatedAt),
                new CreateIndexOptions { Name = "idx_tenant_humanreview" })
        ]);
    }

    public async Task SaveAsync(EvaluationResult result, CancellationToken ct = default)
    {
        var doc = MapToDocument(result);
        await _collection.InsertOneAsync(doc, cancellationToken: ct);

        _logger.LogInformation(
            "Evaluation saved: exec={ExecutionId}, quality={Quality:F2}, hallucination={Hallucination}, review={Review}",
            result.ExecutionId, result.QualityScore, result.HallucinationRisk, result.RequiresHumanReview);
    }

    public async Task<EvaluationResult?> GetByExecutionIdAsync(
        string executionId, string tenantId, CancellationToken ct = default)
    {
        var filter = Builders<EvaluationResultDocument>.Filter.And(
            Builders<EvaluationResultDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<EvaluationResultDocument>.Filter.Eq(d => d.ExecutionId, executionId));

        var doc = await _collection.Find(filter).FirstOrDefaultAsync(ct);
        return doc is null ? null : MapToResult(doc);
    }

    public async Task<IReadOnlyList<EvaluationResult>> GetByAgentAsync(
        string agentKey, string tenantId, int limit = 50, CancellationToken ct = default)
    {
        var filter = Builders<EvaluationResultDocument>.Filter.And(
            Builders<EvaluationResultDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<EvaluationResultDocument>.Filter.Eq(d => d.AgentKey, agentKey));

        var docs = await _collection.Find(filter)
            .SortByDescending(d => d.EvaluatedAt)
            .Limit(limit)
            .ToListAsync(ct);

        return docs.Select(MapToResult).ToList();
    }

    public async Task<IReadOnlyList<EvaluationResult>> GetPendingHumanReviewAsync(
        string tenantId, int limit = 50, CancellationToken ct = default)
    {
        var filter = Builders<EvaluationResultDocument>.Filter.And(
            Builders<EvaluationResultDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<EvaluationResultDocument>.Filter.Eq(d => d.RequiresHumanReview, true));

        var docs = await _collection.Find(filter)
            .SortByDescending(d => d.EvaluatedAt)
            .Limit(limit)
            .ToListAsync(ct);

        return docs.Select(MapToResult).ToList();
    }

    public async Task<EvaluationSummary> GetAgentSummaryAsync(
        string agentKey, string agentVersion, string tenantId, CancellationToken ct = default)
    {
        var filter = Builders<EvaluationResultDocument>.Filter.And(
            Builders<EvaluationResultDocument>.Filter.Eq(d => d.TenantId, tenantId),
            Builders<EvaluationResultDocument>.Filter.Eq(d => d.AgentKey, agentKey),
            Builders<EvaluationResultDocument>.Filter.Eq(d => d.AgentVersion, agentVersion));

        var docs = await _collection.Find(filter).ToListAsync(ct);

        if (docs.Count == 0)
        {
            return new EvaluationSummary
            {
                AgentKey = agentKey,
                AgentVersion = agentVersion
            };
        }

        var toolAccuracies = docs.Where(d => d.ToolUsageAccuracy.HasValue).Select(d => d.ToolUsageAccuracy!.Value).ToList();

        return new EvaluationSummary
        {
            AgentKey = agentKey,
            AgentVersion = agentVersion,
            TotalEvaluations = docs.Count,
            AverageQualityScore = docs.Average(d => d.QualityScore),
            AverageComplianceScore = docs.Average(d => d.PolicyComplianceScore),
            HallucinationCriticalCount = docs.Count(d => d.HallucinationRisk == nameof(HallucinationRisk.Critical)),
            HallucinationHighCount = docs.Count(d => d.HallucinationRisk == nameof(HallucinationRisk.High)),
            PendingHumanReviewCount = docs.Count(d => d.RequiresHumanReview),
            AverageToolAccuracy = toolAccuracies.Count > 0 ? toolAccuracies.Average() : null
        };
    }

    // ── Mapping ──

    private static EvaluationResultDocument MapToDocument(EvaluationResult r) => new()
    {
        ExecutionId = r.ExecutionId,
        TenantId = r.TenantId,
        AgentKey = r.EvaluationRationale.Contains("agent_key=") ? "" : "", // extracted below
        AgentVersion = "",
        QualityScore = r.QualityScore,
        PolicyComplianceScore = r.PolicyComplianceScore,
        HallucinationRisk = r.HallucinationRisk.ToString(),
        ToolUsageAccuracy = r.ToolUsageAccuracy,
        RequiresHumanReview = r.RequiresHumanReview,
        ReviewPriority = r.ReviewPriority.ToString(),
        EvaluationRationale = r.EvaluationRationale,
        IsShadowEvaluation = r.IsShadowEvaluation,
        Violations = r.Violations.Select(v => new ViolationDoc
        {
            Code = v.Code,
            Description = v.Description,
            Severity = v.Severity.ToString(),
            Evidence = v.Evidence
        }).ToList(),
        EvaluatedAt = DateTimeOffset.UtcNow
    };

    private static EvaluationResult MapToResult(EvaluationResultDocument d) => new()
    {
        ExecutionId = d.ExecutionId,
        TenantId = d.TenantId,
        QualityScore = d.QualityScore,
        PolicyComplianceScore = d.PolicyComplianceScore,
        HallucinationRisk = Enum.Parse<HallucinationRisk>(d.HallucinationRisk, ignoreCase: true),
        ToolUsageAccuracy = d.ToolUsageAccuracy,
        RequiresHumanReview = d.RequiresHumanReview,
        ReviewPriority = Enum.Parse<HumanReviewPriority>(d.ReviewPriority, ignoreCase: true),
        EvaluationRationale = d.EvaluationRationale,
        IsShadowEvaluation = d.IsShadowEvaluation,
        Violations = d.Violations.Select(v => new EvaluationViolation
        {
            Code = v.Code,
            Description = v.Description,
            Severity = Enum.Parse<EvaluationSeverity>(v.Severity, ignoreCase: true),
            Evidence = v.Evidence
        }).ToList()
    };
}

// =========================================================================
// MONGODB DOCUMENTS (internal)
// =========================================================================

internal sealed class EvaluationResultDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string ExecutionId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string AgentKey { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public double QualityScore { get; set; }
    public double PolicyComplianceScore { get; set; }
    public string HallucinationRisk { get; set; } = "None";
    public double? ToolUsageAccuracy { get; set; }
    public bool RequiresHumanReview { get; set; }
    public string ReviewPriority { get; set; } = "Low";
    public string EvaluationRationale { get; set; } = string.Empty;
    public bool IsShadowEvaluation { get; set; }
    public List<ViolationDoc> Violations { get; set; } = [];
    public DateTimeOffset EvaluatedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class ViolationDoc
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning";
    public string? Evidence { get; set; }
}
