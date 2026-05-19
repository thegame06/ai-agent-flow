namespace AgentFlow.Application.Data;

/// <summary>
/// Data access layer for intent routing persistence.
/// Handles storage and retrieval of custom intent definitions, routing rules, and ownership state.
/// Implementation backed by MongoDB.
/// </summary>
/// <remarks>
/// NOTE: This is a placeholder interface. Full implementation will be added in a future phase.
/// For now, only base intents from YAML are supported.
/// </remarks>
public interface IIntentRoutingStore
{
    // TODO: Implement methods for custom intent CRUD operations
    // - Task<IntentDefinition> GetCustomIntentAsync(string tenantId, string intentKey, CancellationToken ct);
    // - Task<IReadOnlyList<IntentDefinition>> GetAllCustomIntentsAsync(string tenantId, CancellationToken ct);
    // - Task<IntentDefinition> CreateCustomIntentAsync(string tenantId, IntentDefinition intent, CancellationToken ct);
    // - Task<IntentDefinition> UpdateCustomIntentAsync(string tenantId, string intentKey, IntentDefinition intent, CancellationToken ct);
    // - Task DeleteCustomIntentAsync(string tenantId, string intentKey, CancellationToken ct);
}
