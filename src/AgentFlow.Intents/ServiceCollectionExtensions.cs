using AgentFlow.Intents.Catalog;
using AgentFlow.Intents.Classification;
using AgentFlow.Intents.Inbox;
using AgentFlow.Intents.Indexing;
using AgentFlow.Intents.Ownership;
using AgentFlow.Intents.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentFlow.Intents;

/// <summary>
/// Extension methods for registering Intent Routing services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Intent Routing services with the service collection.
    /// Includes classification, routing orchestration, ownership, catalog, indexing, and bootstrap services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIntentRouting(this IServiceCollection services)
    {
        // ========================================
        // CLASSIFICATION SERVICES
        // ========================================
        
        // Register the semantic intent matcher
        services.AddScoped<ISemanticIntentMatcher, QdrantSemanticIntentMatcher>();

        // Register the keyword intent matcher
        services.AddScoped<IKeywordIntentMatcher, KeywordIntentMatcher>();

        // Register the hybrid scoring engine (combines semantic + keyword + priority)
        services.AddScoped<IIntentScoringEngine, IntentScoringEngine>();

        // ========================================
        // ROUTING ORCHESTRATION
        // ========================================
        
        // Register the routing orchestrator (core decision-making component)
        services.AddScoped<IRoutingOrchestrator, RoutingOrchestrator>();

        // ========================================
        // OWNERSHIP SERVICES
        // ========================================
        
        // Register the conversation ownership manager (critical for single-agent-per-conversation rule)
        services.AddSingleton<IConversationOwnershipManager, ConversationOwnershipManager>();

        // ========================================
        // INBOX SERVICES
        // ========================================
        
        // Register the conversation inbox service (stores conversations requiring human review)
        services.AddScoped<IConversationInboxService, ConversationInboxService>();

        // ========================================
        // CATALOG & INDEXING SERVICES
        // ========================================
        
        // Register the intent catalog service (loads base intents from YAML)
        services.AddSingleton<IIntentCatalogService, IntentCatalogService>();

        // Fallback embedding generator to keep the system bootable in environments
        // where a provider-specific embedding generator has not yet been configured.
        services.TryAddSingleton<IEmbeddingGenerator, HashEmbeddingGenerator>();

        // Register the intent vector indexer (indexes intents into Qdrant)
        services.AddSingleton<IntentVectorIndexer>();

        // Register the bootstrap service (runs on startup to load and validate base intents)
        services.AddHostedService<IntentBootstrapService>();

        // ========================================
        // NOTES ON EXTERNAL DEPENDENCIES
        // ========================================
        
        // Note: IEmbeddingGenerator must be registered by the host application
        // Example:
        // services.AddSingleton<IEmbeddingGenerator, OpenAIEmbeddingGenerator>();
        // or
        // services.AddSingleton<IEmbeddingGenerator, AzureEmbeddingGenerator>();

        // Note: IDistributedLockService and IConnectionMultiplexer must be registered
        // via AgentFlow.Caching.Redis services:
        // services.AddRedisServices(configuration);

        // Note: IVectorMemory must be registered by the host application
        // Example (in AgentFlow.Api/DependencyInjection.cs):
        // services.AddSingleton<IVectorMemory, QdrantVectorMemory>();

        // Note: IIntentRoutingStore is a placeholder for future custom intent persistence
        // For now, only base intents from YAML are supported

        return services;
    }
}
