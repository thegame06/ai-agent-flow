using System.Text.Json;
using AgentFlow.Abstractions;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Voice;
using AgentFlow.Api.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/webhooks/twilio/voice")]
[AllowAnonymous]
public sealed class TwilioVoiceWebhookController : ControllerBase
{
    private readonly IAgentEventTransport _eventTransport;
    private readonly IWorkflowAuditService _audit;
    private readonly IVoiceSessionOrchestrator _voiceSessionOrchestrator;
    private readonly IAgentRuntimeRegistry _runtimeRegistry;
    private readonly ITwilioWebhookSignatureValidator _signatureValidator;
    private readonly ITenantConnectionStore _tenantConnectionStore;
    private readonly ILogger<TwilioVoiceWebhookController> _logger;

    public TwilioVoiceWebhookController(
        IAgentEventTransport eventTransport,
        IWorkflowAuditService audit,
        IVoiceSessionOrchestrator voiceSessionOrchestrator,
        IAgentRuntimeRegistry runtimeRegistry,
        ITwilioWebhookSignatureValidator signatureValidator,
        ITenantConnectionStore tenantConnectionStore,
        ILogger<TwilioVoiceWebhookController> logger)
    {
        _eventTransport = eventTransport;
        _audit = audit;
        _voiceSessionOrchestrator = voiceSessionOrchestrator;
        _runtimeRegistry = runtimeRegistry;
        _signatureValidator = signatureValidator;
        _tenantConnectionStore = tenantConnectionStore;
        _logger = logger;
    }

    [HttpPost("status")]
    public async Task<IActionResult> ReceiveStatus(
        [FromRoute] string tenantId,
        [FromForm] TwilioVoiceStatusForm form,
        CancellationToken ct)
    {
        var signatureValid = await _signatureValidator.IsValidAsync(tenantId, Request, ct);
        if (!signatureValid)
            return Unauthorized(new { error = "Invalid Twilio signature." });

        var normalizedStatus = (form.CallStatus ?? string.Empty).Trim().ToLowerInvariant();
        var channel = string.Equals(form.Channel, "callcenter", StringComparison.OrdinalIgnoreCase)
            ? "callcenter"
            : "voice";
        var providerDefaults = await ResolveVoiceProviderDefaultsAsync(tenantId, channel, ct);
        var session = await _voiceSessionOrchestrator.HandleStatusCallbackAsync(
            new VoiceStatusCallbackRequest
            {
                TenantId = tenantId,
                ChannelKey = channel,
                CallSid = form.CallSid ?? Guid.NewGuid().ToString("N"),
                CallStatus = form.CallStatus ?? "unknown",
                From = form.From,
                To = form.To,
                Direction = form.Direction,
                CallDuration = form.CallDuration,
                SessionIdHint = form.SessionId
            },
            ct);
        var sessionId = session.SessionId;

        object typedEvent = normalizedStatus switch
        {
            "queued" or "initiated" or "ringing" or "in-progress" or "answered" => new CallStartedEvent
            {
                TenantId = tenantId,
                ChannelId = channel,
                SessionId = sessionId,
                CallId = form.CallSid ?? session.CallId,
                PhoneNumber = form.To ?? string.Empty
            },
            _ => new CallEndedEvent
            {
                TenantId = tenantId,
                ChannelId = channel,
                SessionId = sessionId,
                CallId = form.CallSid ?? session.CallId,
                Status = normalizedStatus
            }
        };

        await _eventTransport.PublishAsync(new AgentEvent
        {
            EventType = $"connect.call.status.{normalizedStatus}",
            TenantId = tenantId,
            AgentKey = "voice-runtime",
            SessionId = sessionId,
            CorrelationId = form.CallSid,
            Headers = new Dictionary<string, string>
            {
                ["provider"] = providerDefaults.CallControlPreferredProvider,
                ["channel"] = channel,
                ["providerCandidates.callControl"] = providerDefaults.CallControlProvidersCsv,
                ["providerCandidates.stt"] = providerDefaults.SttProvidersCsv,
                ["providerCandidates.tts"] = providerDefaults.TtsProvidersCsv
            },
            Payload = JsonSerializer.Serialize(typedEvent)
        }, ct);

        await _audit.RecordStudioActionAsync(
            tenantId,
            "twilio-webhook",
            "twilio.voice.status.received",
            form.CallSid ?? "voice",
            new
            {
                sessionId,
                form.CallSid,
                form.CallStatus,
                form.From,
                form.To,
                channel,
                form.Direction,
                form.CallDuration,
                providerRouting = new
                {
                    preferredProviders = new
                    {
                        callControl = providerDefaults.CallControlPreferredProvider,
                        stt = providerDefaults.SttPreferredProvider,
                        tts = providerDefaults.TtsPreferredProvider
                    },
                    providerChains = new
                    {
                        callControl = providerDefaults.CallControlProvidersCsv,
                        stt = providerDefaults.SttProvidersCsv,
                        tts = providerDefaults.TtsProvidersCsv
                    }
                }
            },
            form.CallSid,
            ct);

        _logger.LogInformation(
            "Received Twilio voice status callback. Tenant={TenantId} SessionId={SessionId} CallSid={CallSid} Status={Status} Channel={Channel}",
            tenantId,
            sessionId,
            form.CallSid,
            form.CallStatus,
            channel);

        return Ok(new { status = "accepted" });
    }

