namespace AgentFlow.Abstractions;

public static class CommunicationCapabilities
{
    public const string TextSend = "text.send";
    public const string TemplateSend = "template.send";
    public const string AudioStreamIn = "audio.stream.in";
    public const string AudioStreamOut = "audio.stream.out";
    public const string AudioTranscribe = "audio.transcribe";
    public const string AudioSynthesize = "audio.synthesize";
    public const string CallOutbound = "call.outbound";
    public const string CallControl = "call.control";
    public const string VideoStreamIn = "video.stream.in";
    public const string VideoStreamOut = "video.stream.out";
}

public sealed record ProviderCapabilityDescriptor
{
    public required string Name { get; init; }
    public required string Channel { get; init; }
    public string? Description { get; init; }
    public bool SupportsStreaming { get; init; }
}

public sealed record ProviderConnectionProfile
{
    public required string ConnectionId { get; init; }
    public required string TenantId { get; init; }
    public required string ProviderId { get; init; }
    public required string ConnectorId { get; init; }
    public IReadOnlyDictionary<string, string> Config { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Secret { get; init; } = new Dictionary<string, string>();
}

public sealed record ProviderResolutionContext
{
    public required string TenantId { get; init; }
    public required string Capability { get; init; }
    public required string Channel { get; init; }
    public string? PreferredProviderId { get; init; }
    public string? ConnectionId { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public interface IProviderAdapter
{
    string ProviderId { get; }
    IReadOnlyList<ProviderCapabilityDescriptor> Capabilities { get; }
}

public interface IMessageSendProviderAdapter : IProviderAdapter
{
    Task<ProviderMessageSendResult> SendMessageAsync(
        ProviderConnectionProfile connection,
        ProviderMessageSendRequest request,
        CancellationToken ct = default);
}

public interface IVoiceCallProviderAdapter : IProviderAdapter
{
    Task<ProviderVoiceCallResult> PlaceCallAsync(
        ProviderConnectionProfile connection,
        ProviderVoiceCallRequest request,
        CancellationToken ct = default);
}

public interface IVoiceCallControlProviderAdapter : IProviderAdapter
{
    Task<ProviderVoiceCallControlResult> UpdateCallAsync(
        ProviderConnectionProfile connection,
        ProviderVoiceCallControlRequest request,
        CancellationToken ct = default);
}

public interface IAudioTranscriptionProviderAdapter : IProviderAdapter
{
    Task<ProviderTranscriptionResult> TranscribeAsync(
        ProviderConnectionProfile connection,
        ProviderTranscriptionRequest request,
        CancellationToken ct = default);
}

public interface IAudioSynthesisProviderAdapter : IProviderAdapter
{
    Task<ProviderSynthesisResult> SynthesizeAsync(
        ProviderConnectionProfile connection,
        ProviderSynthesisRequest request,
        CancellationToken ct = default);
}

public interface IProviderRegistry
{
    void Register(IProviderAdapter adapter);
    IReadOnlyList<IProviderAdapter> GetAll();
    IReadOnlyList<TAdapter> GetByCapability<TAdapter>(string capability, string channel)
        where TAdapter : class, IProviderAdapter;
}

public interface IProviderResolver
{
    Task<ResolvedProviderAdapter<TAdapter>> ResolveRequiredAsync<TAdapter>(
        ProviderResolutionContext context,
        CancellationToken ct = default)
        where TAdapter : class, IProviderAdapter;
}

public sealed record ResolvedProviderAdapter<TAdapter>(TAdapter Adapter, ProviderConnectionProfile Connection)
    where TAdapter : class, IProviderAdapter;

public sealed record ProviderMessageSendRequest
{
    public required string Recipient { get; init; }
    public required string Content { get; init; }
    public string? TemplateName { get; init; }
    public string? StatusCallbackUrl { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ProviderMessageSendResult
{
    public required string ProviderMessageId { get; init; }
    public required string ProviderStatus { get; init; }
    public string? RawResponse { get; init; }
}

public sealed record ProviderVoiceCallRequest
{
    public required string PhoneNumber { get; init; }
    public required string Script { get; init; }
    public string? StatusCallbackUrl { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ProviderVoiceCallResult
{
    public required string ProviderCallId { get; init; }
    public required string ProviderStatus { get; init; }
    public string? RawResponse { get; init; }
}

public sealed record ProviderVoiceCallControlRequest
{
    public required string CallId { get; init; }
    public required string Twiml { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ProviderVoiceCallControlResult
{
    public required string ProviderCallId { get; init; }
    public required string ProviderStatus { get; init; }
    public string? RawResponse { get; init; }
}

public sealed record ProviderTranscriptionRequest
{
    public required byte[] AudioBytes { get; init; }
    public required string ContentType { get; init; }
    public string? Language { get; init; }
    public string? Model { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ProviderTranscriptionResult
{
    public required string Transcript { get; init; }
    public required string ProviderStatus { get; init; }
    public string? RawResponse { get; init; }
}

public sealed record ProviderSynthesisRequest
{
    public required string Text { get; init; }
    public string? Voice { get; init; }
    public string? Model { get; init; }
    public string? OutputFormat { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ProviderSynthesisResult
{
    public required byte[] AudioBytes { get; init; }
    public required string ContentType { get; init; }
    public required string ProviderStatus { get; init; }
    public string? RawResponse { get; init; }
}

public enum AgentRuntimeKind
{
    Text = 0,
    Voice = 1,
    MultimodalRealtime = 2
}

public sealed record AgentRuntimeRequest
{
    public required string TenantId { get; init; }
    public required AgentRuntimeKind RuntimeKind { get; init; }
    public string? SessionId { get; init; }
    public string? ConversationId { get; init; }
    public string? ThreadId { get; init; }
    public string? AgentId { get; init; }
    public string? CorrelationId { get; init; }
    public string? Channel { get; init; }
    public AgentExecutionRequest? TextExecutionRequest { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record AgentRuntimeResult
{
    public required AgentRuntimeKind RuntimeKind { get; init; }
    public required ExecutionStatus Status { get; init; }
    public string? TenantId { get; init; }
    public string? ConversationId { get; init; }
    public string? ThreadId { get; init; }
    public string? AgentId { get; init; }
    public string? CorrelationId { get; init; }
    public string? Channel { get; init; }
    public string? Response { get; init; }
    public string? ExecutionId { get; init; }
    public string? SessionId { get; init; }
}

public interface IAgentRuntime
{
    AgentRuntimeKind Kind { get; }
    Task<AgentRuntimeResult> ExecuteAsync(AgentRuntimeRequest request, CancellationToken ct = default);
}

public interface IAgentRuntimeRegistry
{
    void Register(IAgentRuntime runtime);
    IAgentRuntime GetRequired(AgentRuntimeKind kind);
}

public interface IRealtimeSessionRuntime : IAgentRuntime
{
}

public sealed record MessageReceivedEvent
{
    public required string TenantId { get; init; }
    public required string ChannelId { get; init; }
    public required string SessionId { get; init; }
    public required string MessageId { get; init; }
    public required string Sender { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record MessageDeliveryUpdatedEvent
{
    public required string TenantId { get; init; }
    public required string ChannelId { get; init; }
    public required string MessageId { get; init; }
    public required string ProviderStatus { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CallStartedEvent
{
    public required string TenantId { get; init; }
    public required string ChannelId { get; init; }
    public required string SessionId { get; init; }
    public required string CallId { get; init; }
    public required string PhoneNumber { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CallEndedEvent
{
    public required string TenantId { get; init; }
    public required string ChannelId { get; init; }
    public required string SessionId { get; init; }
    public required string CallId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AudioChunkReceivedEvent
{
    public required string TenantId { get; init; }
    public required string SessionId { get; init; }
    public required string StreamId { get; init; }
    public required string ContentType { get; init; }
    public required byte[] Payload { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record TranscriptProducedEvent
{
    public required string TenantId { get; init; }
    public required string SessionId { get; init; }
    public required string Transcript { get; init; }
    public string? ProviderId { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AudioSynthesizedEvent
{
    public required string TenantId { get; init; }
    public required string SessionId { get; init; }
    public required string StreamId { get; init; }
    public required string ContentType { get; init; }
    public required byte[] Payload { get; init; }
    public string? Text { get; init; }
    public string? ProviderId { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record RuntimeDecisionProducedEvent
{
    public required string TenantId { get; init; }
    public required string SessionId { get; init; }
    public required AgentRuntimeKind RuntimeKind { get; init; }
    public required string DecisionType { get; init; }
    public string? PayloadJson { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ToolInvocationRequestedEvent
{
    public required string TenantId { get; init; }
    public required string ExecutionId { get; init; }
    public required string ToolName { get; init; }
    public required string InputJson { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ToolInvocationCompletedEvent
{
    public required string TenantId { get; init; }
    public required string ExecutionId { get; init; }
    public required string ToolName { get; init; }
    public required bool Succeeded { get; init; }
    public string? OutputJson { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ConversationEscalatedEvent
{
    public required string TenantId { get; init; }
    public required string SessionId { get; init; }
    public required string Reason { get; init; }
    public string? TargetQueue { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
