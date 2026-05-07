using AgentFlow.Abstractions.Workflow;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Workflow;

public interface IWorkflowStudioStore
{
    Task<IReadOnlyList<WorkflowActivityCatalogContract>> GetActivitiesAsync(CancellationToken ct = default);
    Task<WorkflowActivityCatalogContract> UpsertActivityAsync(WorkflowActivityCatalogContract activity, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowEventCatalogContract>> GetEventsAsync(CancellationToken ct = default);
    Task<WorkflowEventCatalogContract> UpsertEventAsync(WorkflowEventCatalogContract evt, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowTemplateContract>> GetTemplatesAsync(string tenantId, CancellationToken ct = default);
    Task<WorkflowTemplateContract> UpsertTemplateAsync(WorkflowTemplateContract template, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowDefinitionContract>> GetDefinitionsAsync(string tenantId, CancellationToken ct = default);
    Task<WorkflowDefinitionContract?> GetDefinitionAsync(string tenantId, string workflowId, CancellationToken ct = default);
    Task<WorkflowDefinitionContract> UpsertDefinitionAsync(WorkflowDefinitionContract definition, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowExecutionContract>> GetExecutionsAsync(string tenantId, int limit, CancellationToken ct = default);
    Task<WorkflowExecutionContract> CreateExecutionAsync(WorkflowExecutionContract execution, CancellationToken ct = default);
    Task<WorkflowExecutionContract?> UpdateExecutionStatusAsync(string tenantId, string executionId, WorkflowExecutionStatus status, string? error, CancellationToken ct = default);
    Task<WorkflowExecutionContract?> UpdateExecutionContextAsync(string tenantId, string executionId, string contextJson, CancellationToken ct = default);
    Task<WorkflowExecutionStepLogContract> CreateStepLogAsync(WorkflowExecutionStepLogContract step, CancellationToken ct = default);
    Task<WorkflowExecutionStepLogContract?> CompleteStepLogAsync(string tenantId, string stepId, WorkflowExecutionStatus status, string? outputJson, string? error, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecutionStepLogContract>> GetStepLogsAsync(string tenantId, string executionId, CancellationToken ct = default);
}

public sealed class MongoWorkflowStudioStore : IWorkflowStudioStore
{
    private readonly IMongoCollection<WorkflowActivityCatalogDocument> _activities;
    private readonly IMongoCollection<WorkflowEventCatalogDocument> _events;
    private readonly IMongoCollection<WorkflowTemplateDocument> _templates;
    private readonly IMongoCollection<WorkflowDefinitionDocument> _definitions;
    private readonly IMongoCollection<WorkflowExecutionDocument> _executions;
    private readonly IMongoCollection<WorkflowExecutionStepLogDocument> _executionSteps;

    public MongoWorkflowStudioStore(IMongoDatabase db)
    {
        _activities = db.GetCollection<WorkflowActivityCatalogDocument>("workflow_catalog_activities");
        _events = db.GetCollection<WorkflowEventCatalogDocument>("workflow_catalog_events");
        _templates = db.GetCollection<WorkflowTemplateDocument>("workflow_templates");
        _definitions = db.GetCollection<WorkflowDefinitionDocument>("workflow_definitions");
        _executions = db.GetCollection<WorkflowExecutionDocument>("workflow_executions");
        _executionSteps = db.GetCollection<WorkflowExecutionStepLogDocument>("workflow_execution_steps");
    }

    public async Task<IReadOnlyList<WorkflowActivityCatalogContract>> GetActivitiesAsync(CancellationToken ct = default)
        => (await _activities.Find(_ => true).SortBy(x => x.Category).ThenBy(x => x.DisplayName).ToListAsync(ct)).Select(ToContract).ToList();

    public async Task<WorkflowActivityCatalogContract> UpsertActivityAsync(WorkflowActivityCatalogContract activity, CancellationToken ct = default)
    {
        var doc = new WorkflowActivityCatalogDocument
        {
            TypeName = activity.TypeName,
            DisplayName = activity.DisplayName,
            Category = activity.Category,
            Description = activity.Description,
            InputSchema = activity.InputSchema,
            OutputSchema = activity.OutputSchema,
            UpdatedAt = activity.UpdatedAt,
            UpdatedBy = activity.UpdatedBy
        };

        await _activities.ReplaceOneAsync(x => x.TypeName == doc.TypeName, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return ToContract(doc);
    }

    public async Task<IReadOnlyList<WorkflowEventCatalogContract>> GetEventsAsync(CancellationToken ct = default)
        => (await _events.Find(_ => true).SortBy(x => x.Entity).ThenBy(x => x.EventName).ToListAsync(ct)).Select(ToContract).ToList();

    public async Task<WorkflowEventCatalogContract> UpsertEventAsync(WorkflowEventCatalogContract evt, CancellationToken ct = default)
    {
        var doc = new WorkflowEventCatalogDocument
        {
            EventName = evt.EventName,
            DisplayName = evt.DisplayName,
            Entity = evt.Entity,
            Description = evt.Description,
            UpdatedAt = evt.UpdatedAt,
            UpdatedBy = evt.UpdatedBy
        };

        await _events.ReplaceOneAsync(x => x.EventName == doc.EventName, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return ToContract(doc);
    }

    public async Task<IReadOnlyList<WorkflowTemplateContract>> GetTemplatesAsync(string tenantId, CancellationToken ct = default)
        => (await _templates.Find(x => x.TenantId == tenantId).SortByDescending(x => x.UpdatedAt).ToListAsync(ct)).Select(ToContract).ToList();

    public async Task<WorkflowTemplateContract> UpsertTemplateAsync(WorkflowTemplateContract template, CancellationToken ct = default)
    {
        var doc = new WorkflowTemplateDocument
        {
            Id = template.Id,
            TenantId = template.TenantId,
            Name = template.Name,
            Description = template.Description,
            TriggerEventName = template.TriggerEventName,
            DefinitionJson = template.DefinitionJson,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            UpdatedBy = template.UpdatedBy
        };

        await _templates.ReplaceOneAsync(x => x.TenantId == doc.TenantId && x.Id == doc.Id, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return ToContract(doc);
    }

    public async Task<IReadOnlyList<WorkflowDefinitionContract>> GetDefinitionsAsync(string tenantId, CancellationToken ct = default)
        => (await _definitions.Find(x => x.TenantId == tenantId).SortByDescending(x => x.UpdatedAt).ToListAsync(ct)).Select(ToContract).ToList();

    public async Task<WorkflowDefinitionContract?> GetDefinitionAsync(string tenantId, string workflowId, CancellationToken ct = default)
    {
        var doc = await _definitions.Find(x => x.TenantId == tenantId && x.Id == workflowId).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToContract(doc);
    }

    public async Task<WorkflowDefinitionContract> UpsertDefinitionAsync(WorkflowDefinitionContract definition, CancellationToken ct = default)
    {
        var doc = new WorkflowDefinitionDocument
        {
            Id = definition.Id,
            TenantId = definition.TenantId,
            Name = definition.Name,
            TriggerEventName = definition.TriggerEventName,
            Version = definition.Version,
            Status = definition.Status,
            DefinitionJson = definition.DefinitionJson,
            Metadata = definition.Metadata,
            CreatedAt = definition.CreatedAt,
            UpdatedAt = definition.UpdatedAt,
            UpdatedBy = definition.UpdatedBy
        };

        await _definitions.ReplaceOneAsync(x => x.TenantId == doc.TenantId && x.Id == doc.Id, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return ToContract(doc);
    }

    public async Task<IReadOnlyList<WorkflowExecutionContract>> GetExecutionsAsync(string tenantId, int limit, CancellationToken ct = default)
    {
        var bounded = Math.Clamp(limit, 1, 500);
        var docs = await _executions.Find(x => x.TenantId == tenantId).SortByDescending(x => x.UpdatedAt).Limit(bounded).ToListAsync(ct);
        return docs.Select(ToContract).ToList();
    }

    public async Task<WorkflowExecutionContract> CreateExecutionAsync(WorkflowExecutionContract execution, CancellationToken ct = default)
    {
        var doc = new WorkflowExecutionDocument
        {
            Id = execution.Id,
            TenantId = execution.TenantId,
            WorkflowDefinitionId = execution.WorkflowDefinitionId,
            TriggerEventName = execution.TriggerEventName,
            CorrelationId = execution.CorrelationId,
            PayloadJson = execution.PayloadJson,
            ContextJson = execution.ContextJson,
            Status = execution.Status,
            Error = execution.Error,
            CreatedAt = execution.CreatedAt,
            UpdatedAt = execution.UpdatedAt,
            RequestedBy = execution.RequestedBy
        };

        await _executions.InsertOneAsync(doc, cancellationToken: ct);
        return ToContract(doc);
    }

    public async Task<WorkflowExecutionContract?> UpdateExecutionStatusAsync(string tenantId, string executionId, WorkflowExecutionStatus status, string? error, CancellationToken ct = default)
    {
        var update = Builders<WorkflowExecutionDocument>.Update
            .Set(x => x.Status, status)
            .Set(x => x.Error, error)
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow);

        var updated = await _executions.FindOneAndUpdateAsync(
            x => x.TenantId == tenantId && x.Id == executionId,
            update,
            new FindOneAndUpdateOptions<WorkflowExecutionDocument> { ReturnDocument = ReturnDocument.After },
            ct);

        return updated is null ? null : ToContract(updated);
    }

    public async Task<WorkflowExecutionContract?> UpdateExecutionContextAsync(string tenantId, string executionId, string contextJson, CancellationToken ct = default)
    {
        var update = Builders<WorkflowExecutionDocument>.Update
            .Set(x => x.ContextJson, contextJson)
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow);

        var updated = await _executions.FindOneAndUpdateAsync(
            x => x.TenantId == tenantId && x.Id == executionId,
            update,
            new FindOneAndUpdateOptions<WorkflowExecutionDocument> { ReturnDocument = ReturnDocument.After },
            ct);

        return updated is null ? null : ToContract(updated);
    }

    public async Task<WorkflowExecutionStepLogContract> CreateStepLogAsync(WorkflowExecutionStepLogContract step, CancellationToken ct = default)
    {
        var doc = new WorkflowExecutionStepLogDocument
        {
            Id = step.Id,
            TenantId = step.TenantId,
            ExecutionId = step.ExecutionId,
            ActivityType = step.ActivityType,
            ActivityName = step.ActivityName,
            Status = step.Status,
            InputJson = step.InputJson,
            OutputJson = step.OutputJson,
            Error = step.Error,
            StartedAt = step.StartedAt,
            CompletedAt = step.CompletedAt
        };
        await _executionSteps.InsertOneAsync(doc, cancellationToken: ct);
        return ToContract(doc);
    }

    public async Task<WorkflowExecutionStepLogContract?> CompleteStepLogAsync(string tenantId, string stepId, WorkflowExecutionStatus status, string? outputJson, string? error, CancellationToken ct = default)
    {
        var update = Builders<WorkflowExecutionStepLogDocument>.Update
            .Set(x => x.Status, status)
            .Set(x => x.OutputJson, outputJson)
            .Set(x => x.Error, error)
            .Set(x => x.CompletedAt, DateTimeOffset.UtcNow);

        var updated = await _executionSteps.FindOneAndUpdateAsync(
            x => x.TenantId == tenantId && x.Id == stepId,
            update,
            new FindOneAndUpdateOptions<WorkflowExecutionStepLogDocument> { ReturnDocument = ReturnDocument.After },
            ct);
        return updated is null ? null : ToContract(updated);
    }

    public async Task<IReadOnlyList<WorkflowExecutionStepLogContract>> GetStepLogsAsync(string tenantId, string executionId, CancellationToken ct = default)
    {
        var docs = await _executionSteps.Find(x => x.TenantId == tenantId && x.ExecutionId == executionId)
            .SortBy(x => x.StartedAt)
            .ToListAsync(ct);
        return docs.Select(ToContract).ToList();
    }

    private static WorkflowActivityCatalogContract ToContract(WorkflowActivityCatalogDocument doc) => new()
    {
        TypeName = doc.TypeName,
        DisplayName = doc.DisplayName,
        Category = doc.Category,
        Description = doc.Description,
        InputSchema = doc.InputSchema,
        OutputSchema = doc.OutputSchema,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private static WorkflowEventCatalogContract ToContract(WorkflowEventCatalogDocument doc) => new()
    {
        EventName = doc.EventName,
        DisplayName = doc.DisplayName,
        Entity = doc.Entity,
        Description = doc.Description,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private static WorkflowTemplateContract ToContract(WorkflowTemplateDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        Name = doc.Name,
        Description = doc.Description,
        TriggerEventName = doc.TriggerEventName,
        DefinitionJson = doc.DefinitionJson,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private static WorkflowDefinitionContract ToContract(WorkflowDefinitionDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        Name = doc.Name,
        TriggerEventName = doc.TriggerEventName,
        Version = doc.Version,
        Status = doc.Status,
        DefinitionJson = doc.DefinitionJson,
        Metadata = doc.Metadata,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private static WorkflowExecutionContract ToContract(WorkflowExecutionDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        WorkflowDefinitionId = doc.WorkflowDefinitionId,
        TriggerEventName = doc.TriggerEventName,
        CorrelationId = doc.CorrelationId,
        PayloadJson = doc.PayloadJson,
        ContextJson = doc.ContextJson,
        Status = doc.Status,
        Error = doc.Error,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
        RequestedBy = doc.RequestedBy
    };

    private static WorkflowExecutionStepLogContract ToContract(WorkflowExecutionStepLogDocument doc) => new()
    {
        Id = doc.Id,
        TenantId = doc.TenantId,
        ExecutionId = doc.ExecutionId,
        ActivityType = doc.ActivityType,
        ActivityName = doc.ActivityName,
        Status = doc.Status,
        InputJson = doc.InputJson,
        OutputJson = doc.OutputJson,
        Error = doc.Error,
        StartedAt = doc.StartedAt,
        CompletedAt = doc.CompletedAt
    };

    private sealed class WorkflowActivityCatalogDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string TypeName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, string> InputSchema { get; set; } = new();
        public Dictionary<string, string> OutputSchema { get; set; } = new();
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    private sealed class WorkflowEventCatalogDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string EventName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Entity { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    private sealed class WorkflowTemplateDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TriggerEventName { get; set; } = string.Empty;
        public string DefinitionJson { get; set; } = "{}";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    private sealed class WorkflowDefinitionDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TriggerEventName { get; set; } = string.Empty;
        public int Version { get; set; }
        public WorkflowDefinitionStatus Status { get; set; }
        public string DefinitionJson { get; set; } = "{}";
        public Dictionary<string, string> Metadata { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    private sealed class WorkflowExecutionDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string WorkflowDefinitionId { get; set; } = string.Empty;
        public string TriggerEventName { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = "{}";
        public string ContextJson { get; set; } = "{}";
        public WorkflowExecutionStatus Status { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string RequestedBy { get; set; } = string.Empty;
    }

    private sealed class WorkflowExecutionStepLogDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ExecutionId { get; set; } = string.Empty;
        public string ActivityType { get; set; } = string.Empty;
        public string ActivityName { get; set; } = string.Empty;
        public WorkflowExecutionStatus Status { get; set; }
        public string InputJson { get; set; } = "{}";
        public string? OutputJson { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }
}
