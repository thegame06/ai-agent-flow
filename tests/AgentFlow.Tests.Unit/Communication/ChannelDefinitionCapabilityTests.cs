using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Tests.Unit.Communication;

public class ChannelDefinitionCapabilityTests
{
    [Fact]
    public void VoiceChannel_ExposesRealtimeCapabilitiesAndPolicy()
    {
        var channel = ChannelDefinition.Create("tenant-a", "Voice", ChannelType.Voice);

        Assert.Contains(channel.Capabilities, x => x.Name == "call.outbound");
        Assert.Contains(channel.Capabilities, x => x.Name == "audio.stream.in" && x.SupportsStreaming);
        Assert.True(channel.SessionPolicy.SupportsRealtime);
        Assert.True(channel.SessionPolicy.SupportsInterruptions);
        Assert.Equal("connect.call.received", channel.EventContractMap.InboundEventType);
    }

    [Fact]
    public void WhatsAppChannel_RequiresTemplateOutsideWindow()
    {
        var channel = ChannelDefinition.Create(
            "tenant-a",
            "WhatsApp",
            ChannelType.WhatsApp,
            new Dictionary<string, string> { ["SessionWindowHours"] = "24" });

        Assert.Contains(channel.Capabilities, x => x.Name == "template.send");
        Assert.True(channel.SessionPolicy.RequiresTemplateOutsideWindow);
        Assert.Equal("connect.message.received", channel.EventContractMap.InboundEventType);
    }
}
