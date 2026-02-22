using AgentFlow.Abstractions;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// Tenant aggregate - the root of multi-tenancy.
/// Each tenant has isolated data, rate limits, and feature flags.
/// Data isolation strategy: shared database, tenant-scoped collections with TenantId index prefix.
/// </summary>
public sealed class Tenant : AggregateRoot
{
    public string Slug { get; private set; } = string.Empty;     // URL-safe identifier
    public string DisplayName { get; private set; } = string.Empty;
    public TenantTier Tier { get; private set; } = TenantTier.Free;
    public bool IsActive { get; private set; } = true;
    public TenantQuota Quota { get; private set; } = default!;
    public TenantSettings Settings { get; private set; } = default!;

    private Tenant() { }

    public static Result<Tenant> Create(
        string slug,
        string displayName,
        TenantTier tier,
        string adminUserId)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return Result<Tenant>.Failure(Error.Validation(nameof(slug), "Tenant slug is required."));

        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9\-]{3,50}$"))
            return Result<Tenant>.Failure(Error.Validation(nameof(slug), "Slug must be 3-50 lowercase alphanumeric characters or hyphens."));

        var tenant = new Tenant
        {
            Slug = slug,
            DisplayName = displayName,
            Tier = tier,
            Quota = TenantQuota.ForTier(tier),
            Settings = new TenantSettings(),
            CreatedBy = adminUserId,
            UpdatedBy = adminUserId
        };

        // Tenant's own ID becomes its TenantId (self-referential for the root)
        tenant.TenantId = tenant.Id;

        return Result<Tenant>.Success(tenant);
    }

    public Result Suspend(string reason, string suspendedBy)
    {
        if (!IsActive)
            return Result.Failure(Error.Validation(nameof(IsActive), "Tenant is already inactive."));

        IsActive = false;
        MarkUpdated(suspendedBy);
        AddDomainEvent(new TenantSuspendedEvent(Id, Slug, reason, suspendedBy));
        return Result.Success();
    }

    public Result UpgradeTier(TenantTier newTier, string updatedBy)
    {
        if (newTier <= Tier)
            return Result.Failure(Error.Validation(nameof(newTier), "New tier must be higher than current tier."));

        Tier = newTier;
        Quota = TenantQuota.ForTier(newTier);
        MarkUpdated(updatedBy);
        return Result.Success();
    }
}

public sealed record TenantQuota
{
    public int MaxAgents { get; init; }
    public int MaxExecutionsPerDay { get; init; }
    public int MaxToolsPerAgent { get; init; }
    public int MaxTokensPerMonth { get; init; }
    public int MaxConcurrentExecutions { get; init; }

    public static TenantQuota ForTier(TenantTier tier) => tier switch
    {
        TenantTier.Free => new TenantQuota
        {
            MaxAgents = 3,
            MaxExecutionsPerDay = 100,
            MaxToolsPerAgent = 5,
            MaxTokensPerMonth = 500_000,
            MaxConcurrentExecutions = 2
        },
        TenantTier.Professional => new TenantQuota
        {
            MaxAgents = 25,
            MaxExecutionsPerDay = 5_000,
            MaxToolsPerAgent = 20,
            MaxTokensPerMonth = 10_000_000,
            MaxConcurrentExecutions = 10
        },
        TenantTier.Enterprise => new TenantQuota
        {
            MaxAgents = int.MaxValue,
            MaxExecutionsPerDay = int.MaxValue,
            MaxToolsPerAgent = int.MaxValue,
            MaxTokensPerMonth = int.MaxValue,
            MaxConcurrentExecutions = 100
        },
        _ => new TenantQuota { MaxAgents = 0, MaxExecutionsPerDay = 0, MaxToolsPerAgent = 0 }
    };
}

public sealed record TenantSettings
{
    public bool AllowExternalTools { get; init; } = false;
    public bool RequireMfaForCriticalTools { get; init; } = true;
    public string DefaultLanguage { get; init; } = "en";
    public string[] AllowedIpRanges { get; init; } = [];
    public bool EnableAuditLog { get; init; } = true;
}

public record TenantSuspendedEvent(string TenantId, string Slug, string Reason, string SuspendedBy) : DomainEvent;
