using System.Text;
using System.Text.Json;
using AgentFlow.Abstractions;
using AgentFlow.Observability;
using AgentFlow.Api.Workflow;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace AgentFlow.Api.Voice;

public sealed class VoicePlaybackGateway : BackgroundService
{
    private readonly IAgentEventTransport _eventTransport;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VoicePlaybackGateway> _logger;
    private readonly IWorkflowAuditService _audit;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _dedupe = new(StringComparer.OrdinalIgnoreCase);

    public VoicePlaybackGateway(
        IAgentEventTransport eventTransport,
        IServiceScopeFactory scopeFactory,
        ILogger<VoicePlaybackGateway> logger,
        IWorkflowAuditService audit)
    {
        _eventTransport = eventTransport;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _audit = audit;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscription = await _eventTransport.SubscribeAsync(
            "voice-runtime-playback",
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
        if (!string.Equals(evt.EventType, "connect.call.audio.synthesized", StringComparison.OrdinalIgnoreCase))
            return;

        var synthesized = JsonSerializer.Deserialize<AudioSynthesizedEvent>(evt.Payload);
        if (synthesized is null)
            return;

        await HandleSynthesizedEventAsync(evt, synthesized, ct);
    }

    public async Task HandleSynthesizedEventAsync(AgentEvent evt, AudioSynthesizedEvent synthesized, CancellationToken ct)
    {
        if (!MarkEventAsInFlight(evt))
        {
            _logger.LogDebug("Skipping duplicate playback event. EventId={EventId}", evt.EventId);
            await _audit.RecordStudioActionAsync(
                evt.TenantId,
                "voice-playback",
                "voice.playback.duplicate_ignored",
                synthesized.SessionId,
                new { evt.EventId, synthesized.SessionId },
                evt.CorrelationId,
                ct);
            return;
        }

        using var playbackActivity = AgentFlowTelemetry.EngineSource.StartActivity("VoicePlaybackGateway.HandleSynthesized", ActivityKind.Internal);
        playbackActivity?.SetTag("agentflow.tenant_id", evt.TenantId);
        playbackActivity?.SetTag("agentflow.session_id", synthesized.SessionId);
        playbackActivity?.SetTag("agentflow.event_id", evt.EventId);

        var callId = evt.CorrelationId;
        if (string.IsNullOrWhiteSpace(callId))
        {
            _logger.LogDebug(
                "Skipping synthesized playback without call correlation. Tenant={TenantId} SessionId={SessionId}",
                evt.TenantId,
                evt.SessionId);
            return;
        }

        var text = ResolvePlaybackText(synthesized);
        if (string.IsNullOrWhiteSpace(text))
            text = "Estoy procesando tu solicitud.";

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<IProviderResolver>();
            var channel = evt.Headers.TryGetValue("channel", out var ch) ? ch : "voice";
            var preferredProvider = evt.Headers.TryGetValue("provider", out var p) ? p : "twilio";
            var providerCandidates = BuildProviderCandidates(preferredProvider);
            Exception? lastException = null;
            var delivered = false;

            foreach (var provider in providerCandidates)
            {
                try
                {
                    var resolved = await resolver.ResolveRequiredAsync<IVoiceCallControlProviderAdapter>(
                        new ProviderResolutionContext
                        {
                            TenantId = evt.TenantId,
                            Capability = CommunicationCapabilities.CallControl,
                            Channel = channel,
                            PreferredProviderId = provider
                        },
                        ct);

                    var twiml = BuildPlaybackTwiml(evt, text);
                    await RetryAsync(async token =>
                    {
                        await resolved.Adapter.UpdateCallAsync(
                            resolved.Connection,
                            new ProviderVoiceCallControlRequest
                            {
                                CallId = callId!,
                                Twiml = twiml,
                                Metadata = new Dictionary<string, string>
                                {
                                    ["sessionId"] = synthesized.SessionId,
                                    ["eventId"] = evt.EventId
                                }
                            },
                            token);
                    }, ct);

                    _logger.LogInformation(
                        "Delivered synthesized playback to provider. Tenant={TenantId} SessionId={SessionId} CallId={CallId} Provider={Provider}",
                        evt.TenantId,
                        synthesized.SessionId,
                        callId,
                        resolved.Adapter.ProviderId);
                    await _audit.RecordStudioActionAsync(
                        evt.TenantId,
                        "voice-playback",
                        "voice.playback.delivered",
                        synthesized.SessionId,
                        new
                        {
                            policy = "voice_playback_provider_chain",
                            decision = provider.Equals(preferredProvider, StringComparison.OrdinalIgnoreCase) ? "primary" : "fallback",
                            provider = resolved.Adapter.ProviderId,
                            providerAttempted = provider,
                            synthesized.ContentType,
                            usedPlay = ShouldUsePlayTwiml(evt, synthesized),
                            evt.EventId
                        },
                        evt.CorrelationId,
                        ct);

                    delivered = true;
                    break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(
                        ex,
                        "Playback provider attempt failed. Tenant={TenantId} SessionId={SessionId} Provider={Provider} EventId={EventId}",
                        evt.TenantId,
                        synthesized.SessionId,
                        provider,
                        evt.EventId);
                }
            }

            if (!delivered && lastException is not null)
                throw lastException;
        }
        catch (Exception ex)
        {
            AgentFlowTelemetry.ExecutionRetries.Add(1,
                new KeyValuePair<string, object?>("tool_name", "voice.playback"));
            _logger.LogWarning(
                ex,
                "Failed to deliver synthesized playback. Tenant={TenantId} SessionId={SessionId} CallId={CallId}",
                evt.TenantId,
                synthesized.SessionId,
                callId);
            await _audit.RecordStudioActionAsync(
                evt.TenantId,
                "voice-playback",
                "voice.playback.failed",
                synthesized.SessionId,
                new
                {
                    policy = "voice_playback_provider_chain",
                    decision = "failed",
                    evt.EventId,
                    synthesized.ContentType
                },
                evt.CorrelationId,
                ct);
        }
        finally
        {
            CleanupDedupe();
        }
    }

