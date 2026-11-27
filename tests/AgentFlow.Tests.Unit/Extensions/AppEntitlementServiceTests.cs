using AgentFlow.Api.Entitlements;
using AgentFlow.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentFlow.Tests.Unit.Extensions;

public sealed class AppEntitlementServiceTests
{
    private const string Tenant = "tenant-1";

    [Fact]
    public async Task Defaults_PreexistingAppsOn_CommerceOff_UnknownDenied()
    {
        var service = new AppEntitlementService(new InMemoryTenantExtensionStateStore());

        Assert.True(await service.IsEnabledAsync(Tenant, AppModules.Campaigns));
        Assert.True(await service.IsEnabledAsync(Tenant, AppModules.Conversations));
        Assert.True(await service.IsEnabledAsync(Tenant, AppModules.Workflows));
        Assert.False(await service.IsEnabledAsync(Tenant, AppModules.Inventory));
        Assert.False(await service.IsEnabledAsync(Tenant, "not-a-module"));
    }

    [Fact]
    public async Task ExplicitState_OverridesDefault_PerTenant()
    {
        var store = new InMemoryTenantExtensionStateStore();
        await store.SetStateAsync(Tenant, AppModules.Campaigns, enabled: false);
        await store.SetStateAsync(Tenant, AppModules.Inventory, enabled: true);
        var service = new AppEntitlementService(store);

        Assert.False(await service.IsEnabledAsync(Tenant, AppModules.Campaigns));
        Assert.True(await service.IsEnabledAsync(Tenant, AppModules.Inventory));
        Assert.True(await service.IsEnabledAsync("tenant-2", AppModules.Campaigns)); // other tenants untouched
    }

    [Fact]
    public async Task Quarantine_WinsOverExplicitAndDefault()
    {
        var store = new InMemoryTenantExtensionStateStore();
        await store.SetStateAsync(Tenant, AppModules.Inventory, enabled: true);
        await store.QuarantineAsync(AppModules.Inventory, "incident");
        await store.QuarantineAsync(AppModules.Workflows, "incident");
        var service = new AppEntitlementService(store);

        Assert.False(await service.IsEnabledAsync(Tenant, AppModules.Inventory));
        Assert.False(await service.IsEnabledAsync(Tenant, AppModules.Workflows));
    }

    [Fact]
    public async Task GetEffective_ListsEveryModuleWithItsSource()
    {
        var store = new InMemoryTenantExtensionStateStore();
        await store.SetStateAsync(Tenant, AppModules.Campaigns, enabled: false);
        await store.QuarantineAsync(AppModules.Billing, "incident");
        var effective = await new AppEntitlementService(store).GetEffectiveAsync(Tenant);

        Assert.Equal(AppModules.All.Count, effective.Count);
        Assert.Equal(("explicit", false), Source(effective, AppModules.Campaigns));
        Assert.Equal(("default", true), Source(effective, AppModules.Workflows));
        Assert.Equal(("default", false), Source(effective, AppModules.Inventory));
        Assert.Equal(("quarantined", false), Source(effective, AppModules.Billing));
    }

    [Fact]
    public async Task Filter_DisabledModule_Returns403WithErrorCode()
    {
        var store = new InMemoryTenantExtensionStateStore();
        await store.SetStateAsync(Tenant, AppModules.Campaigns, enabled: false);
        var context = FilterContext(store, Tenant);

        await new RequiresAppModuleAttribute(AppModules.Campaigns).OnAuthorizationAsync(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Contains(RequiresAppModuleAttribute.ErrorCode, System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task Filter_EnabledModuleOrNoTenantRoute_LetsRequestThrough()
    {
        var store = new InMemoryTenantExtensionStateStore();

        var enabled = FilterContext(store, Tenant);
        await new RequiresAppModuleAttribute(AppModules.Campaigns).OnAuthorizationAsync(enabled);
        Assert.Null(enabled.Result);

        var noTenant = FilterContext(store, tenantId: null);
        await new RequiresAppModuleAttribute(AppModules.Inventory).OnAuthorizationAsync(noTenant);
        Assert.Null(noTenant.Result);
    }

    private static (string, bool) Source(IReadOnlyList<AppModuleEntitlement> effective, string moduleId)
    {
        var entry = effective.Single(x => x.ModuleId == moduleId);
        return (entry.Source, entry.Enabled);
    }

    private static AuthorizationFilterContext FilterContext(ITenantExtensionStateStore store, string? tenantId)
    {
        var services = new ServiceCollection()
            .AddSingleton(store)
            .AddSingleton<IAppEntitlementService, AppEntitlementService>()
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var routeData = new RouteData();
        if (tenantId is not null) routeData.Values["tenantId"] = tenantId;

        var action = new ActionContext(http, routeData, new ActionDescriptor());
        return new AuthorizationFilterContext(action, new List<IFilterMetadata>());
    }
}
