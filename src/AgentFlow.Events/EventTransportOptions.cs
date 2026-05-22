namespace AgentFlow.Events;

public sealed class EventTransportOptions
{
    public const string SectionName = "EventTransport";

    public string Provider { get; set; } = "InProcess";
    public string NatsUrl { get; set; } = "nats://localhost:4222";
    public string NatsSubjectPrefix { get; set; } = "agentflow.events";
    public string NatsDeadLetterSuffix { get; set; } = "deadletter";
    public int DeliveryMaxAttempts { get; set; } = 3;
    public int DeliveryBaseBackoffMs { get; set; } = 200;
    public string DeadLetterStoreProvider { get; set; } = "InMemory";
    public int DeadLetterRetentionHours { get; set; } = 72;
    public string? ConnectionString { get; set; }
    public string TopicName { get; set; } = "agentflow-events";
    public string SubscriptionPrefix { get; set; } = "agentflow";
}
