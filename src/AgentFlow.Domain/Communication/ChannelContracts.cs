namespace AgentFlow.Domain.Communication;

public sealed record ChannelCapabilityDescriptor
{
    public required string Name { get; init; }
    public string Direction { get; init; } = "bidirectional";
    public bool SupportsStreaming { get; init; }
    public string? PayloadFormat { get; init; }
}

public sealed record ChannelSessionPolicy
{
    public int SessionWindowHours { get; init; } = 24;
    public bool RequiresTemplateOutsideWindow { get; init; }
    public bool SupportsRealtime { get; init; }
    public bool SupportsInterruptions { get; init; }
}

public sealed record ChannelEventContractMap
{
    public required string InboundEventType { get; init; }
    public required string OutboundEventType { get; init; }
    public string? DeliveryStatusEventType { get; init; }
    public string? SessionStartedEventType { get; init; }
    public string? SessionEndedEventType { get; init; }
}
