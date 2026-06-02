using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;
using AgentFlow.Observability;
using AgentFlow.Api.Workflow;
using AgentFlow.Api.AuthProfiles;
using AgentFlow.Api.TestStudio;
using System.Text.Json;

namespace AgentFlow.Api.Voice;

public sealed class VoiceRuntimeEventDispatcher : BackgroundService
{
    private readonly IAgentEventTransport _eventTransport;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VoiceRuntimeEventDispatcher> _logger;
    private readonly IExecutionGovernancePolicy _governancePolicy;
    private readonly ITestStudioSessionStore? _testStudioStore;

    public VoiceRuntimeEventDispatcher(
        IAgentEventTransport eventTransport,
        IServiceScopeFactory scopeFactory,
        ILogger<VoiceRuntimeEventDispatcher> logger,
        IExecutionGovernancePolicy governancePolicy,
        ITestStudioSessionStore? testStudioStore = null)
    {
        _eventTransport = eventTransport;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _governancePolicy = governancePolicy;
        _testStudioStore = testStudioStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscription = await _eventTransport.SubscribeAsync(
            "voice-runtime",
            async evt => await DispatchAsync(evt, stoppingToken),
            stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
        finally
        {
            await subscription.DisposeAsync();
        }
    }

    private async Task DispatchAsync(AgentEvent evt, CancellationToken ct)
    {
        if (!evt.EventType.StartsWith("connect.call.", StringComparison.OrdinalIgnoreCase))
            return;

        var shouldExecuteRuntimeDispatch = true;
        if (string.Equals(evt.EventType, "connect.call.audio.chunk", StringComparison.OrdinalIgnoreCase))
        {
            await PublishTranscriptEventAsync(evt, ct);
            shouldExecuteRuntimeDispatch = false;
        }
        else if (string.Equals(evt.EventType, "connect.call.transcript.produced", StringComparison.OrdinalIgnoreCase))
        {
            await PublishSynthesizedAudioEventAsync(evt, ct);
            shouldExecuteRuntimeDispatch = false;
        }

        if (!shouldExecuteRuntimeDispatch)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runtimeRegistry = scope.ServiceProvider.GetRequiredService<IAgentRuntimeRegistry>();
            var runtimeProfileStore = scope.ServiceProvider.GetService<IRuntimeModelProfileStore>();
            var runtimeProfile = runtimeProfileStore?.GetDefault(evt.TenantId, AgentRuntimeKind.Voice.ToString());
            var runtime = runtimeRegistry.GetRequired(AgentRuntimeKind.Voice);
            var metadata = new Dictionary<string, string>(evt.Headers)
            {
                ["eventType"] = evt.EventType,
                ["eventId"] = evt.EventId
            };
            if (runtimeProfile is not null)
            {
                metadata["runtimeModelProfileId"] = runtimeProfile.Id;
                runtimeProfile.ApplyExecutionMetadata(metadata);
            }
            var result = await runtime.ExecuteAsync(new AgentRuntimeRequest
            {
                TenantId = evt.TenantId,
                RuntimeKind = AgentRuntimeKind.Voice,
                SessionId = evt.SessionId,
                CorrelationId = evt.CorrelationId,
                ThreadId = evt.SessionId,
                Channel = evt.Headers.TryGetValue("channel", out var channelValue) ? channelValue : "voice",
                Metadata = metadata
                }, ct);

            _logger.LogInformation(
                "Voice runtime dispatched event. Tenant={TenantId} SessionId={SessionId} EventType={EventType} Status={Status}",
                evt.TenantId,
                evt.SessionId,
                evt.EventType,
                result.Status);
        }
        catch (Exception ex)
        {
            AgentFlowTelemetry.ExecutionsFailed.Add(1,
                new KeyValuePair<string, object?>("runtime_kind", AgentRuntimeKind.Voice.ToString()),
                new KeyValuePair<string, object?>("event_type", evt.EventType));
            _logger.LogWarning(
                ex,
                "Voice runtime dispatch failed. Tenant={TenantId} EventType={EventType}",
                evt.TenantId,
                evt.EventType);
        }
    }

