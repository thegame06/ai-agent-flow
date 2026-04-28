using AgentFlow.Abstractions.Connect;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Connect;

public interface IConnectStore
{
    Task<IReadOnlyList<ConnectTemplateContract>> GetTemplatesAsync(string tenantId, CancellationToken ct = default);
    Task<ConnectTemplateContract?> GetTemplateAsync(string tenantId, string templateId, CancellationToken ct = default);
    Task<ConnectTemplateContract> UpsertTemplateAsync(ConnectTemplateContract template, CancellationToken ct = default);

    Task<IReadOnlyList<ConnectCampaignContract>> GetCampaignsAsync(string tenantId, CancellationToken ct = default);
    Task<ConnectCampaignContract> UpsertCampaignAsync(ConnectCampaignContract campaign, CancellationToken ct = default);

    Task<IReadOnlyList<ConnectInboxMessageContract>> GetInboxAsync(string tenantId, int limit, CancellationToken ct = default);
    Task<ConnectInboxMessageContract> CreateInboxMessageAsync(ConnectInboxMessageContract message, CancellationToken ct = default);
    Task<ConnectInboxMessageContract?> UpdateMessageStatusAsync(string tenantId, string messageId, ConnectOperationalStatus status, string updatedBy, string? lastError, CancellationToken ct = default);
}

public sealed class MongoConnectStore : IConnectStore
{
    private readonly IMongoCollection<ConnectTemplateDocument> _templates;
    private readonly IMongoCollection<ConnectCampaignDocument> _campaigns;
    private readonly IMongoCollection<ConnectInboxDocument> _inbox;

    public MongoConnectStore(IMongoDatabase database)
    {
        _templates = database.GetCollection<ConnectTemplateDocument>("connect_templates");
        _campaigns = database.GetCollection<ConnectCampaignDocument>("connect_campaigns");
        _inbox = database.GetCollection<ConnectInboxDocument>("connect_inbox");
    }

    public async Task<IReadOnlyList<ConnectTemplateContract>> GetTemplatesAsync(string tenantId, CancellationToken ct = default)
    {
        var docs = await _templates.Find(x => x.TenantId == tenantId).SortByDescending(x => x.UpdatedAt).ToListAsync(ct);
        return docs.Select(ToContract).ToList();
    }

