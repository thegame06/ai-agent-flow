using AgentFlow.Abstractions;
using AgentFlow.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentFlow.Tests.Unit.Extensions;

public sealed class ExtensionRegistryTenantStateTests
{
    private const string Tenant = "tenant-1";
    private const string Inventory = "inventory"; // seeded builtin commerce module

    private static ExtensionRegistry CreateRegistry(ITenantExtensionStateStore store) =>
        new(Array.Empty<IToolPlugin>(), Array.Empty<IPolicyEvaluator>(), store, NullLogger<ExtensionRegistry>.Instance);

    [Fact]
    public async Task TenantState_SurvivesRegistryRecreation_WhenStoreIsShared()
    {
        // A new registry over the same store is what a restart looks like with a durable store.
        var store = new InMemoryTenantExtensionStateStore();
        var before = CreateRegistry(store);
        Assert.True((await before.SetTenantInstallStateAsync(Tenant, Inventory, installed: true, enabled: true)).IsSuccess);

        var after = CreateRegistry(store);
        var states = await after.GetTenantExtensionStatesAsync(Tenant);

        Assert.True(states[Inventory]);
    }

    [Fact]
    public async Task Install_UnknownExtension_ReturnsNotFound()
    {
        var registry = CreateRegistry(new InMemoryTenantExtensionStateStore());

        var result = await registry.SetTenantInstallStateAsync(Tenant, "does-not-exist", installed: true, enabled: true);

        Assert.False(result.IsSuccess);
        Assert.Empty(await registry.GetTenantExtensionStatesAsync(Tenant));
    }

    [Fact]
    public async Task Install_RespectsTenantAllowlist()
    {
        var registry = CreateRegistry(new InMemoryTenantExtensionStateStore());
        await registry.SetTenantAllowlistAsync(Tenant, new[] { "billing" });

        var blocked = await registry.SetTenantInstallStateAsync(Tenant, Inventory, installed: true, enabled: true);
        var allowed = await registry.SetTenantInstallStateAsync(Tenant, "billing", installed: true, enabled: true);

        Assert.False(blocked.IsSuccess);
        Assert.True(allowed.IsSuccess);
        Assert.Equal(new[] { "billing" }, (await registry.GetTenantExtensionStatesAsync(Tenant)).Keys);
    }

    [Fact]
    public async Task EnableDisableAndUninstall_UpdateState()
    {
        var registry = CreateRegistry(new InMemoryTenantExtensionStateStore());

        Assert.False((await registry.SetTenantEnabledStateAsync(Tenant, Inventory, enabled: true)).IsSuccess); // not installed
        await registry.SetTenantInstallStateAsync(Tenant, Inventory, installed: true, enabled: false);
        Assert.True((await registry.SetTenantEnabledStateAsync(Tenant, Inventory, enabled: true)).IsSuccess);
        Assert.True((await registry.GetTenantExtensionStatesAsync(Tenant))[Inventory]);

        await registry.SetTenantInstallStateAsync(Tenant, Inventory, installed: false, enabled: false);
        Assert.Empty(await registry.GetTenantExtensionStatesAsync(Tenant));
    }

    [Fact]
    public async Task Quarantine_DisablesForAllTenants_BlocksEnable_AndSurvivesRecreation()
    {
        var store = new InMemoryTenantExtensionStateStore();
        var registry = CreateRegistry(store);
        await registry.SetTenantInstallStateAsync("tenant-a", Inventory, installed: true, enabled: true);
        await registry.SetTenantInstallStateAsync("tenant-b", Inventory, installed: true, enabled: true);

        await registry.QuarantineExtensionAsync(Inventory, "CVE in dependency");

        var recreated = CreateRegistry(store);
        Assert.False((await recreated.GetTenantExtensionStatesAsync("tenant-a"))[Inventory]);
        Assert.False((await recreated.GetTenantExtensionStatesAsync("tenant-b"))[Inventory]);
        Assert.False((await recreated.SetTenantEnabledStateAsync("tenant-a", Inventory, enabled: true)).IsSuccess);

        var entry = Assert.Single(await recreated.BrowseCatalogAsync(Inventory));
        Assert.True(entry.Metadata.IsQuarantined);
        Assert.Equal("CVE in dependency", entry.Metadata.QuarantineReason);
    }
}