    private async Task PublishTranscriptEventAsync(AgentEvent evt, CancellationToken ct)
    {
        try
        {
            var chunk = JsonSerializer.Deserialize<AudioChunkReceivedEvent>(evt.Payload);
            if (chunk is null)
                return;

            var transcriptResult = await TranscribeWithProviderOrFallbackAsync(evt, chunk, ct);
            var transcriptEvent = new TranscriptProducedEvent
            {
                TenantId = chunk.TenantId,
                SessionId = chunk.SessionId,
                Transcript = transcriptResult.Transcript,
                ProviderId = transcriptResult.ProviderId
            };
            AppendVoiceTestEvent(
                evt,
                stage: "stt_transcript",
                payloadType: "transcript",
                status: "produced",
                message: transcriptResult.Transcript,
                direction: "inbound");

            await _eventTransport.PublishAsync(new AgentEvent
            {
                EventType = "connect.call.transcript.produced",
                TenantId = evt.TenantId,
                AgentKey = "voice-runtime",
                SessionId = evt.SessionId,
                CorrelationId = evt.CorrelationId,
                Headers = new Dictionary<string, string>(evt.Headers)
                {
                    ["transcriptProvider"] = transcriptResult.ProviderId
                },
                Payload = JsonSerializer.Serialize(transcriptEvent)
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish transcript event for audio chunk. EventId={EventId}", evt.EventId);
        }
    }

    private async Task PublishSynthesizedAudioEventAsync(AgentEvent evt, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runtimeRegistry = scope.ServiceProvider.GetRequiredService<IAgentRuntimeRegistry>();
            var transcript = JsonSerializer.Deserialize<TranscriptProducedEvent>(evt.Payload);
            if (transcript is null || string.IsNullOrWhiteSpace(transcript.Transcript))
                return;

            var runtime = runtimeRegistry.GetRequired(AgentRuntimeKind.Voice);
            var runtimeResult = await runtime.ExecuteAsync(new AgentRuntimeRequest
            {
                TenantId = evt.TenantId,
                RuntimeKind = AgentRuntimeKind.Voice,
                SessionId = evt.SessionId,
                CorrelationId = evt.CorrelationId,
                ThreadId = evt.SessionId,
                Channel = evt.Headers.TryGetValue("channel", out var channelValue) ? channelValue : "voice",
                Metadata = new Dictionary<string, string>(evt.Headers)
                {
                    ["eventType"] = evt.EventType,
                    ["transcript"] = transcript.Transcript
                }
            }, ct);

            var textToSpeak = string.IsNullOrWhiteSpace(runtimeResult.Response)
                ? "Estoy procesando tu solicitud."
                : runtimeResult.Response!;

            var synthesizedResult = await SynthesizeWithProviderOrFallbackAsync(evt, textToSpeak, ct);
            var synthesized = new AudioSynthesizedEvent
            {
                TenantId = evt.TenantId,
                SessionId = transcript.SessionId,
                StreamId = evt.CorrelationId ?? Guid.NewGuid().ToString("N"),
                ContentType = synthesizedResult.ContentType,
                Payload = synthesizedResult.AudioBytes,
                Text = textToSpeak,
                ProviderId = synthesizedResult.ProviderId
            };
            AppendVoiceTestEvent(
                evt,
                stage: "tts_synthesized",
                payloadType: "audio",
                status: "produced",
                message: textToSpeak,
                direction: "outbound");

            await _eventTransport.PublishAsync(new AgentEvent
            {
                EventType = "connect.call.audio.synthesized",
                TenantId = evt.TenantId,
                AgentKey = "voice-runtime",
                SessionId = evt.SessionId,
                CorrelationId = evt.CorrelationId,
                Headers = new Dictionary<string, string>(evt.Headers)
                {
                    ["ttsProvider"] = synthesizedResult.ProviderId
                },
                Payload = JsonSerializer.Serialize(synthesized)
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish synthesized audio event. EventId={EventId}", evt.EventId);
        }
    }

    private async Task<(string Transcript, string ProviderId)> TranscribeWithProviderOrFallbackAsync(AgentEvent evt, AudioChunkReceivedEvent chunk, CancellationToken ct)
    {
        IWorkflowAuditService? audit = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<IProviderResolver>();
            var modelCatalog = scope.ServiceProvider.GetService<IModelCatalogStore>();
            audit = scope.ServiceProvider.GetRequiredService<IWorkflowAuditService>();
            var sttModelId = evt.Headers.TryGetValue("sttModelId", out var sttModel) ? sttModel : null;
            var catalogPreferred = !string.IsNullOrWhiteSpace(sttModelId) ? modelCatalog?.Get(evt.TenantId, sttModelId!)?.ProviderId : null;
            var preferred = !string.IsNullOrWhiteSpace(catalogPreferred)
                ? catalogPreferred!
                : GetPreferredProvider(evt.Headers, "sttProvider", "openai");
            var providerChain = BuildProviderChain(evt.Headers, "stt", preferred, "openai");
            var channel = evt.Headers.TryGetValue("channel", out var ch) ? ch : "voice";
            var resolved = await resolver.ResolveRequiredAsync<IAudioTranscriptionProviderAdapter>(
                new ProviderResolutionContext
                {
                    TenantId = evt.TenantId,
                    Capability = CommunicationCapabilities.AudioTranscribe,
                    Channel = channel,
                    PreferredProviderId = preferred,
                    Metadata = new Dictionary<string, string>
                    {
                        ["providerCandidates"] = providerChain
                    }
                },
                ct);

            var result = await resolved.Adapter.TranscribeAsync(
                resolved.Connection,
                new ProviderTranscriptionRequest
                {
                    AudioBytes = chunk.Payload,
                    ContentType = chunk.ContentType,
                    Metadata = new Dictionary<string, string>
                    {
                        ["sessionId"] = chunk.SessionId,
                        ["streamId"] = chunk.StreamId
                    }
                },
                ct);

            if (audit is not null)
            {
                await audit.RecordStudioActionAsync(
                    evt.TenantId,
                    "voice-runtime",
                    "voice.stt.provider.selected",
                    chunk.SessionId,
                    new
                    {
                        policy = "voice_stt_provider_chain",
                        decision = string.Equals(resolved.Adapter.ProviderId, preferred, StringComparison.OrdinalIgnoreCase) ? "primary" : "fallback",
                        provider = resolved.Adapter.ProviderId,
                        preferredProvider = preferred,
                        providerChain
                    },
                    evt.CorrelationId,
                    ct);
            }

            return (result.Transcript, resolved.Adapter.ProviderId);
        }
        catch (Exception ex)
        {
            var attemptedProvider = GetPreferredProvider(evt.Headers, "sttProvider", "openai");
            _governancePolicy.RecordFallback(
                "voice_stt_fallback",
                "fallback",
                tenantId: evt.TenantId,
                flow: "voice.stt",
                provider: attemptedProvider);
            if (audit is not null)
            {
                await audit.RecordStudioActionAsync(
                evt.TenantId,
                "voice-runtime",
                "voice.stt.fallback",
                evt.SessionId ?? "voice",
                new
                {
                    policy = "voice_stt_fallback",
                    decision = "fallback",
                    provider = attemptedProvider,
                    sessionId = chunk.SessionId
                },
                evt.CorrelationId,
                ct);
            }
            _logger.LogDebug(
                ex,
                "Falling back to synthetic transcript for audio chunk. Tenant={TenantId} SessionId={SessionId}",
                evt.TenantId,
                chunk.SessionId);
            return ($"[audio_chunk bytes={chunk.Payload.Length} stream={chunk.StreamId}]", "voice-fallback");
        }
    }

    private async Task<(byte[] AudioBytes, string ContentType, string ProviderId)> SynthesizeWithProviderOrFallbackAsync(
        AgentEvent evt,
        string text,
        CancellationToken ct)
    {
        IWorkflowAuditService? audit = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<IProviderResolver>();
            var modelCatalog = scope.ServiceProvider.GetService<IModelCatalogStore>();
            audit = scope.ServiceProvider.GetRequiredService<IWorkflowAuditService>();
            var ttsModelId = evt.Headers.TryGetValue("ttsModelId", out var ttsModel) ? ttsModel : null;
            var catalogPreferred = !string.IsNullOrWhiteSpace(ttsModelId) ? modelCatalog?.Get(evt.TenantId, ttsModelId!)?.ProviderId : null;
            var preferred = !string.IsNullOrWhiteSpace(catalogPreferred)
                ? catalogPreferred!
                : GetPreferredProvider(evt.Headers, "ttsProvider", "openai");
            var providerChain = BuildProviderChain(evt.Headers, "tts", preferred, "openai");
            var channel = evt.Headers.TryGetValue("channel", out var ch) ? ch : "voice";
            var resolved = await resolver.ResolveRequiredAsync<IAudioSynthesisProviderAdapter>(
                new ProviderResolutionContext
                {
                    TenantId = evt.TenantId,
                    Capability = CommunicationCapabilities.AudioSynthesize,
                    Channel = channel,
                    PreferredProviderId = preferred,
                    Metadata = new Dictionary<string, string>
                    {
                        ["providerCandidates"] = providerChain
                    }
                },
                ct);

            var result = await resolved.Adapter.SynthesizeAsync(
                resolved.Connection,
                new ProviderSynthesisRequest
                {
                    Text = text,
                    Metadata = new Dictionary<string, string>
                    {
                        ["sessionId"] = evt.SessionId ?? string.Empty
                    }
                },
                ct);

            if (audit is not null)
            {
                await audit.RecordStudioActionAsync(
                    evt.TenantId,
                    "voice-runtime",
                    "voice.tts.provider.selected",
                    evt.SessionId ?? "voice",
                    new
                    {
                        policy = "voice_tts_provider_chain",
                        decision = string.Equals(resolved.Adapter.ProviderId, preferred, StringComparison.OrdinalIgnoreCase) ? "primary" : "fallback",
                        provider = resolved.Adapter.ProviderId,
                        preferredProvider = preferred,
                        providerChain
                    },
                    evt.CorrelationId,
                    ct);
            }

            return (result.AudioBytes, result.ContentType, resolved.Adapter.ProviderId);
        }
        catch (Exception ex)
        {
            var attemptedProvider = GetPreferredProvider(evt.Headers, "ttsProvider", "openai");
            _governancePolicy.RecordFallback(
                "voice_tts_fallback",
                "fallback",
                tenantId: evt.TenantId,
                flow: "voice.tts",
                provider: attemptedProvider);
            if (audit is not null)
            {
                await audit.RecordStudioActionAsync(
                evt.TenantId,
                "voice-runtime",
                "voice.tts.fallback",
                evt.SessionId ?? "voice",
                new
                {
                    policy = "voice_tts_fallback",
                    decision = "fallback",
                    provider = attemptedProvider,
                    sessionId = evt.SessionId
                },
                evt.CorrelationId,
                ct);
            }
            _logger.LogDebug(
                ex,
                "Falling back to synthetic audio bytes for TTS. Tenant={TenantId} SessionId={SessionId}",
                evt.TenantId,
                evt.SessionId);
            return (System.Text.Encoding.UTF8.GetBytes(text), "text/plain", "voice-fallback");
        }
    }

    private static string GetPreferredProvider(IReadOnlyDictionary<string, string> headers, string key, string fallback)
    {
        if (headers.TryGetValue(key, out var configured) && !string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
        return fallback;
    }

    private static string BuildProviderChain(
        IReadOnlyDictionary<string, string> headers,
        string role,
        string preferredProvider,
        params string[] defaultFallbacks)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chain = new List<string>();

        void add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            foreach (var value in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (set.Add(value))
                    chain.Add(value);
            }
        }

        add(preferredProvider);
        if (headers.TryGetValue($"{role}Providers", out var scopedProviders))
            add(scopedProviders);
        if (headers.TryGetValue($"{role}ProvidersCsv", out var scopedProvidersCsv))
            add(scopedProvidersCsv);
        if (headers.TryGetValue($"providerCandidates.{role}", out var roleCandidates))
            add(roleCandidates);
        if (headers.TryGetValue("providerCandidates", out var genericCandidates))
            add(genericCandidates);
        if (headers.TryGetValue("providerCandidatesCsv", out var genericCandidatesCsv))
            add(genericCandidatesCsv);
        foreach (var fallback in defaultFallbacks)
            add(fallback);

        return string.Join(",", chain);
    }

    private void AppendVoiceTestEvent(
        AgentEvent evt,
        string stage,
        string payloadType,
        string status,
        string? message,
        string direction)
    {
        if (_testStudioStore is null || string.IsNullOrWhiteSpace(evt.CorrelationId)) return;
        var session = _testStudioStore.FindByCorrelationId(evt.TenantId, evt.CorrelationId, AgentRuntimeKind.Voice);
        if (session is null) return;

        _testStudioStore.AppendEvent(evt.TenantId, session.TestSessionId, new TestStudioEvent
        {
            Stage = stage,
            Direction = direction,
            PayloadType = payloadType,
            Status = status,
            CorrelationId = evt.CorrelationId,
            Message = message
        });
    }
}
