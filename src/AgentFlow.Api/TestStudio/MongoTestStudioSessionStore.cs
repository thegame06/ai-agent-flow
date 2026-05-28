using AgentFlow.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.TestStudio;

public sealed class MongoTestStudioSessionStore : ITestStudioSessionStore
{
    private readonly IMongoCollection<TestStudioSessionDocument> _sessions;
    private readonly IMongoCollection<TestStudioEventDocument> _events;
    private readonly IMongoCollection<TestStudioArtifactDocument> _artifacts;
    private readonly IMongoCollection<TestStudioArtifactContentDocument> _artifactContent;
    private readonly IMongoCollection<TestStudioRateDocument> _rate;

    public MongoTestStudioSessionStore(IMongoDatabase database)
    {
        _sessions = database.GetCollection<TestStudioSessionDocument>("teststudio_sessions");
        _events = database.GetCollection<TestStudioEventDocument>("teststudio_events");
        _artifacts = database.GetCollection<TestStudioArtifactDocument>("teststudio_artifacts");
        _artifactContent = database.GetCollection<TestStudioArtifactContentDocument>("teststudio_artifact_content");
        _rate = database.GetCollection<TestStudioRateDocument>("teststudio_rate");

        _sessions.Indexes.CreateOne(new CreateIndexModel<TestStudioSessionDocument>(
            Builders<TestStudioSessionDocument>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.TestSessionId),
            new CreateIndexOptions { Unique = true }));

