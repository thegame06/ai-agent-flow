using System.Collections.Concurrent;
using AgentFlow.Abstractions;

namespace AgentFlow.Api.TestStudio;

public interface ITestStudioSessionStore
{
    TestStudioSession Create(string tenantId, AgentRuntimeKind runtimeKind, string correlationId, string mode, string? channelType);
    TestStudioSession? Get(string tenantId, string sessionId);
    IReadOnlyList<TestStudioEvent> GetTimeline(string tenantId, string sessionId);
    void AppendEvent(string tenantId, string sessionId, TestStudioEvent evt);
    void AddArtifact(string tenantId, string sessionId, TestStudioArtifact artifact);
    IReadOnlyList<TestStudioArtifact> GetArtifacts(string tenantId, string sessionId);
    bool Close(string tenantId, string sessionId);
    TestStudioSession? FindByCorrelationId(string tenantId, string correlationId, AgentRuntimeKind runtimeKind);
    bool UpdateCorrelationId(string tenantId, string sessionId, string correlationId);
    bool TryConsumeMessageQuota(string tenantId, string sessionId, int maxMessagesPerMinute);
    IReadOnlyList<TestStudioSession> ListByRuntime(string tenantId, AgentRuntimeKind? runtimeKind = null);
    void SaveArtifactContent(string tenantId, string sessionId, string attachmentRef, byte[] content);
    (byte[] Content, string ContentType, string Name)? GetArtifactContent(string tenantId, string sessionId, string attachmentRef);
}

public sealed class InMemoryTestStudioSessionStore : ITestStudioSessionStore
{
    private readonly ConcurrentDictionary<string, TestStudioSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TestStudioEvent>> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TestStudioArtifact>> _artifacts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _messageRate = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte[]> _artifactContent = new(StringComparer.OrdinalIgnoreCase);

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
        _sessions[BuildKey(tenantId, session.TestSessionId)] = session;
        _events.TryAdd(BuildKey(tenantId, session.TestSessionId), new ConcurrentQueue<TestStudioEvent>());
        _artifacts.TryAdd(BuildKey(tenantId, session.TestSessionId), new ConcurrentQueue<TestStudioArtifact>());
        _messageRate.TryAdd(BuildKey(tenantId, session.TestSessionId), new ConcurrentQueue<DateTimeOffset>());
        return session;
    }

    public TestStudioSession? Get(string tenantId, string sessionId)
        => _sessions.TryGetValue(BuildKey(tenantId, sessionId), out var session) ? session : null;

    public IReadOnlyList<TestStudioEvent> GetTimeline(string tenantId, string sessionId)
        => _events.TryGetValue(BuildKey(tenantId, sessionId), out var queue)
            ? queue.ToArray().OrderBy(x => x.Timestamp).ToList()
            : [];

    public void AppendEvent(string tenantId, string sessionId, TestStudioEvent evt)
    {
        if (_events.TryGetValue(BuildKey(tenantId, sessionId), out var queue))
        {
            queue.Enqueue(evt);
        }
    }

    public void AddArtifact(string tenantId, string sessionId, TestStudioArtifact artifact)
    {
        if (_artifacts.TryGetValue(BuildKey(tenantId, sessionId), out var queue))
        {
            queue.Enqueue(artifact);
        }
    }

    public IReadOnlyList<TestStudioArtifact> GetArtifacts(string tenantId, string sessionId)
        => _artifacts.TryGetValue(BuildKey(tenantId, sessionId), out var queue)
            ? queue.ToArray().ToList()
            : [];

    public bool Close(string tenantId, string sessionId)
    {
        var key = BuildKey(tenantId, sessionId);
        if (!_sessions.TryGetValue(key, out var session)) return false;
        session.Status = "completed";
        session.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public TestStudioSession? FindByCorrelationId(string tenantId, string correlationId, AgentRuntimeKind runtimeKind)
    {
        foreach (var kv in _sessions)
        {
            var session = kv.Value;
            if (!string.Equals(session.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(session.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase)) continue;
            if (session.RuntimeKind != runtimeKind) continue;
            return session;
        }

        return null;
    }

    public bool UpdateCorrelationId(string tenantId, string sessionId, string correlationId)
    {
        var key = BuildKey(tenantId, sessionId);
        if (!_sessions.TryGetValue(key, out var session)) return false;
        session.CorrelationId = correlationId;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public bool TryConsumeMessageQuota(string tenantId, string sessionId, int maxMessagesPerMinute)
    {
        var key = BuildKey(tenantId, sessionId);
        if (!_messageRate.TryGetValue(key, out var queue))
            return false;

        var now = DateTimeOffset.UtcNow;
        var threshold = now.AddMinutes(-1);

        while (queue.TryPeek(out var stamp) && stamp < threshold)
            queue.TryDequeue(out _);

        if (queue.Count >= maxMessagesPerMinute)
            return false;

        queue.Enqueue(now);
        return true;
    }

    public IReadOnlyList<TestStudioSession> ListByRuntime(string tenantId, AgentRuntimeKind? runtimeKind = null)
    {
        var list = new List<TestStudioSession>();
        foreach (var session in _sessions.Values)
        {
            if (!string.Equals(session.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) continue;
            if (runtimeKind.HasValue && session.RuntimeKind != runtimeKind.Value) continue;
            list.Add(session);
        }

        return list.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public void SaveArtifactContent(string tenantId, string sessionId, string attachmentRef, byte[] content)
        => _artifactContent[BuildArtifactKey(tenantId, sessionId, attachmentRef)] = content;

    public (byte[] Content, string ContentType, string Name)? GetArtifactContent(string tenantId, string sessionId, string attachmentRef)
    {
        if (!_artifactContent.TryGetValue(BuildArtifactKey(tenantId, sessionId, attachmentRef), out var content))
            return null;
        var artifact = GetArtifacts(tenantId, sessionId).FirstOrDefault(x => x.AttachmentRef == attachmentRef);
        if (artifact is null) return null;
        return (content, artifact.ContentType, artifact.Name);
    }

    private static string BuildKey(string tenantId, string sessionId) => $"{tenantId}:{sessionId}";
    private static string BuildArtifactKey(string tenantId, string sessionId, string attachmentRef) => $"{tenantId}:{sessionId}:{attachmentRef}";
}

public sealed record TestStudioSession
{
    public required string TestSessionId { get; init; }
    public required string TenantId { get; init; }
    public required AgentRuntimeKind RuntimeKind { get; init; }
    public required string CorrelationId { get; set; }
    public required string Mode { get; init; }
    public string? ChannelType { get; init; }
    public string? AgentId { get; set; }
    public string? ChannelId { get; set; }
    public string? ThreadId { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record TestStudioEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string Stage { get; init; }
    public required string Direction { get; init; }
    public required string PayloadType { get; init; }
    public required string Status { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public required string CorrelationId { get; init; }
}

public sealed record TestStudioArtifact
{
    public required string AttachmentRef { get; init; }
    public required string Name { get; init; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }
    public string Status { get; init; } = "registered";
}
