using AgentFlow.Abstractions;
using AgentFlow.Api.TestStudio;

namespace AgentFlow.Tests.Unit.TestStudio;

public sealed class InMemoryTestStudioSessionStoreTests
{
    [Fact]
    public void Create_AppendEvent_AndClose_Works()
    {
        var store = new InMemoryTestStudioSessionStore();
        var session = store.Create("tenant-a", AgentRuntimeKind.Text, "corr-1", "direct", null);

        store.AppendEvent("tenant-a", session.TestSessionId, new TestStudioEvent
        {
            Stage = "input",
            Direction = "inbound",
            PayloadType = "text",
            Status = "accepted",
            CorrelationId = "corr-1",
            Message = "hello"
        });

        var timeline = store.GetTimeline("tenant-a", session.TestSessionId);
        Assert.Single(timeline);
        Assert.Equal("input", timeline[0].Stage);

        var closed = store.Close("tenant-a", session.TestSessionId);
        Assert.True(closed);
        Assert.Equal("completed", store.Get("tenant-a", session.TestSessionId)!.Status);
    }

    [Fact]
    public void FindByCorrelationId_FindsMatchingVoiceSession()
    {
        var store = new InMemoryTestStudioSessionStore();
        var session = store.Create("tenant-a", AgentRuntimeKind.Voice, "call-abc", "direct", "voice");
        var found = store.FindByCorrelationId("tenant-a", "call-abc", AgentRuntimeKind.Voice);

        Assert.NotNull(found);
        Assert.Equal(session.TestSessionId, found!.TestSessionId);
    }

    [Fact]
    public void TryConsumeMessageQuota_BlocksAfterLimit()
    {
        var store = new InMemoryTestStudioSessionStore();
        var session = store.Create("tenant-a", AgentRuntimeKind.Text, "corr-2", "direct", null);

        Assert.True(store.TryConsumeMessageQuota("tenant-a", session.TestSessionId, 2));
        Assert.True(store.TryConsumeMessageQuota("tenant-a", session.TestSessionId, 2));
        Assert.False(store.TryConsumeMessageQuota("tenant-a", session.TestSessionId, 2));
    }
}