    [HttpPost("incoming")]
    public async Task<IActionResult> ReceiveIncoming(
        [FromRoute] string tenantId,
        [FromForm] TwilioVoiceStatusForm form,
        CancellationToken ct)
    {
        var signatureValid = await _signatureValidator.IsValidAsync(tenantId, Request, ct);
        if (!signatureValid)
            return Unauthorized(new { error = "Invalid Twilio signature." });

        var channel = string.Equals(form.Channel, "callcenter", StringComparison.OrdinalIgnoreCase)
            ? "callcenter"
            : "voice";
        var providerDefaults = await ResolveVoiceProviderDefaultsAsync(tenantId, channel, ct);

        var session = await _voiceSessionOrchestrator.HandleStatusCallbackAsync(
            new VoiceStatusCallbackRequest
            {
                TenantId = tenantId,
                ChannelKey = channel,
                CallSid = form.CallSid ?? Guid.NewGuid().ToString("N"),
                CallStatus = string.IsNullOrWhiteSpace(form.CallStatus) ? "initiated" : form.CallStatus!,
                From = form.From,
                To = form.To,
                Direction = form.Direction,
                CallDuration = form.CallDuration,
                SessionIdHint = form.SessionId
            },
            ct);

        var callReceived = new CallStartedEvent
        {
            TenantId = tenantId,
            ChannelId = channel,
            SessionId = session.SessionId,
            CallId = form.CallSid ?? session.CallId,
            PhoneNumber = form.From ?? string.Empty
        };

        await _eventTransport.PublishAsync(new AgentEvent
        {
            EventType = "connect.call.received",
            TenantId = tenantId,
            AgentKey = "voice-runtime",
            SessionId = session.SessionId,
            CorrelationId = form.CallSid,
            Headers = new Dictionary<string, string>
            {
                ["provider"] = providerDefaults.CallControlPreferredProvider,
                ["channel"] = channel,
                ["providerCandidates.callControl"] = providerDefaults.CallControlProvidersCsv,
                ["providerCandidates.stt"] = providerDefaults.SttProvidersCsv,
                ["providerCandidates.tts"] = providerDefaults.TtsProvidersCsv
            },
            Payload = JsonSerializer.Serialize(callReceived)
        }, ct);

        await _audit.RecordStudioActionAsync(
            tenantId,
            "twilio-webhook",
            "twilio.voice.incoming.received",
            form.CallSid ?? "voice",
            new
            {
                sessionId = session.SessionId,
                form.CallSid,
                form.From,
                form.To,
                channel,
                form.Direction,
                providerRouting = new
                {
                    preferredProviders = new
                    {
                        callControl = providerDefaults.CallControlPreferredProvider,
                        stt = providerDefaults.SttPreferredProvider,
                        tts = providerDefaults.TtsPreferredProvider
                    },
                    providerChains = new
                    {
                        callControl = providerDefaults.CallControlProvidersCsv,
                        stt = providerDefaults.SttProvidersCsv,
                        tts = providerDefaults.TtsProvidersCsv
                    }
                }
            },
            form.CallSid,
            ct);

        _logger.LogInformation(
            "Received Twilio incoming voice callback. Tenant={TenantId} SessionId={SessionId} CallSid={CallSid} Channel={Channel}",
            tenantId,
            session.SessionId,
            form.CallSid,
            channel);

        var runtime = _runtimeRegistry.GetRequired(AgentRuntimeKind.Voice);
        var runtimeResult = await runtime.ExecuteAsync(new AgentRuntimeRequest
        {
            TenantId = tenantId,
            RuntimeKind = AgentRuntimeKind.Voice,
            SessionId = session.SessionId,
            CorrelationId = form.CallSid,
            ThreadId = session.SessionId,
            Channel = channel,
            Metadata = new Dictionary<string, string>
            {
                ["eventType"] = "connect.call.received",
                ["channel"] = channel,
                ["from"] = form.From ?? string.Empty,
                ["to"] = form.To ?? string.Empty,
                ["provider"] = providerDefaults.CallControlPreferredProvider,
                ["providerCandidates.callControl"] = providerDefaults.CallControlProvidersCsv,
                ["providerCandidates.stt"] = providerDefaults.SttProvidersCsv,
                ["providerCandidates.tts"] = providerDefaults.TtsProvidersCsv
            }
        }, ct);

        var sayText = string.IsNullOrWhiteSpace(runtimeResult.Response)
            ? "Hola. Esta llamada ha sido recibida por el asistente de voz."
            : runtimeResult.Response!;
        var escaped = System.Security.SecurityElement.Escape(sayText) ?? "Hola.";
        return Content($"<Response><Say language=\"es-MX\" voice=\"Polly.Mia\">{escaped}</Say></Response>", "text/xml");
    }

