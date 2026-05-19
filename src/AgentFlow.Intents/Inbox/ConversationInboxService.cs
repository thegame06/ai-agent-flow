using AgentFlow.Intents.Classification.Models;
using AgentFlow.Intents.Inbox.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Intents.Inbox;

/// <summary>
/// MongoDB-backed implementation of the Conversation Inbox Service.
/// Provides persistent storage and querying for conversations requiring human review.
/// </summary>
/// <remarks>
/// <para><b>Collection:</b> conversation_inbox</para>
/// <para><b>Indexes:</b></para>
/// <list type="bullet">
///   <item><description>Compound: (TenantId ASC, State ASC, UpdatedAt DESC)</description></item>
///   <item><description>Used for efficient filtering and sorting in GetPendingAsync</description></item>
/// </list>
/// <para><b>Performance Considerations:</b></para>
/// <list type="bullet">
///   <item><description>Stats queries use aggregation pipelines for efficiency</description></item>
///   <item><description>All queries filter by TenantId first to leverage index</description></item>
///   <item><description>Consider adding caching for GetStatsAsync in high-traffic scenarios</description></item>
/// </list>
/// </remarks>
public sealed class ConversationInboxService : IConversationInboxService
{
    private readonly IMongoCollection<InboxConversationDocument> _collection;
    private readonly ILogger<ConversationInboxService> _logger;

    public ConversationInboxService(IMongoDatabase database, ILogger<ConversationInboxService> logger)
    {
        _collection = database.GetCollection<InboxConversationDocument>("conversation_inbox");
        _logger = logger;
        
        // Create indexes for efficient querying
        CreateIndexes();
    }

