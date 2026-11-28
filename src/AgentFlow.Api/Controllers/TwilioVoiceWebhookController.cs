using System.Text.Json;
using AgentFlow.Abstractions;
using AgentFlow.Api.Connect;
using AgentFlow.Api.TestStudio;
using AgentFlow.Api.Voice;
using AgentFlow.Api.Workflow;
using AgentFlow.Infrastructure.Providers;
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
    private readonly IVoiceConversationService _conversation;
    private readonly AgentFlow.Api.Campaigns.ICampaignCallTracker? _callTracker;
    private readonly string? _defaultCountryCode;
    private readonly ITwilioWebhookSignatureValidator _signatureValidator;
    private readonly ITenantConnectionStore _tenantConnectionStore;
    private readonly ITestStudioSessionStore? _testStudioStore;
    private readonly ILogger<TwilioVoiceWebhookController> _logger;

    public TwilioVoiceWebhookController(
        IAgentEventTransport eventTransport,
        IWorkflowAuditService audit,
        IVoiceSessionOrchestrator voiceSessionOrchestrator,
        IVoiceConversationService conversation,
        ITwilioWebhookSignatureValidator signatureValidator,
        ITenantConnectionStore tenantConnectionStore,
        ILogger<TwilioVoiceWebhookController> logger,
        ITestStudioSessionStore? testStudioStore = null,
        IConfiguration? configuration = null,
        AgentFlow.Api.Campaigns.ICampaignCallTracker? callTracker = null)
    {
        _callTracker = callTracker;
        _eventTransport = eventTransport;
        _audit = audit;
        _voiceSessionOrchestrator = voiceSessionOrchestrator;
        _conversation = conversation;
        _defaultCountryCode = configuration?["Messaging:DefaultCountryCode"];
        _signatureValidator = signatureValidator;
        _tenantConnectionStore = tenantConnectionStore;
        _testStudioStore = testStudioStore;
        _logger = logger;
    }

    [HttpPost("status")]
    public async Task<IActionResult> ReceiveStatus(
        [FromRoute] string tenantId,
        [FromQuery(Name = "channel")] string? channelKey,
        [FromForm] TwilioVoiceStatusForm form,
        CancellationToken ct)
    {
        var signatureValid = await _signatureValidator.IsValidAsync(tenantId, Request, ct);
        if (!signatureValid)
            return Unauthorized(new { error = "Invalid Twilio signature." });

        var normalizedStatus = (form.CallStatus ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(form.SipResponseCode) && normalizedStatus is "no-answer" or "busy" or "failed" or "canceled")
            _logger.LogInformation("Call {CallSid} ended {Status} with SIP {SipResponseCode}.", form.CallSid, normalizedStatus, form.SipResponseCode);
        // A campaign call: its contact and outcome follow what happened on the phone.
        if (_callTracker is not null && !string.IsNullOrWhiteSpace(form.CallSid))
            await _callTracker.ApplyAsync(tenantId, form.CallSid!, normalizedStatus, form.AnsweredBy, form.CallDuration, ct);
        // The callback names the call's line (a channel id) or its type; the type keys the events.
        var line = VoiceConversationService.ChannelKeyOf(channelKey ?? form.Channel);
        var channel = line == "callcenter" ? "callcenter" : "voice";
        var providerDefaults = await ResolveVoiceProviderDefaultsAsync(tenantId, channel, ct);
        var session = await _voiceSessionOrchestrator.HandleStatusCallbackAsync(
            new VoiceStatusCallbackRequest
            {
                TenantId = tenantId,
                ChannelKey = line,
                CallSid = form.CallSid ?? Guid.NewGuid().ToString("N"),
                CallStatus = form.CallStatus ?? "unknown",
                From = form.From,
                To = form.To,
                Direction = form.Direction,
                CallDuration = form.CallDuration,
                SessionIdHint = form.SessionId,
                CustomerAddress = CustomerOf(form)
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

        AppendVoiceTestEvent(
            tenantId,
            form.CallSid,
            "call_status",
            "voice_status",
            normalizedStatus is "completed" or "failed" or "busy" or "no-answer" ? "completed" : "in_progress",
            $"Twilio status: {normalizedStatus}");

        return Ok(new { status = "accepted" });
    }

    /// <summary>
    /// Incoming call on a webhook shared by every tenant (one Twilio account, or subaccounts, whose
    /// numbers all point here): the number called says whose line it is, and that tenant's Twilio
    /// account must have signed the request. A number no line (or more than one tenant) claims is refused.
    /// </summary>
    [HttpPost("/api/v1/webhooks/twilio/voice/incoming")]
    public async Task<IActionResult> ReceiveIncomingForNumber(
        [FromForm] TwilioVoiceStatusForm form,
        [FromServices] AgentFlow.Api.Channels.ChannelLineService lines,
        CancellationToken ct)
    {
        var line = await lines.FindOwnerAsync(form.To, null, AgentFlow.Domain.Aggregates.ChannelLines.IsVoice, ct);
        if (line is null)
        {
            _logger.LogWarning("Shared Twilio voice webhook: no line owns {To}.", form.To);
            return Content(TwilioVoiceTwiml.Response(TwilioVoiceTwiml.Say("Lo siento, este número no está disponible."), TwilioVoiceTwiml.Hangup()), "text/xml");
        }
        return await ReceiveIncoming(line.TenantId, line.Id, form, ct);
    }

    /// <summary>Status of a call on the shared webhook: routed to the tenant whose line the business number is.</summary>
    [HttpPost("/api/v1/webhooks/twilio/voice/status")]
    public async Task<IActionResult> ReceiveStatusForNumber(
        [FromForm] TwilioVoiceStatusForm form,
        [FromServices] AgentFlow.Api.Channels.ChannelLineService lines,
        CancellationToken ct)
    {
        var business = form.Direction?.StartsWith("outbound", StringComparison.OrdinalIgnoreCase) == true ? form.From : form.To;
        var line = await lines.FindOwnerAsync(business, null, AgentFlow.Domain.Aggregates.ChannelLines.IsVoice, ct);
        if (line is null) return NotFound(new { error = "No line owns this number." });
        return await ReceiveStatus(line.TenantId, line.Id, form, ct);
    }

    [HttpPost("incoming")]
    public async Task<IActionResult> ReceiveIncoming(
        [FromRoute] string tenantId,
        [FromQuery(Name = "channel")] string? channelKey,
        [FromForm] TwilioVoiceStatusForm form,
        CancellationToken ct)
    {
        var signatureValid = await _signatureValidator.IsValidAsync(tenantId, Request, ct);
        if (!signatureValid)
            return Unauthorized(new { error = "Invalid Twilio signature." });

        var requested = VoiceConversationService.ChannelKeyOf(channelKey ?? form.Channel);
        var channel = requested == "callcenter" ? "callcenter" : "voice";
        var providerDefaults = await ResolveVoiceProviderDefaultsAsync(tenantId, channel, ct);
        // The line is the number the customer called: each line answers with its own assistant.
        var line = await _conversation.ResolveLineAsync(tenantId, requested, form.To, ct);
        if (line is null)
        {
            _logger.LogWarning("Incoming Twilio call without an active {Channel} line. Tenant={TenantId} To={To}", channel, tenantId, form.To);
            return Content(await _conversation.GreetAsync(tenantId, requested, form.To, ct), "text/xml");
        }

        var session = await _voiceSessionOrchestrator.HandleStatusCallbackAsync(
            new VoiceStatusCallbackRequest
            {
                TenantId = tenantId,
                ChannelKey = line.Id,
                CallSid = form.CallSid ?? Guid.NewGuid().ToString("N"),
                CallStatus = string.IsNullOrWhiteSpace(form.CallStatus) ? "initiated" : form.CallStatus!,
                From = form.From,
                To = form.To,
                Direction = form.Direction,
                CallDuration = form.CallDuration,
                SessionIdHint = form.SessionId,
                CustomerAddress = CustomerOf(form)
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

        AppendVoiceTestEvent(
            tenantId,
            form.CallSid,
            "call_received",
            "voice_call",
            "received",
            $"Incoming call from {form.From ?? "unknown"}");

        // The line's assistant greets and listens; each turn comes back to "gather".
        return Content(await _conversation.GreetAsync(tenantId, line.Id, form.To, ct), "text/xml");
    }

    /// <summary>
    /// What the customer said on the call (Twilio's speech transcription). It goes through the same
    /// pipeline as a WhatsApp message and the assistant's reply is spoken back.
    /// </summary>
    [HttpPost("gather")]
    public async Task<IActionResult> ReceiveSpeech(
        [FromRoute] string tenantId,
        [FromQuery] int silences,
        [FromQuery] int turn,
        [FromQuery(Name = "channel")] string? channelKey,
        [FromForm] TwilioVoiceGatherForm form,
        CancellationToken ct)
    {
        var signatureValid = await _signatureValidator.IsValidAsync(tenantId, Request, ct);
        if (!signatureValid)
            return Unauthorized(new { error = "Invalid Twilio signature." });

        var channel = VoiceConversationService.ChannelKeyOf(channelKey ?? form.Channel);
        AppendVoiceTestEvent(tenantId, form.CallSid, "transcript", "voice_speech",
            string.IsNullOrWhiteSpace(form.SpeechResult) ? "empty" : "received",
            string.IsNullOrWhiteSpace(form.SpeechResult) ? "No speech" : $"Customer said: {form.SpeechResult}");

        var twiml = await _conversation.AnswerAsync(new VoiceTurn
        {
            TenantId = tenantId,
            ChannelKey = channel,
            CallSid = form.CallSid,
            From = form.From,
            To = form.To,
            Direction = form.Direction,
            SpeechResult = form.SpeechResult,
            Confidence = form.Confidence,
            Silences = Math.Max(0, silences),
            Turn = Math.Max(0, turn)
        }, ct);
        return Content(twiml, "text/xml");
    }

    /// <summary>
    /// An outbound call was answered (Twilio's answering-machine detection says by whom): a person
    /// hears the opening and talks with the assistant; a voicemail is hung up on.
    /// </summary>
    [HttpPost("answer")]
    public async Task<IActionResult> ReceiveAnswer(
        [FromRoute] string tenantId,
        [FromQuery(Name = "channel")] string? channelKey,
        [FromQuery] string? script,
        [FromForm] TwilioVoiceAnswerForm form,
        CancellationToken ct)
    {
        var signatureValid = await _signatureValidator.IsValidAsync(tenantId, Request, ct);
        if (!signatureValid)
            return Unauthorized(new { error = "Invalid Twilio signature." });

        var channel = VoiceConversationService.ChannelKeyOf(channelKey);
        _logger.LogInformation("Outbound call answered. Tenant={TenantId} CallSid={CallSid} AnsweredBy={AnsweredBy}",
            tenantId, form.CallSid, form.AnsweredBy);
        AppendVoiceTestEvent(tenantId, form.CallSid, "call_answered", "voice_call", form.AnsweredBy ?? "unknown",
            $"Answered by {form.AnsweredBy ?? "unknown"}");
        if (_callTracker is not null && !string.IsNullOrWhiteSpace(form.CallSid))
            await _callTracker.ApplyAsync(tenantId, form.CallSid!, "answered", form.AnsweredBy, null, ct);
        return Content(await _conversation.AnsweredAsync(tenantId, channel, script, form.AnsweredBy, ct), "text/xml");
    }

    private string? CustomerOf(TwilioVoiceStatusForm form)
    {
        var raw = VoiceStatusCallbackRequest.CustomerOf(form.Direction, form.From, form.To);
        var address = AgentFlow.Api.Channels.CustomerAddress.Normalize(raw, AgentFlow.Domain.Aggregates.ChannelType.Voice, _defaultCountryCode);
        return address.Value.Length == 0 ? null : address.Value;
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

        AppendVoiceTestEvent(
            tenantId,
            form.CallSid ?? form.StreamSid,
            "audio_chunk",
            "audio",
            "accepted",
            $"Audio chunk accepted ({payload.Length} bytes).");

        return Ok(new { status = "accepted", bytes = payload.Length });
    }

    private void AppendVoiceTestEvent(
        string tenantId,
        string? correlationId,
        string stage,
        string payloadType,
        string status,
        string message)
    {
        if (_testStudioStore is null || string.IsNullOrWhiteSpace(correlationId)) return;
        var session = _testStudioStore.FindByCorrelationId(tenantId, correlationId, AgentRuntimeKind.Voice);
        if (session is null) return;

        _testStudioStore.AppendEvent(tenantId, session.TestSessionId, new TestStudioEvent
        {
            Stage = stage,
            Direction = "inbound",
            PayloadType = payloadType,
            Status = status,
            CorrelationId = correlationId,
            Message = message
        });
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
    /// <summary>With answering-machine detection: human, machine_start, machine_end_*, fax, unknown.</summary>
    public string? AnsweredBy { get; init; }
    /// <summary>The carrier's SIP answer on a call that did not connect (480 unavailable, 486 busy, 603 rejected...).</summary>
    public string? SipResponseCode { get; init; }
}

public sealed record TwilioVoiceAnswerForm
{
    public string? CallSid { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? AnsweredBy { get; init; }
}

public sealed record TwilioVoiceGatherForm
{
    public string? CallSid { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Direction { get; init; }
    public string? SpeechResult { get; init; }
    public string? Confidence { get; init; }
    public string? Channel { get; init; }
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