    [HttpPost("stream/chunk")]
    public async Task<IActionResult> ReceiveAudioChunk(
        [FromRoute] string tenantId,
        [FromForm] TwilioVoiceMediaForm form,
        CancellationToken ct)
    {
        var signatureValid = await _signatureValidator.IsValidAsync(tenantId, Request, ct);
        if (!signatureValid)
            return Unauthorized(new { error = "Invalid Twilio signature." });

        if (string.IsNullOrWhiteSpace(form.StreamSid) || string.IsNullOrWhiteSpace(form.PayloadBase64))
            return BadRequest(new { error = "Missing stream payload." });

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(form.PayloadBase64);
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "Invalid base64 payload." });
        }

        var channel = string.Equals(form.Channel, "callcenter", StringComparison.OrdinalIgnoreCase)
            ? "callcenter"
            : "voice";
        var providerDefaults = await ResolveVoiceProviderDefaultsAsync(tenantId, channel, ct);
        var sessionId = string.IsNullOrWhiteSpace(form.SessionId)
            ? (form.CallSid ?? form.StreamSid!)
            : form.SessionId!;

        var evt = new AudioChunkReceivedEvent
        {
            TenantId = tenantId,
            SessionId = sessionId,
            StreamId = form.StreamSid!,
            ContentType = string.IsNullOrWhiteSpace(form.ContentType) ? "audio/x-mulaw" : form.ContentType!,
            Payload = payload
        };

        await _eventTransport.PublishAsync(new AgentEvent
        {
            EventType = "connect.call.audio.chunk",
            TenantId = tenantId,
            AgentKey = "voice-runtime",
            SessionId = sessionId,
            CorrelationId = form.CallSid ?? form.StreamSid,
            Headers = new Dictionary<string, string>
            {
                ["provider"] = providerDefaults.CallControlPreferredProvider,
                ["channel"] = channel,
                ["track"] = form.Track ?? "inbound",
                ["sttProvider"] = string.IsNullOrWhiteSpace(form.SttProvider) ? providerDefaults.SttPreferredProvider : form.SttProvider!,
                ["ttsProvider"] = providerDefaults.TtsPreferredProvider,
                ["providerCandidates.callControl"] = providerDefaults.CallControlProvidersCsv,
                ["providerCandidates.stt"] = providerDefaults.SttProvidersCsv,
                ["providerCandidates.tts"] = providerDefaults.TtsProvidersCsv
            },
            Payload = JsonSerializer.Serialize(evt)
        }, ct);

        return Ok(new { status = "accepted", bytes = payload.Length });
    }

    private async Task<VoiceProviderDefaults> ResolveVoiceProviderDefaultsAsync(
        string tenantId,
        string channel,
        CancellationToken ct)
    {
        var connections = await _tenantConnectionStore.GetConnectionsAsync(tenantId, ct);
        var twilio = connections.FirstOrDefault(connection =>
            string.Equals(connection.ConnectorId, "twilio", StringComparison.OrdinalIgnoreCase) &&
            connection.Type == TenantConnectionType.Messaging);

        static string? get(IReadOnlyDictionary<string, string>? config, string key)
            => config is not null && config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;

        var cfg = twilio?.Config;
        var sttPreferred = get(cfg, "sttProvider")
            ?? get(cfg, $"sttProvider.{channel}")
            ?? "openai";
        var ttsPreferred = get(cfg, "ttsProvider")
            ?? get(cfg, $"ttsProvider.{channel}")
            ?? "openai";
        var callPreferred = get(cfg, "callControlProvider")
            ?? get(cfg, $"callControlProvider.{channel}")
            ?? "twilio";

        var sttCsv = get(cfg, "sttProvidersCsv")
            ?? get(cfg, $"sttProvidersCsv.{channel}")
            ?? sttPreferred;
        var ttsCsv = get(cfg, "ttsProvidersCsv")
            ?? get(cfg, $"ttsProvidersCsv.{channel}")
            ?? ttsPreferred;
        var callCsv = get(cfg, "callControlProvidersCsv")
            ?? get(cfg, $"callControlProvidersCsv.{channel}")
            ?? callPreferred;

        return new VoiceProviderDefaults(
            SttPreferredProvider: sttPreferred,
            TtsPreferredProvider: ttsPreferred,
            CallControlPreferredProvider: callPreferred,
            SttProvidersCsv: sttCsv,
            TtsProvidersCsv: ttsCsv,
            CallControlProvidersCsv: callCsv);
    }
}

public sealed record VoiceProviderDefaults(
    string SttPreferredProvider,
    string TtsPreferredProvider,
    string CallControlPreferredProvider,
    string SttProvidersCsv,
    string TtsProvidersCsv,
    string CallControlProvidersCsv);

public sealed record TwilioVoiceStatusForm
{
    public string? CallSid { get; init; }
    public string? CallStatus { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Direction { get; init; }
    public string? CallDuration { get; init; }
    public string? Channel { get; init; }
    public string? SessionId { get; init; }
}

public sealed record TwilioVoiceMediaForm
{
    public string? CallSid { get; init; }
    public string? StreamSid { get; init; }
    public string? Track { get; init; }
    public string? PayloadBase64 { get; init; }
    public string? ContentType { get; init; }
    public string? Channel { get; init; }
    public string? SessionId { get; init; }
    public string? SttProvider { get; init; }
}
