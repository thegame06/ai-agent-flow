using AgentFlow.Security;

namespace AgentFlow.Tests.Unit.Security;

public sealed class AgentAuthorizationServiceTests
{
    [Fact]
    public async Task CanHandoffExecutionAsync_ReturnsTrue_WhenPermissionPresent()
    {
        var service = new AgentAuthorizationService();
        var context = new TenantContext
        {
            TenantId = "tenant-1",
            UserId = "u1",
            Roles = new[] { "operator" },
            Permissions = new[] { AgentFlowPermissions.ExecutionHandoff }
        };

        var allowed = await service.CanHandoffExecutionAsync(context, "manager-agent", "sales-agent", CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public async Task CanHandoffExecutionAsync_ReturnsFalse_WhenPermissionMissing()
    {
        var service = new AgentAuthorizationService();
        var context = new TenantContext
        {
            TenantId = "tenant-1",
            UserId = "u1",
            Roles = new[] { "viewer" },
            Permissions = Array.Empty<string>()
        };

        var allowed = await service.CanHandoffExecutionAsync(context, "manager-agent", "sales-agent", CancellationToken.None);

        Assert.False(allowed);
    }
}
