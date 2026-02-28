namespace AgentFlow.Security;

/// <summary>
/// RBAC Policy definitions.
/// 
/// Design: permission-based (not role-based at check sites).
/// Roles are assigned permissions. Code checks permissions, not roles.
/// This allows role definitions to change without code changes.
/// </summary>
public static class AgentFlowPermissions
{
    // Agent permissions
    public const string AgentRead = "agent:read";
    public const string AgentCreate = "agent:create";
    public const string AgentUpdate = "agent:update";
    public const string AgentDelete = "agent:delete";
    public const string AgentPublish = "agent:publish";

    // Execution permissions
    public const string ExecutionTrigger = "execution:trigger";
    public const string ExecutionRead = "execution:read";
    public const string ExecutionCancel = "execution:cancel";
    public const string ExecutionHandoff = "execution:handoff";
    public const string ExecutionReadAll = "execution:read_all"; // All tenant executions

    // Tool permissions
    public const string ToolRead = "tool:read";
    public const string ToolCreate = "tool:create";
    public const string ToolUpdate = "tool:update";
    public const string ToolDelete = "tool:delete";
    public const string ToolExecuteLow = "tool:execute:low";
    public const string ToolExecuteMedium = "tool:execute:medium";
    public const string ToolExecuteHigh = "tool:execute:high";
    public const string ToolExecuteCritical = "tool:execute:critical"; // Requires MFA

    // Audit permissions
    public const string AuditRead = "audit:read";

    // Tenant management (admin only)
    public const string TenantManage = "tenant:manage";
    public const string TenantRead = "tenant:read";

    // Platform admin (cross-tenant)
    public const string PlatformAdmin = "platform:admin";
}

/// <summary>
/// Pre-defined role → permission mappings.
/// Stored in DB per-tenant for customization.
/// These are defaults.
/// </summary>
public static class AgentFlowRoles
{
    public static readonly string[] Viewer =
    [
        AgentFlowPermissions.AgentRead,
        AgentFlowPermissions.ExecutionRead,
        AgentFlowPermissions.ToolRead
    ];

    public static readonly string[] Operator =
    [
        ..Viewer,
        AgentFlowPermissions.ExecutionTrigger,
        AgentFlowPermissions.ExecutionCancel,
        AgentFlowPermissions.ExecutionHandoff,
        AgentFlowPermissions.ToolExecuteLow,
        AgentFlowPermissions.ToolExecuteMedium
    ];

    public static readonly string[] Developer =
    [
        ..Operator,
        AgentFlowPermissions.AgentCreate,
        AgentFlowPermissions.AgentUpdate,
        AgentFlowPermissions.ToolCreate,
        AgentFlowPermissions.ToolUpdate,
        AgentFlowPermissions.ToolExecuteHigh
    ];

    public static readonly string[] Admin =
    [
        ..Developer,
        AgentFlowPermissions.AgentDelete,
        AgentFlowPermissions.AgentPublish,
        AgentFlowPermissions.ToolDelete,
        AgentFlowPermissions.ToolExecuteCritical,
        AgentFlowPermissions.AuditRead,
        AgentFlowPermissions.TenantManage,
        AgentFlowPermissions.TenantRead
    ];
}

/// <summary>
/// Authorization service for agent-level operations.
/// </summary>
public interface IAgentAuthorizationService
{
    Task<bool> CanCreateAgentAsync(TenantContext context, CancellationToken ct = default);
    Task<bool> CanReadAgentAsync(TenantContext context, string agentId, CancellationToken ct = default);
    Task<bool> CanUpdateAgentAsync(TenantContext context, string agentId, CancellationToken ct = default);
    Task<bool> CanPublishAgentAsync(TenantContext context, string agentId, CancellationToken ct = default);
    Task<bool> CanTriggerExecutionAsync(TenantContext context, string agentId, CancellationToken ct = default);
    Task<bool> CanCancelExecutionAsync(TenantContext context, string executionId, CancellationToken ct = default);
    Task<bool> CanHandoffExecutionAsync(TenantContext context, string sourceAgentId, string targetAgentId, CancellationToken ct = default);
}

public sealed class AgentAuthorizationService : IAgentAuthorizationService
{
    // All checks are pure permission lookups + tenant boundary enforcement
    public Task<bool> CanCreateAgentAsync(TenantContext context, CancellationToken ct = default)
        => Task.FromResult(context.HasPermission(AgentFlowPermissions.AgentCreate));

    public Task<bool> CanReadAgentAsync(TenantContext context, string agentId, CancellationToken ct = default)
        => Task.FromResult(context.HasPermission(AgentFlowPermissions.AgentRead));

    public Task<bool> CanUpdateAgentAsync(TenantContext context, string agentId, CancellationToken ct = default)
        => Task.FromResult(context.HasPermission(AgentFlowPermissions.AgentUpdate));

    public Task<bool> CanPublishAgentAsync(TenantContext context, string agentId, CancellationToken ct = default)
        => Task.FromResult(context.HasPermission(AgentFlowPermissions.AgentPublish));

    public Task<bool> CanTriggerExecutionAsync(TenantContext context, string agentId, CancellationToken ct = default)
        => Task.FromResult(context.HasPermission(AgentFlowPermissions.ExecutionTrigger));

    public Task<bool> CanCancelExecutionAsync(TenantContext context, string executionId, CancellationToken ct = default)
        => Task.FromResult(context.HasPermission(AgentFlowPermissions.ExecutionCancel));

    public Task<bool> CanHandoffExecutionAsync(TenantContext context, string sourceAgentId, string targetAgentId, CancellationToken ct = default)
        => Task.FromResult(context.HasPermission(AgentFlowPermissions.ExecutionHandoff));
}
