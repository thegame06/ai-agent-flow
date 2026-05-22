using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Core.Engine;

public interface IChannelCapabilityPolicy
{
    void EnsureSupportsAny(ChannelDefinition channel, string messageId, params string[] acceptedCapabilities);
}

public sealed class ChannelCapabilityPolicy : IChannelCapabilityPolicy
{
    public void EnsureSupportsAny(ChannelDefinition channel, string messageId, params string[] acceptedCapabilities)
    {
        if (acceptedCapabilities.Any(channel.SupportsCapability))
            return;

        throw new InvalidOperationException(
            $"Channel '{channel.Id}' does not support required capabilities [{string.Join(", ", acceptedCapabilities)}]. MessageId='{messageId}'.");
    }
}