    public async Task<ConnectTemplateContract?> GetTemplateAsync(string tenantId, string templateId, CancellationToken ct = default)
    {
        var doc = await _templates.Find(x => x.TenantId == tenantId && x.Id == templateId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToContract(doc);
    }

    public async Task<ConnectTemplateContract> UpsertTemplateAsync(ConnectTemplateContract template, CancellationToken ct = default)
    {
        var doc = new ConnectTemplateDocument
        {
            Id = template.Id,
            TenantId = template.TenantId,
            Name = template.Name,
            Channel = template.Channel,
            Body = template.Body,
            PublishedWorkflowAgentId = template.PublishedWorkflowAgentId,
            PublishedWorkflowAgentName = template.PublishedWorkflowAgentName,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            UpdatedBy = template.UpdatedBy
        };

        await _templates.ReplaceOneAsync(
            x => x.TenantId == template.TenantId && x.Id == template.Id,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return ToContract(doc);
    }

    public async Task<IReadOnlyList<ConnectCampaignContract>> GetCampaignsAsync(string tenantId, CancellationToken ct = default)
    {
        var docs = await _campaigns.Find(x => x.TenantId == tenantId).SortByDescending(x => x.UpdatedAt).ToListAsync(ct);
        return docs.Select(ToContract).ToList();
    }

    public async Task<ConnectCampaignContract> UpsertCampaignAsync(ConnectCampaignContract campaign, CancellationToken ct = default)
    {
        var doc = new ConnectCampaignDocument
        {
            Id = campaign.Id,
            TenantId = campaign.TenantId,
            Name = campaign.Name,
            Channel = campaign.Channel,
            TemplateId = campaign.TemplateId,
            PublishedWorkflowAgentId = campaign.PublishedWorkflowAgentId,
            ScheduledAt = campaign.ScheduledAt,
            Enabled = campaign.Enabled,
            CreatedAt = campaign.CreatedAt,
            UpdatedAt = campaign.UpdatedAt,
            UpdatedBy = campaign.UpdatedBy
        };

        await _campaigns.ReplaceOneAsync(
            x => x.TenantId == campaign.TenantId && x.Id == campaign.Id,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return ToContract(doc);
    }

    public async Task<IReadOnlyList<ConnectInboxMessageContract>> GetInboxAsync(string tenantId, int limit, CancellationToken ct = default)
    {
        var bounded = Math.Clamp(limit, 1, 500);
        var docs = await _inbox.Find(x => x.TenantId == tenantId)
            .SortByDescending(x => x.UpdatedAt)
            .Limit(bounded)
            .ToListAsync(ct);
        return docs.Select(ToContract).ToList();
    }

    public async Task<ConnectInboxMessageContract> CreateInboxMessageAsync(ConnectInboxMessageContract message, CancellationToken ct = default)
    {
        var doc = new ConnectInboxDocument
        {
            Id = message.Id,
            TenantId = message.TenantId,
            Channel = message.Channel,
            Recipient = message.Recipient,
            Content = message.Content,
            CampaignId = message.CampaignId,
            TemplateId = message.TemplateId,
            Status = message.Status,
            LastError = message.LastError,
            CreatedAt = message.CreatedAt,
            UpdatedAt = message.UpdatedAt,
            UpdatedBy = message.UpdatedBy
        };

        await _inbox.InsertOneAsync(doc, cancellationToken: ct);
        return ToContract(doc);
    }

    public async Task<ConnectInboxMessageContract?> UpdateMessageStatusAsync(string tenantId, string messageId, ConnectOperationalStatus status, string updatedBy, string? lastError, CancellationToken ct = default)
    {
        var update = Builders<ConnectInboxDocument>.Update
            .Set(x => x.Status, status)
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow)
            .Set(x => x.UpdatedBy, updatedBy)
            .Set(x => x.LastError, lastError);

        var updated = await _inbox.FindOneAndUpdateAsync(
            x => x.TenantId == tenantId && x.Id == messageId,
            update,
            new FindOneAndUpdateOptions<ConnectInboxDocument> { ReturnDocument = ReturnDocument.After },
            ct);

        return updated is null ? null : ToContract(updated);
    }

    private static ConnectTemplateContract ToContract(ConnectTemplateDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        Name = doc.Name,
        Channel = doc.Channel,
        Body = doc.Body,
        PublishedWorkflowAgentId = doc.PublishedWorkflowAgentId,
        PublishedWorkflowAgentName = doc.PublishedWorkflowAgentName,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private static ConnectCampaignContract ToContract(ConnectCampaignDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        Name = doc.Name,
        Channel = doc.Channel,
        TemplateId = doc.TemplateId,
        PublishedWorkflowAgentId = doc.PublishedWorkflowAgentId,
        ScheduledAt = doc.ScheduledAt,
        Enabled = doc.Enabled,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private static ConnectInboxMessageContract ToContract(ConnectInboxDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        Channel = doc.Channel,
        Recipient = doc.Recipient,
        Content = doc.Content,
        CampaignId = doc.CampaignId,
        TemplateId = doc.TemplateId,
        Status = doc.Status,
        LastError = doc.LastError,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private sealed class ConnectTemplateDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? PublishedWorkflowAgentId { get; set; }
        public string? PublishedWorkflowAgentName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    private sealed class ConnectCampaignDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string TemplateId { get; set; } = string.Empty;
        public string? PublishedWorkflowAgentId { get; set; }
        public DateTimeOffset ScheduledAt { get; set; }
        public bool Enabled { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    private sealed class ConnectInboxDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? CampaignId { get; set; }
        public string? TemplateId { get; set; }
        public ConnectOperationalStatus Status { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }
}