    private static string ResolvePlaybackText(AudioSynthesizedEvent synthesized)
    {
        if (!string.IsNullOrWhiteSpace(synthesized.Text))
            return synthesized.Text!;

        if (synthesized.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetString(synthesized.Payload);

        return string.Empty;
    }

    private static IReadOnlyList<string> BuildProviderCandidates(string preferredProvider)
    {
        if (string.IsNullOrWhiteSpace(preferredProvider))
            return new[] { "twilio" };

        return new[] { preferredProvider, "twilio" }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ShouldUsePlayTwiml(AgentEvent evt, AudioSynthesizedEvent synthesized)
    {
        if (!synthesized.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!evt.Headers.TryGetValue("audioPlaybackUrl", out var playbackUrl))
            return false;
        return Uri.TryCreate(playbackUrl, UriKind.Absolute, out _);
    }

    private static string BuildPlaybackTwiml(AgentEvent evt, string text)
    {
        if (evt.Headers.TryGetValue("audioPlaybackUrl", out var playbackUrl)
            && Uri.TryCreate(playbackUrl, UriKind.Absolute, out _))
        {
            var escapedUrl = System.Security.SecurityElement.Escape(playbackUrl) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(escapedUrl))
                return $"<Response><Play>{escapedUrl}</Play></Response>";
        }

        var escaped = System.Security.SecurityElement.Escape(text) ?? "Hola.";
        return $"<Response><Say language='es-MX' voice='Polly.Mia'>{escaped}</Say></Response>";
    }

    private static async Task RetryAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        var delays = new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(350) };
        Exception? last = null;

        foreach (var delay in delays)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);

            try
            {
                await action(ct);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("Unknown retry failure.");
    }

    private bool MarkEventAsInFlight(AgentEvent evt)
    {
        var key = $"{evt.TenantId}:{evt.EventId}";
        return _dedupe.TryAdd(key, DateTimeOffset.UtcNow);
    }

    private void CleanupDedupe()
    {
        var threshold = DateTimeOffset.UtcNow.AddMinutes(-10);
        foreach (var item in _dedupe)
        {
            if (item.Value < threshold)
                _dedupe.TryRemove(item.Key, out _);
        }
    }
}
