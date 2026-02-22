using AgentFlow.Abstractions;
using AgentFlow.Domain.Common;

namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// Aggregate root for a set of policies governing agents.
/// Follows the principle: Everything is versioned and Published sets are immutable.
/// </summary>
public sealed class PolicySetDefinition : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Version { get; private set; } = "1.0.0";
    public bool IsPublished { get; private set; }
    public IReadOnlyList<PolicyDefinition> Policies { get; private set; } = [];

    private PolicySetDefinition() { } // For MongoDB

    public static Result<PolicySetDefinition> Create(
        string tenantId,
        string name,
        string description,
        string createdBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<PolicySetDefinition>.Failure(Error.Validation("Name", "Policy set name is required."));

        return Result<PolicySetDefinition>.Success(new PolicySetDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            Name = name,
            Description = description,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
            IsPublished = false,
            Policies = []
        });
    }

    public Result UpdatePolicies(IReadOnlyList<PolicyDefinition> policies, string updatedBy)
    {
        if (IsPublished)
            return Result.Failure(Error.Forbidden("Cannot update a published policy set. Create a new version."));

        Policies = policies;
        UpdatedBy = updatedBy;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Success();
    }

    public Result Publish(string publishedBy)
    {
        if (IsPublished)
            return Result.Failure(Error.Forbidden("Policy set is already published."));

        IsPublished = true;
        UpdatedBy = publishedBy;
        UpdatedAt = DateTimeOffset.UtcNow;
        return Result.Success();
    }
}
