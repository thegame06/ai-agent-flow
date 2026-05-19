using AgentFlow.Intents.Catalog.Models;

namespace AgentFlow.Intents.Catalog;

/// <summary>
/// Service for managing intent catalogs: base intents (from YAML) and tenant-specific custom intents.
/// Provides a unified interface for loading, caching, and retrieving intent definitions.
/// </summary>
public interface IIntentCatalogService
{
    /// <summary>
    /// Retrieves all base intents from the catalog YAML file.
    /// These are immutable, pre-configured intents shared across all tenants.
    /// Results are cached for performance.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of base intent definitions.</returns>
    Task<IReadOnlyList<IntentDefinition>> GetBaseIntentsAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves all custom intents defined by a specific tenant.
    /// Custom intents are stored in the database and can be created/modified by tenant admins.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of tenant-specific custom intent definitions.</returns>
    Task<IReadOnlyList<IntentDefinition>> GetTenantIntentsAsync(
        string tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all intents available for a tenant: base intents + tenant-specific custom intents.
    /// This is the complete set of intents that should be indexed and available for matching.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of all intent definitions (base + custom) for the tenant.</returns>
    Task<IReadOnlyList<IntentDefinition>> GetAllIntentsForTenantAsync(
        string tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new custom intent for a tenant.
    /// The intent is persisted to the database and must be re-indexed for semantic search.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="intent">The intent definition to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created intent definition with generated metadata.</returns>
    Task<IntentDefinition> CreateCustomIntentAsync(
        string tenantId,
        IntentDefinition intent,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing custom intent for a tenant.
    /// Base intents cannot be modified. Version is automatically incremented.
    /// The intent must be re-indexed after update.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="intentKey">The unique key of the intent to update.</param>
    /// <param name="intent">The updated intent definition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated intent definition.</returns>
    Task<IntentDefinition> UpdateCustomIntentAsync(
        string tenantId,
        string intentKey,
        IntentDefinition intent,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a custom intent for a tenant.
    /// Base intents cannot be deleted.
    /// The intent must be removed from the vector index after deletion.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="intentKey">The unique key of the intent to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteCustomIntentAsync(
        string tenantId,
        string intentKey,
        CancellationToken ct = default);
}
