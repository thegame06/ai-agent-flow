using AgentFlow.Abstractions;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// ToolDefinition - Registry entry for a tool.
/// Tools are tenant-scoped OR platform-scoped (TenantId = "platform").
/// Versioned for non-breaking upgrades.
/// </summary>
public sealed class ToolDefinition : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public new string Version { get; private set; } = "1.0.0";
    public ToolScope Scope { get; private set; } = ToolScope.Tenant;
    public ToolStatus Status { get; private set; } = ToolStatus.Active;

    // --- Schema ---
    public string InputSchemaJson { get; private set; } = "{}"; // JSON Schema
    public string OutputSchemaJson { get; private set; } = "{}";

    // --- Authorization ---
    public ToolRiskLevel RiskLevel { get; private set; } = ToolRiskLevel.Low;
    public bool RequiresSandbox { get; private set; } = false;
    public bool RequiresExplicitApproval { get; private set; } = false;
    public IReadOnlyList<string> RequiredPermissions { get; private set; } = [];

    // --- Metadata ---
    public string Category { get; private set; } = string.Empty;
    public string ImplementationAssembly { get; private set; } = string.Empty;
    public string ImplementationType { get; private set; } = string.Empty;

    private ToolDefinition() { }

    public static Result<ToolDefinition> Create(
        string tenantId,
        string name,
        string description,
        string version,
        ToolScope scope,
        ToolRiskLevel riskLevel,
        string inputSchemaJson,
        string outputSchemaJson,
        string implementationType,
        string createdBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<ToolDefinition>.Failure(Error.Validation(nameof(name), "Tool name is required."));

        if (riskLevel >= ToolRiskLevel.High)
        {
            // High-risk tools are forced into sandbox + explicit approval
            return Result<ToolDefinition>.Success(new ToolDefinition
            {
                TenantId = tenantId,
                Name = name,
                Description = description,
                Version = version,
                Scope = scope,
                RiskLevel = riskLevel,
                RequiresSandbox = true,
                RequiresExplicitApproval = true,
                InputSchemaJson = inputSchemaJson,
                OutputSchemaJson = outputSchemaJson,
                ImplementationType = implementationType,
                CreatedBy = createdBy,
                UpdatedBy = createdBy
            });
        }

        return Result<ToolDefinition>.Success(new ToolDefinition
        {
            TenantId = tenantId,
            Name = name,
            Description = description,
            Version = version,
            Scope = scope,
            RiskLevel = riskLevel,
            InputSchemaJson = inputSchemaJson,
            OutputSchemaJson = outputSchemaJson,
            ImplementationType = implementationType,
            CreatedBy = createdBy,
            UpdatedBy = createdBy
        });
    }

    public void Deprecate(string deprecatedBy)
    {
        Status = ToolStatus.Deprecated;
        MarkUpdated(deprecatedBy);
    }
}

/// <summary>
/// Immutable audit log for each tool invocation.
/// Written to MongoDB with TTL index and read-only after creation.
/// </summary>
public sealed class ToolExecutionLog
{
    public string Id { get; init; } = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
    public string TenantId { get; init; } = string.Empty;
    public string ExecutionId { get; init; } = string.Empty;
    public string StepId { get; init; } = string.Empty;
    public string ToolId { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string ToolVersion { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string InputJson { get; init; } = string.Empty;
    public string? OutputJson { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsSuccess { get; init; }
    public long DurationMs { get; init; }
    public DateTimeOffset InvokedAt { get; init; } = DateTimeOffset.UtcNow;
    public string CorrelationId { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
}