    /// <summary>
    /// Creates MongoDB indexes to optimize queries.
    /// Called once during service initialization.
    /// </summary>
    private void CreateIndexes()
    {
        try
        {
            // Compound index for filtering and sorting
            var indexKeys = Builders<InboxConversationDocument>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.State)
                .Descending(x => x.UpdatedAt);
            
            var indexModel = new CreateIndexModel<InboxConversationDocument>(
                indexKeys,
                new CreateIndexOptions { Name = "idx_tenant_state_updated" });

            _collection.Indexes.CreateOne(indexModel);
            
            _logger.LogInformation("Conversation inbox indexes created successfully");
        }
        catch (Exception ex)
        {
            // Index creation failures are non-fatal (indexes may already exist)
            _logger.LogWarning(ex, "Failed to create conversation inbox indexes (may already exist)");
        }
    }

    public async Task<InboxConversation> CreateOrUpdateAsync(
        InboxConversation conversation,
        CancellationToken ct = default)
    {
        var doc = MapToDocument(conversation);
        
        await _collection.ReplaceOneAsync(
            x => x.TenantId == doc.TenantId && x.Id == doc.Id,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);

        _logger.LogInformation(
            "Inbox conversation {ConvId} created/updated. State: {State}, Confidence: {Confidence}, RequiresReview: {RequiresReview}",
            conversation.Id, conversation.State, conversation.Confidence, conversation.RequiresHumanReview);

        return conversation;
    }

    public async Task<PagedResult<InboxConversation>> GetPendingAsync(
        string tenantId,
        InboxFilter filter,
        CancellationToken ct = default)
    {
        var query = BuildFilterQuery(tenantId, filter);
        
        // Get total count for pagination
        var total = await _collection.CountDocumentsAsync(query, cancellationToken: ct);
        
        // Get paginated results sorted by most recent first
        var docs = await _collection
            .Find(query)
            .Sort(Builders<InboxConversationDocument>.Sort.Descending(x => x.UpdatedAt))
            .Skip(filter.Skip)
            .Limit(filter.Take)
            .ToListAsync(ct);

        _logger.LogDebug(
            "Retrieved {Count} conversations for tenant {TenantId} (page {Page}/{TotalPages})",
            docs.Count, tenantId, filter.Page, Math.Ceiling((double)total / filter.PageSize));

        return new PagedResult<InboxConversation>
        {
            Items = docs.Select(MapToModel).ToList(),
            Total = (int)total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<InboxConversation?> GetByIdAsync(
        string tenantId,
        string conversationId,
        CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(x => x.TenantId == tenantId && x.Id == conversationId)
            .FirstOrDefaultAsync(ct);

        if (doc == null)
        {
            _logger.LogDebug("Conversation {ConvId} not found for tenant {TenantId}", conversationId, tenantId);
        }

        return doc == null ? null : MapToModel(doc);
    }

    public async Task<bool> UpdateStateAsync(
        string tenantId,
        string conversationId,
        ConversationState newState,
        string? notes = null,
        CancellationToken ct = default)
    {
        var update = Builders<InboxConversationDocument>.Update
            .Set(x => x.State, newState.ToString())
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        if (notes != null)
        {
            update = update.Set(x => x.ReviewNotes, notes);
        }

        if (newState == ConversationState.Resolved)
        {
            update = update.Set(x => x.ResolvedAt, DateTime.UtcNow);
        }

        var result = await _collection.UpdateOneAsync(
            x => x.TenantId == tenantId && x.Id == conversationId,
            update,
            cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation(
                "Conversation {ConvId} state updated to {State}. Notes: {HasNotes}",
                conversationId, newState, notes != null);
        }
        else
        {
            _logger.LogWarning(
                "Failed to update conversation {ConvId} for tenant {TenantId} (not found)",
                conversationId, tenantId);
        }

        return result.ModifiedCount > 0;
    }

    public async Task<InboxStats> GetStatsAsync(
        string tenantId,
        CancellationToken ct = default)
    {
        var filter = Builders<InboxConversationDocument>.Filter.Eq(x => x.TenantId, tenantId);
        
        // Total count
        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        
        // Aggregate by state
        var statePipeline = _collection.Aggregate()
            .Match(filter)
            .Group(x => x.State, g => new { State = g.Key, Count = g.Count() });

        var stateGroups = await statePipeline.ToListAsync(ct);
        var byState = stateGroups.ToDictionary(
            x => Enum.Parse<ConversationState>(x.State),
            x => x.Count);

        // Aggregate by confidence
        var confidencePipeline = _collection.Aggregate()
            .Match(filter)
            .Group(x => x.Confidence, g => new { Confidence = g.Key, Count = g.Count() });

        var confidenceGroups = await confidencePipeline.ToListAsync(ct);
        var byConfidence = confidenceGroups.ToDictionary(
            x => Enum.Parse<ConfidenceLevel>(x.Confidence),
            x => x.Count);

        // Count resolved today (UTC)
        var today = DateTime.UtcNow.Date;
        var resolvedToday = await _collection.CountDocumentsAsync(
            Builders<InboxConversationDocument>.Filter.And(
                filter,
                Builders<InboxConversationDocument>.Filter.Eq(x => x.State, ConversationState.Resolved.ToString()),
                Builders<InboxConversationDocument>.Filter.Gte(x => x.ResolvedAt, today)),
            cancellationToken: ct);

        _logger.LogDebug(
            "Stats for tenant {TenantId}: Total={Total}, Pending={Pending}, Resolved={Resolved}",
            tenantId, total, byState.GetValueOrDefault(ConversationState.PendingHumanReview), resolvedToday);

        return new InboxStats
        {
            TotalConversations = (int)total,
            AwaitingClassification = byState.GetValueOrDefault(ConversationState.AwaitingClassification, 0),
            RequiresReview = byState.GetValueOrDefault(ConversationState.PendingHumanReview, 0) +
                            byState.GetValueOrDefault(ConversationState.LowConfidence, 0),
            ResolvedToday = (int)resolvedToday,
            InProgress = byState.GetValueOrDefault(ConversationState.InProgress, 0),
            NoMatch = byState.GetValueOrDefault(ConversationState.NoMatch, 0),
            ByState = byState,
            ByConfidence = byConfidence
        };
    }

    /// <summary>
    /// Builds a MongoDB filter from the InboxFilter criteria.
    /// Combines all non-null filters with AND logic.
    /// </summary>
    private FilterDefinition<InboxConversationDocument> BuildFilterQuery(
        string tenantId,
        InboxFilter filter)
    {
        var builder = Builders<InboxConversationDocument>.Filter;
        var filters = new List<FilterDefinition<InboxConversationDocument>>
        {
            builder.Eq(x => x.TenantId, tenantId)
        };

        if (filter.State.HasValue)
        {
            filters.Add(builder.Eq(x => x.State, filter.State.Value.ToString()));
        }

        if (filter.Confidence.HasValue)
        {
            filters.Add(builder.Eq(x => x.Confidence, filter.Confidence.Value.ToString()));
        }

        if (!string.IsNullOrEmpty(filter.Channel))
        {
            filters.Add(builder.Eq(x => x.Channel, filter.Channel));
        }

        if (filter.RequiresReview.HasValue)
        {
            filters.Add(builder.Eq(x => x.RequiresHumanReview, filter.RequiresReview.Value));
        }

        return builder.And(filters);
    }

    /// <summary>
    /// Maps domain model to MongoDB document.
    /// </summary>
    private InboxConversationDocument MapToDocument(InboxConversation model)
    {
        return new InboxConversationDocument
        {
            Id = model.Id,
            TenantId = model.TenantId,
            Channel = model.Channel,
            UserIdentifier = model.UserIdentifier,
            LastMessage = model.LastMessage,
            State = model.State.ToString(),
            Confidence = model.Confidence.ToString(),
            DetectedIntentKey = model.DetectedIntentKey,
            AssignedAgentId = model.AssignedAgentId,
            WorkflowExecutionId = model.WorkflowExecutionId,
            CreatedAt = model.CreatedAt.UtcDateTime,
            UpdatedAt = model.UpdatedAt.UtcDateTime,
            RequiresHumanReview = model.RequiresHumanReview,
            ReviewNotes = model.ReviewNotes,
            ResolvedBy = model.ResolvedBy,
            ResolvedAt = model.ResolvedAt?.UtcDateTime
        };
    }

    /// <summary>
    /// Maps MongoDB document to domain model.
    /// </summary>
    private InboxConversation MapToModel(InboxConversationDocument doc)
    {
        return new InboxConversation
        {
            Id = doc.Id,
            TenantId = doc.TenantId,
            Channel = doc.Channel,
            UserIdentifier = doc.UserIdentifier,
            LastMessage = doc.LastMessage,
            State = Enum.Parse<ConversationState>(doc.State),
            Confidence = Enum.Parse<ConfidenceLevel>(doc.Confidence),
            DetectedIntentKey = doc.DetectedIntentKey,
            AssignedAgentId = doc.AssignedAgentId,
            WorkflowExecutionId = doc.WorkflowExecutionId,
            CreatedAt = new DateTimeOffset(doc.CreatedAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(doc.UpdatedAt, TimeSpan.Zero),
            RequiresHumanReview = doc.RequiresHumanReview,
            ReviewNotes = doc.ReviewNotes,
            ResolvedBy = doc.ResolvedBy,
            ResolvedAt = doc.ResolvedAt.HasValue ? new DateTimeOffset(doc.ResolvedAt.Value, TimeSpan.Zero) : null
        };
    }

    /// <summary>
    /// MongoDB document structure for conversation inbox.
    /// Stores all conversation metadata and state.
    /// </summary>
    private sealed class InboxConversationDocument
    {
        [BsonId]
        public string Id { get; set; } = null!;
        
        public string TenantId { get; set; } = null!;
        public string Channel { get; set; } = null!;
        public string UserIdentifier { get; set; } = null!;
        public string LastMessage { get; set; } = null!;
        public string State { get; set; } = null!;
        public string Confidence { get; set; } = null!;
        public string? DetectedIntentKey { get; set; }
        public string? AssignedAgentId { get; set; }
        public string? WorkflowExecutionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool RequiresHumanReview { get; set; }
        public string? ReviewNotes { get; set; }
        public string? ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }
}
