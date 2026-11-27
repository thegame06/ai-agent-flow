namespace AgentFlow.Extensions;

/// <summary>A sellable app module that can be turned on or off per tenant.</summary>
/// <param name="Id">Stable module id, also used as the extension id in the registry.</param>
/// <param name="App">The app (product package) this module belongs to.</param>
/// <param name="DefaultEnabled">
/// Effective state when the tenant has no explicit state for the module.
/// Modules that existed before entitlements were enforced default to enabled so
/// current tenants keep working; commerce modules were always opt-in.
/// </param>
public sealed record AppModuleDefinition(
    string Id,
    string Name,
    string Description,
    string App,
    bool DefaultEnabled);

/// <summary>
/// Catalog of gated app modules. Platform capabilities (agents, channels, intents,
/// models, governance, executions) are always included and are not listed here.
/// </summary>
public static class AppModules
{
    public const string Campaigns = "campaigns";
    public const string Conversations = "conversations";
    public const string Workflows = "workflows";
    public const string Collections = "collections";

    public const string CommunicationInbox = "communication-inbox";
    public const string Inventory = "inventory";
    public const string SalesPos = "sales-pos";
    public const string Billing = "billing";

    public static readonly IReadOnlyList<AppModuleDefinition> All =
    [
        new(Campaigns, "Campaigns", "Campanas salientes, segmentos y playbooks de llamada", "Campanas", DefaultEnabled: true),
        new(Conversations, "Conversations", "Bandeja de conversaciones y casos sin clasificar", "Bandeja", DefaultEnabled: true),
        new(Workflows, "Workflows", "Automatizaciones: diseno, publicacion y control de workflows", "Automatizaciones", DefaultEnabled: true),
        // New sellable app: opt-in per tenant.
        new(Collections, "Collections", "Cobranza: cartera de clientes y prestamos, importacion y seguimiento por dias de atraso", "Cobranza", DefaultEnabled: false),
        new(CommunicationInbox, "Communication Inbox", "Inbox comercial integrado en chat", "Comercio", DefaultEnabled: false),
        new(Inventory, "Inventory", "Inventario y catalogo de productos", "Comercio", DefaultEnabled: false),
        new(SalesPos, "Sales POS", "Punto de venta y ordenes comerciales", "Comercio", DefaultEnabled: false),
        new(Billing, "Billing", "Facturacion y estados de cobro", "Comercio", DefaultEnabled: false)
    ];

    public static AppModuleDefinition? Find(string moduleId) =>
        All.FirstOrDefault(x => string.Equals(x.Id, moduleId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Effective state of one app module for a tenant.</summary>
/// <param name="Source">"explicit" (tenant state), "default" (module default) or "quarantined".</param>
public sealed record AppModuleEntitlement(
    string ModuleId,
    string Name,
    string App,
    bool Enabled,
    string Source);

/// <summary>Resolves whether a tenant may use an app module.</summary>
public interface IAppEntitlementService
{
    /// <summary>True when the module is enabled for the tenant. Unknown modules are denied.</summary>
    Task<bool> IsEnabledAsync(string tenantId, string moduleId, CancellationToken ct = default);

    /// <summary>Effective state of every catalog module for the tenant.</summary>
    Task<IReadOnlyList<AppModuleEntitlement>> GetEffectiveAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>
/// Effective state = quarantine (always off) → explicit tenant state → module default.
/// Reads the persisted store on every call, so all instances agree.
/// </summary>
public sealed class AppEntitlementService : IAppEntitlementService
{
    private readonly ITenantExtensionStateStore _store;

    public AppEntitlementService(ITenantExtensionStateStore store) => _store = store;

    public async Task<bool> IsEnabledAsync(string tenantId, string moduleId, CancellationToken ct = default)
    {
        var module = AppModules.Find(moduleId);
        if (module is null) return false;

        var quarantine = await _store.GetQuarantinedAsync(ct);
        if (quarantine.ContainsKey(module.Id)) return false;

        var states = await _store.GetStatesAsync(tenantId, ct);
        return states.TryGetValue(module.Id, out var enabled) ? enabled : module.DefaultEnabled;
    }

    public async Task<IReadOnlyList<AppModuleEntitlement>> GetEffectiveAsync(string tenantId, CancellationToken ct = default)
    {
        var quarantine = await _store.GetQuarantinedAsync(ct);
        var states = await _store.GetStatesAsync(tenantId, ct);

        return AppModules.All
            .Select(module =>
            {
                if (quarantine.ContainsKey(module.Id))
                    return new AppModuleEntitlement(module.Id, module.Name, module.App, false, "quarantined");

                return states.TryGetValue(module.Id, out var enabled)
                    ? new AppModuleEntitlement(module.Id, module.Name, module.App, enabled, "explicit")
                    : new AppModuleEntitlement(module.Id, module.Name, module.App, module.DefaultEnabled, "default");
            })
            .ToList();
    }
}
