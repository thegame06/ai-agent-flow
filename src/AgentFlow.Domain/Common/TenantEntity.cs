namespace AgentFlow.Domain.Common;

/// <summary>
/// Multi-tenant base entity.
/// CRITICAL: Every query MUST filter by TenantId first.
/// MongoDB indices must include TenantId as the leading key.
/// </summary>
public abstract class TenantEntity : Entity
{
    public string TenantId { get; protected set; } = string.Empty;

    protected TenantEntity(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId cannot be empty. Multi-tenancy requires explicit tenant context.", nameof(tenantId));
        
        TenantId = tenantId;
    }

    // For MongoDB deserialization
    protected TenantEntity() { }
}