        _sessions.Indexes.CreateOne(new CreateIndexModel<TestStudioSessionDocument>(
            Builders<TestStudioSessionDocument>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.RuntimeKind)
                .Ascending(x => x.CreatedAt)));

        _sessions.Indexes.CreateOne(new CreateIndexModel<TestStudioSessionDocument>(
            Builders<TestStudioSessionDocument>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.RuntimeKind)
                .Ascending(x => x.CorrelationId)));

        _events.Indexes.CreateOne(new CreateIndexModel<TestStudioEventDocument>(
            Builders<TestStudioEventDocument>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.TestSessionId)
                .Ascending(x => x.Timestamp)));

        _artifacts.Indexes.CreateOne(new CreateIndexModel<TestStudioArtifactDocument>(
            Builders<TestStudioArtifactDocument>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.TestSessionId)));

        _artifactContent.Indexes.CreateOne(new CreateIndexModel<TestStudioArtifactContentDocument>(
            Builders<TestStudioArtifactContentDocument>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.TestSessionId)
                .Ascending(x => x.AttachmentRef),
            new CreateIndexOptions { Unique = true }));

        _rate.Indexes.CreateOne(new CreateIndexModel<TestStudioRateDocument>(
            Builders<TestStudioRateDocument>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.TestSessionId)
                .Ascending(x => x.WindowStart),
            new CreateIndexOptions { Unique = true }));

        _rate.Indexes.CreateOne(new CreateIndexModel<TestStudioRateDocument>(
            Builders<TestStudioRateDocument>.IndexKeys
                .Ascending(x => x.WindowStart),
            new CreateIndexOptions { ExpireAfter = TimeSpan.FromHours(24) }));
    }

    public TestStudioSession Create(string tenantId, AgentRuntimeKind runtimeKind, string correlationId, string mode, string? channelType)
    {
        var session = new TestStudioSession
        {
            TestSessionId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            RuntimeKind = runtimeKind,
            CorrelationId = correlationId,
            Mode = mode,
            ChannelType = channelType,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _sessions.InsertOne(ToDocument(session));
        return session;
    }

    public TestStudioSession? Get(string tenantId, string sessionId)
    {
        var doc = _sessions.Find(x => x.TenantId == tenantId && x.TestSessionId == sessionId).FirstOrDefault();
        return doc is null ? null : ToModel(doc);
    }

    public IReadOnlyList<TestStudioEvent> GetTimeline(string tenantId, string sessionId)
    {
        var docs = _events.Find(x => x.TenantId == tenantId && x.TestSessionId == sessionId)
            .SortBy(x => x.Timestamp)
            .ToList();
        return docs.Select(ToModel).ToList();
    }

    public void AppendEvent(string tenantId, string sessionId, TestStudioEvent evt)
    {
        _events.InsertOne(new TestStudioEventDocument
        {
            Id = ObjectId.GenerateNewId(),
            TenantId = tenantId,
            TestSessionId = sessionId,
            Timestamp = evt.Timestamp,
            Stage = evt.Stage,
            Direction = evt.Direction,
            PayloadType = evt.PayloadType,
            Status = evt.Status,
            ErrorCode = evt.ErrorCode,
            Message = evt.Message,
            CorrelationId = evt.CorrelationId
        });
    }

    public void AddArtifact(string tenantId, string sessionId, TestStudioArtifact artifact)
    {
        _artifacts.InsertOne(new TestStudioArtifactDocument
        {
            Id = ObjectId.GenerateNewId(),
            TenantId = tenantId,
            TestSessionId = sessionId,
            AttachmentRef = artifact.AttachmentRef,
            Name = artifact.Name,
            ContentType = artifact.ContentType,
            SizeBytes = artifact.SizeBytes,
            Status = artifact.Status
        });
    }

    public IReadOnlyList<TestStudioArtifact> GetArtifacts(string tenantId, string sessionId)
    {
        var docs = _artifacts.Find(x => x.TenantId == tenantId && x.TestSessionId == sessionId).ToList();
        return docs.Select(ToModel).ToList();
    }

    public bool Close(string tenantId, string sessionId)
    {
        var update = Builders<TestStudioSessionDocument>.Update
            .Set(x => x.Status, "completed")
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow);
        var result = _sessions.UpdateOne(x => x.TenantId == tenantId && x.TestSessionId == sessionId, update);
        return result.ModifiedCount > 0;
    }

    public TestStudioSession? FindByCorrelationId(string tenantId, string correlationId, AgentRuntimeKind runtimeKind)
    {
        var doc = _sessions.Find(x =>
            x.TenantId == tenantId &&
            x.CorrelationId == correlationId &&
            x.RuntimeKind == runtimeKind.ToString()).FirstOrDefault();
        return doc is null ? null : ToModel(doc);
    }

    public bool UpdateCorrelationId(string tenantId, string sessionId, string correlationId)
    {
        var update = Builders<TestStudioSessionDocument>.Update
            .Set(x => x.CorrelationId, correlationId)
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow);
        var result = _sessions.UpdateOne(x => x.TenantId == tenantId && x.TestSessionId == sessionId, update);
        return result.ModifiedCount > 0;
    }

    public bool TryConsumeMessageQuota(string tenantId, string sessionId, int maxMessagesPerMinute)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        var filter = Builders<TestStudioRateDocument>.Filter.And(
            Builders<TestStudioRateDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<TestStudioRateDocument>.Filter.Eq(x => x.TestSessionId, sessionId),
            Builders<TestStudioRateDocument>.Filter.Eq(x => x.WindowStart, windowStart),
            Builders<TestStudioRateDocument>.Filter.Lt(x => x.Count, maxMessagesPerMinute));

        var update = Builders<TestStudioRateDocument>.Update
            .SetOnInsert(x => x.Id, ObjectId.GenerateNewId())
            .SetOnInsert(x => x.TenantId, tenantId)
            .SetOnInsert(x => x.TestSessionId, sessionId)
            .SetOnInsert(x => x.WindowStart, windowStart)
            .Inc(x => x.Count, 1)
            .Set(x => x.UpdatedAt, now);

        var options = new FindOneAndUpdateOptions<TestStudioRateDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        var updated = _rate.FindOneAndUpdate(filter, update, options);
        return updated is not null && updated.Count <= maxMessagesPerMinute;
    }

    public IReadOnlyList<TestStudioSession> ListByRuntime(string tenantId, AgentRuntimeKind? runtimeKind = null)
    {
        var filter = runtimeKind.HasValue
            ? Builders<TestStudioSessionDocument>.Filter.Where(x => x.TenantId == tenantId && x.RuntimeKind == runtimeKind.Value.ToString())
            : Builders<TestStudioSessionDocument>.Filter.Where(x => x.TenantId == tenantId);
        var docs = _sessions.Find(filter).SortByDescending(x => x.CreatedAt).ToList();
        return docs.Select(ToModel).ToList();
    }

    public void SaveArtifactContent(string tenantId, string sessionId, string attachmentRef, byte[] content)
    {
        var update = Builders<TestStudioArtifactContentDocument>.Update
            .SetOnInsert(x => x.Id, ObjectId.GenerateNewId())
            .SetOnInsert(x => x.TenantId, tenantId)
            .SetOnInsert(x => x.TestSessionId, sessionId)
            .SetOnInsert(x => x.AttachmentRef, attachmentRef)
            .Set(x => x.Content, content)
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow);
        _artifactContent.UpdateOne(
            x => x.TenantId == tenantId && x.TestSessionId == sessionId && x.AttachmentRef == attachmentRef,
            update,
            new UpdateOptions { IsUpsert = true });
    }

    public (byte[] Content, string ContentType, string Name)? GetArtifactContent(string tenantId, string sessionId, string attachmentRef)
    {
        var content = _artifactContent.Find(x =>
            x.TenantId == tenantId &&
            x.TestSessionId == sessionId &&
            x.AttachmentRef == attachmentRef).FirstOrDefault();
        if (content is null) return null;
        var artifact = _artifacts.Find(x =>
            x.TenantId == tenantId &&
            x.TestSessionId == sessionId &&
            x.AttachmentRef == attachmentRef).FirstOrDefault();
        if (artifact is null) return null;
        return (content.Content, artifact.ContentType, artifact.Name);
    }

    private static TestStudioSessionDocument ToDocument(TestStudioSession model) => new()
    {
        Id = ObjectId.GenerateNewId(),
        TestSessionId = model.TestSessionId,
        TenantId = model.TenantId,
        RuntimeKind = model.RuntimeKind.ToString(),
        CorrelationId = model.CorrelationId,
        Mode = model.Mode,
        ChannelType = model.ChannelType,
        AgentId = model.AgentId,
        ChannelId = model.ChannelId,
        ThreadId = model.ThreadId,
        Status = model.Status,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt
    };

    private static TestStudioSession ToModel(TestStudioSessionDocument doc) => new()
    {
        TestSessionId = doc.TestSessionId,
        TenantId = doc.TenantId,
        RuntimeKind = Enum.TryParse<AgentRuntimeKind>(doc.RuntimeKind, true, out var kind) ? kind : AgentRuntimeKind.Text,
        CorrelationId = doc.CorrelationId,
        Mode = doc.Mode,
        ChannelType = doc.ChannelType,
        AgentId = doc.AgentId,
        ChannelId = doc.ChannelId,
        ThreadId = doc.ThreadId,
        Status = doc.Status,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt
    };

    private static TestStudioEvent ToModel(TestStudioEventDocument doc) => new()
    {
        Timestamp = doc.Timestamp,
        Stage = doc.Stage,
        Direction = doc.Direction,
        PayloadType = doc.PayloadType,
        Status = doc.Status,
        ErrorCode = doc.ErrorCode,
        Message = doc.Message,
        CorrelationId = doc.CorrelationId
    };

    private static TestStudioArtifact ToModel(TestStudioArtifactDocument doc) => new()
    {
        AttachmentRef = doc.AttachmentRef,
        Name = doc.Name,
        ContentType = doc.ContentType,
        SizeBytes = doc.SizeBytes,
        Status = doc.Status
    };

    private sealed class TestStudioSessionDocument
    {
        [BsonId] public ObjectId Id { get; set; }
        public string TestSessionId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string RuntimeKind { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string Mode { get; set; } = "direct";
        public string? ChannelType { get; set; }
        public string? AgentId { get; set; }
        public string? ChannelId { get; set; }
        public string? ThreadId { get; set; }
        public string Status { get; set; } = "active";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class TestStudioEventDocument
    {
        [BsonId] public ObjectId Id { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string TestSessionId { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public string PayloadType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string? Message { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
    }

    private sealed class TestStudioArtifactDocument
    {
        [BsonId] public ObjectId Id { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string TestSessionId { get; set; } = string.Empty;
        public string AttachmentRef { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Status { get; set; } = "registered";
    }

    private sealed class TestStudioRateDocument
    {
        [BsonId] public ObjectId Id { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string TestSessionId { get; set; } = string.Empty;
        public DateTimeOffset WindowStart { get; set; }
        public int Count { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class TestStudioArtifactContentDocument
    {
        [BsonId] public ObjectId Id { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string TestSessionId { get; set; } = string.Empty;
        public string AttachmentRef { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
