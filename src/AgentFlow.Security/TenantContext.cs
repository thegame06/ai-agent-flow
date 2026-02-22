using System.Security.Claims;

namespace AgentFlow.Security;

/// <summary>
/// Tenant context - propagated through the execution pipeline.
/// Validated at API boundary, never trusted from client payload.
/// </summary>
public sealed class TenantContext
{
    public string TenantId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string UserEmail { get; init; } = string.Empty;
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public string TenantSlug { get; init; } = string.Empty;
    public Domain.Enums.TenantTier TenantTier { get; init; }
    public bool IsPlatformAdmin { get; init; } = false;

    public bool HasRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    public static TenantContext FromClaims(ClaimsPrincipal principal)
    {
        var tenantId = principal.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value
            ?? throw new SecurityException("Missing tenant_id claim.");

        var userId = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
            ?? throw new SecurityException("Missing user identity claim.");

        var roles = principal.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        var permissions = principal.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToList();

        return new TenantContext
        {
            TenantId = tenantId,
            UserId = userId,
            UserEmail = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value ?? string.Empty,
            TenantSlug = principal.Claims.FirstOrDefault(c => c.Type == "tenant_slug")?.Value ?? string.Empty,
            Roles = roles.AsReadOnly(),
            Permissions = permissions.AsReadOnly(),
            IsPlatformAdmin = roles.Contains("platform_admin", StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed class SecurityException : Exception
{
    public SecurityException(string message) : base(message) { }
}

/// <summary>
/// Ambient tenant context for the current request scope.
/// Never use static storage - always scoped via DI.
/// </summary>
public interface ITenantContextAccessor
{
    TenantContext? Current { get; }
    void Set(TenantContext context);
}

public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private TenantContext? _context;

    public TenantContext? Current => _context;

    public void Set(TenantContext context)
    {
        if (_context is not null)
            throw new InvalidOperationException("TenantContext already set for this scope. Cannot override.");
        _context = context;
    }
}
