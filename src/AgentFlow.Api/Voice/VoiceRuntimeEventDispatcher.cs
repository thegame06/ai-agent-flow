using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;
using AgentFlow.Observability;
using AgentFlow.Api.Workflow;
using System.Text.Json;

namespace AgentFlow.Api.Voice;

public sealed class VoiceRuntimeEventDispatcher : BackgroundService
{
    private readonly IAgentEventTransport _eventTransport;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VoiceRuntimeEventDispatcher> _logger;
    private readonly IExecutionGovernancePolicy _governancePolicy;

    public VoiceRuntimeEventDispatcher(
        IAgentEventTransport eventTransport,
        IServiceScopeFactory scopeFactory,
        ILogger<VoiceRuntimeEventDispatcher> logger,
        IExecutionGovernancePolicy governancePolicy)
    {
        _eventTransport = eventTransport;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _governancePolicy = governancePolicy;
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
            var runtime = runtimeRegistry.GetRequired(AgentRuntimeKind.Voice);
            var result = await runtime.ExecuteAsync(new AgentRuntimeRequest
            {
                TenantId = evt.TenantId,
                RuntimeKind = AgentRuntimeKind.Voice,
                SessionId = evt.SessionId,
                Metadata = new Dictionary<string, string>(evt.Headers)
                {
                    ["eventType"] = evt.EventType,
                    ["eventId"] = evt.EventId
                }
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

            var (transcript, providerId) = await TranscribeWithProviderOrFallbackAsync(evt, chunk, ct);
            var transcriptEvent = new TranscriptProducedEvent
            {
                TenantId = chunk.TenantId,
                SessionId = chunk.SessionId,
                Transcript = transcript,
                ProviderId = providerId
            };

            await _eventTransport.PublishAsync(new AgentEvent
            {
                EventType = "connect.call.transcript.produced",
                TenantId = evt.TenantId,
                AgentKey = "voice-runtime",
                SessionId = evt.SessionId,
                CorrelationId = evt.CorrelationId,
                Headers = new Dictionary<string, string>(evt.Headers)
                {
                    ["transcriptProvider"] = providerId
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
                Metadata = new Dictionary<string, string>(evt.Headers)
                {
                    ["eventType"] = evt.EventType,
                    ["transcript"] = transcript.Transcript
                }
            }, ct);

            var textToSpeak = string.IsNullOrWhiteSpace(runtimeResult.Response)
                ? "Estoy procesando tu solicitud."
                : runtimeResult.Response!;

            var (audioBytes, contentType, providerId) = await SynthesizeWithProviderOrFallbackAsync(evt, textToSpeak, ct);
            var synthesized = new AudioSynthesizedEvent
            {
                TenantId = evt.TenantId,
                SessionId = transcript.SessionId,
                StreamId = evt.CorrelationId ?? Guid.NewGuid().ToString("N"),
                ContentType = contentType,
                Payload = audioBytes,
                Text = textToSpeak,
                ProviderId = providerId
            };

            await _eventTransport.PublishAsync(new AgentEvent
            {
                EventType = "connect.call.audio.synthesized",
                TenantId = evt.TenantId,
                AgentKey = "voice-runtime",
                SessionId = evt.SessionId,
                CorrelationId = evt.CorrelationId,
                Headers = new Dictionary<string, string>(evt.Headers)
                {
                    ["ttsProvider"] = providerId
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
            audit = scope.ServiceProvider.GetRequiredService<IWorkflowAuditService>();
            var preferred = evt.Headers.TryGetValue("sttProvider", out var configuredProvider)
                ? configuredProvider
                : "openai";
            var channel = evt.Headers.TryGetValue("channel", out var ch) ? ch : "voice";
            Exception? lastError = null;
            foreach (var providerCandidate in BuildProviderCandidates(preferred, "openai"))
            {
                try
                {
                    var resolved = await resolver.ResolveRequiredAsync<IAudioTranscriptionProviderAdapter>(
                        new ProviderResolutionContext
                        {
                            TenantId = evt.TenantId,
                            Capability = CommunicationCapabilities.AudioTranscribe,
                            Channel = channel,
                            PreferredProviderId = providerCandidate
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

                    return (result.Transcript, resolved.Adapter.ProviderId);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new InvalidOperationException("No STT provider candidate available.");
        }
        catch (Exception ex)
        {
            var attemptedProvider = evt.Headers.TryGetValue("sttProvider", out var providerHint) ? providerHint : "openai";
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
            audit = scope.ServiceProvider.GetRequiredService<IWorkflowAuditService>();
            var preferred = evt.Headers.TryGetValue("ttsProvider", out var configuredProvider)
                ? configuredProvider
                : "openai";
            var channel = evt.Headers.TryGetValue("channel", out var ch) ? ch : "voice";
            Exception? lastError = null;
            foreach (var providerCandidate in BuildProviderCandidates(preferred, "openai"))
            {
                try
                {
                    var resolved = await resolver.ResolveRequiredAsync<IAudioSynthesisProviderAdapter>(
                        new ProviderResolutionContext
                        {
                            TenantId = evt.TenantId,
                            Capability = CommunicationCapabilities.AudioSynthesize,
                            Channel = channel,
                            PreferredProviderId = providerCandidate
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

                    return (result.AudioBytes, result.ContentType, resolved.Adapter.ProviderId);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new InvalidOperationException("No TTS provider candidate available.");
        }
        catch (Exception ex)
        {
            var attemptedProvider = evt.Headers.TryGetValue("ttsProvider", out var providerHint) ? providerHint : "openai";
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

    private static IReadOnlyList<string> BuildProviderCandidates(string preferredProvider, params string[] fallbackProviders)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(preferredProvider))
            candidates.Add(preferredProvider);

        foreach (var fallbackProvider in fallbackProviders)
        {
            if (!string.IsNullOrWhiteSpace(fallbackProvider))
                candidates.Add(fallbackProvider);
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
