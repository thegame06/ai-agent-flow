using AgentFlow.Abstractions;
using AgentFlow.Api.Workflow;

namespace AgentFlow.Tests.Unit.Workflow;

public sealed class RuntimeCompatibilityPolicyTests
{
    [Theory]
    [InlineData("Text", AgentRuntimeKind.Text)]
    [InlineData("voice", AgentRuntimeKind.Voice)]
    [InlineData("MultimodalRealtime", AgentRuntimeKind.MultimodalRealtime)]
    public void TryParseRuntimeKind_ParsesKnownValues(string raw, AgentRuntimeKind expected)
    {
        var ok = RuntimeCompatibilityPolicy.TryParseRuntimeKind(raw, out var kind, out var normalized);
        Assert.True(ok);
        Assert.Equal(expected, kind);
        Assert.Equal(expected.ToString(), normalized);
    }

    [Fact]
    public void IsTriggerEventCompatible_RespectsRuntimeFamilies()
    {
        Assert.True(RuntimeCompatibilityPolicy.IsTriggerEventCompatible(AgentRuntimeKind.Text, "message.received"));
        Assert.False(RuntimeCompatibilityPolicy.IsTriggerEventCompatible(AgentRuntimeKind.Text, "connect.call.received"));

        Assert.True(RuntimeCompatibilityPolicy.IsTriggerEventCompatible(AgentRuntimeKind.Voice, "connect.call.received"));
        Assert.False(RuntimeCompatibilityPolicy.IsTriggerEventCompatible(AgentRuntimeKind.Voice, "message.received"));

        Assert.True(RuntimeCompatibilityPolicy.IsTriggerEventCompatible(AgentRuntimeKind.MultimodalRealtime, "video.frame.realtime"));
        Assert.False(RuntimeCompatibilityPolicy.IsTriggerEventCompatible(AgentRuntimeKind.MultimodalRealtime, "message.received"));
    }
}
