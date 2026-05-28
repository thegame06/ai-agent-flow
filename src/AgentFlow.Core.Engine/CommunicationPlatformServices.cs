using AgentFlow.Abstractions;

namespace AgentFlow.Core.Engine;

public sealed class InMemoryProviderRegistry : IProviderRegistry
{
    private readonly List<IProviderAdapter> _adapters = [];
    private readonly object _sync = new();

    public void Register(IProviderAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (string.IsNullOrWhiteSpace(adapter.ProviderId))
            throw new ArgumentException("Provider adapter must expose a non-empty ProviderId.", nameof(adapter));
        if (adapter.Capabilities.Count == 0)
            throw new ArgumentException($"Provider '{adapter.ProviderId}' must declare at least one capability.", nameof(adapter));

        lock (_sync)
        {
            if (_adapters.Any(x =>
                    x.GetType() == adapter.GetType() &&
                    string.Equals(x.ProviderId, adapter.ProviderId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Provider '{adapter.ProviderId}' with adapter '{adapter.GetType().Name}' is already registered.");
            }

            _adapters.Add(adapter);
        }
    }

    public IReadOnlyList<IProviderAdapter> GetAll()
    {
        lock (_sync)
        {
            return [.. _adapters];
        }
    }

    public IReadOnlyList<TAdapter> GetByCapability<TAdapter>(string capability, string channel)
        where TAdapter : class, IProviderAdapter
    {
        if (string.IsNullOrWhiteSpace(capability))
            throw new ArgumentException("Capability is required.", nameof(capability));
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Channel is required.", nameof(channel));

        lock (_sync)
        {
            return _adapters
                .OfType<TAdapter>()
                .Where(adapter => adapter.Capabilities.Any(cap =>
                    string.Equals(cap.Name, capability, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(cap.Channel, channel, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }
}

public sealed class AgentRuntimeRegistry : IAgentRuntimeRegistry
{
    private readonly Dictionary<AgentRuntimeKind, IAgentRuntime> _runtimes = new();

    public AgentRuntimeRegistry(IEnumerable<IAgentRuntime> runtimes)
    {
        foreach (var runtime in runtimes)
        {
            Register(runtime);
        }
    }

    public void Register(IAgentRuntime runtime)
    {
        _runtimes[runtime.Kind] = runtime;
    }

    public IAgentRuntime GetRequired(AgentRuntimeKind kind)
    {
        if (_runtimes.TryGetValue(kind, out var runtime))
            return runtime;

        throw new InvalidOperationException($"No runtime registered for kind '{kind}'.");
    }
}

public sealed class TextAgentRuntime : IAgentRuntime
{
    private readonly IAgentExecutor _agentExecutor;

    public TextAgentRuntime(IAgentExecutor agentExecutor)
    {
        _agentExecutor = agentExecutor;
    }

    public AgentRuntimeKind Kind => AgentRuntimeKind.Text;

    public async Task<AgentRuntimeResult> ExecuteAsync(AgentRuntimeRequest request, CancellationToken ct = default)
    {
        if (request.TextExecutionRequest is null)
            throw new InvalidOperationException("Text runtime requires TextExecutionRequest.");

        var execution = await _agentExecutor.ExecuteAsync(request.TextExecutionRequest, ct);
        return new AgentRuntimeResult
        {
            RuntimeKind = Kind,
            Status = execution.Status,
            TenantId = request.TenantId,
            ConversationId = request.ConversationId,
            ThreadId = request.ThreadId ?? request.TextExecutionRequest?.ThreadId,
            AgentId = request.AgentId,
            CorrelationId = request.CorrelationId,
            Channel = request.Channel,
            Response = execution.FinalResponse,
            ExecutionId = execution.ExecutionId,
            SessionId = request.SessionId
        };
    }
}

public sealed class VoiceAgentRuntime : IRealtimeSessionRuntime
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> SessionTurns = new(StringComparer.OrdinalIgnoreCase);

    public AgentRuntimeKind Kind => AgentRuntimeKind.Voice;

    public Task<AgentRuntimeResult> ExecuteAsync(AgentRuntimeRequest request, CancellationToken ct = default)
    {
        var eventType = request.Metadata.TryGetValue("eventType", out var et) ? et : string.Empty;
        var channel = request.Metadata.TryGetValue("channel", out var ch) ? ch : "voice";
        var transcript = request.Metadata.TryGetValue("transcript", out var tr) ? tr : null;
        var sessionKey = string.IsNullOrWhiteSpace(request.SessionId) ? Guid.NewGuid().ToString("N") : request.SessionId!;
        var turn = SessionTurns.AddOrUpdate(sessionKey, 1, (_, prev) => prev + 1);

        var response = eventType switch
        {
            "connect.call.received" => "Hola, te habla el asistente de voz. En este momento estamos conectando tu solicitud.",
            "connect.call.transcript.produced" => BuildTranscriptReply(transcript),
            "connect.call.ended" => "Gracias por llamar. Cerramos la sesion de voz.",
            var x when x.StartsWith("connect.call.status.", StringComparison.OrdinalIgnoreCase) =>
                $"Estado de llamada actualizado: {x["connect.call.status.".Length..]}.",
            _ => $"Voice runtime processed event on channel {channel}."
        };

        if (string.Equals(eventType, "connect.call.ended", StringComparison.OrdinalIgnoreCase))
            SessionTurns.TryRemove(sessionKey, out _);
        else if (turn > 1 && eventType == "connect.call.transcript.produced")
            response = $"{response} Turno {turn} de la llamada.";

        return Task.FromResult(new AgentRuntimeResult
        {
            RuntimeKind = Kind,
            Status = ExecutionStatus.Completed,
            TenantId = request.TenantId,
            ConversationId = request.ConversationId,
            ThreadId = request.ThreadId,
            AgentId = request.AgentId,
            CorrelationId = request.CorrelationId,
            Channel = request.Channel ?? channel,
            SessionId = request.SessionId,
            Response = response
        });
    }

    private static string BuildTranscriptReply(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return "He recibido audio de tu llamada y estoy procesando tu solicitud.";

        var clean = transcript.Trim();
        if (clean.Length > 180)
            clean = clean[..180];

        return $"Entendido. Recibi: {clean}. Estoy procesando tu solicitud.";
    }
}

public sealed class MultimodalRealtimeRuntime : IRealtimeSessionRuntime
{
    public AgentRuntimeKind Kind => AgentRuntimeKind.MultimodalRealtime;

    public Task<AgentRuntimeResult> ExecuteAsync(AgentRuntimeRequest request, CancellationToken ct = default)
    {
        var eventType = request.Metadata.TryGetValue("eventType", out var et) ? et : "unknown";
        var hasText = request.Metadata.TryGetValue("text", out var text) && !string.IsNullOrWhiteSpace(text);
        var hasImage = request.Metadata.TryGetValue("imageUrl", out var imageUrl) && !string.IsNullOrWhiteSpace(imageUrl);
        var hasVideo = request.Metadata.TryGetValue("videoUrl", out var videoUrl) && !string.IsNullOrWhiteSpace(videoUrl);
        var hasAudio = request.Metadata.TryGetValue("audioChunk", out var audioChunk) && !string.IsNullOrWhiteSpace(audioChunk);

        var modalities = new List<string>();
        if (hasText) modalities.Add("text");
        if (hasImage) modalities.Add("image");
        if (hasVideo) modalities.Add("video");
        if (hasAudio) modalities.Add("audio");
        if (modalities.Count == 0) modalities.Add("none");

        var response = hasText
            ? $"Multimodal runtime recibio [{string.Join(",", modalities)}] en evento '{eventType}'. Mensaje: {text!.Trim()}."
            : $"Multimodal runtime recibio [{string.Join(",", modalities)}] en evento '{eventType}'.";

        return Task.FromResult(new AgentRuntimeResult
        {
            RuntimeKind = Kind,
            Status = ExecutionStatus.Completed,
            TenantId = request.TenantId,
            ConversationId = request.ConversationId,
            ThreadId = request.ThreadId,
            AgentId = request.AgentId,
            CorrelationId = request.CorrelationId,
            Channel = request.Channel,
            SessionId = request.SessionId,
            Response = response
        });
    }
}
