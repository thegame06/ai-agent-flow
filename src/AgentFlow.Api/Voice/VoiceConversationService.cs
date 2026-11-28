using AgentFlow.Abstractions;
using AgentFlow.Api.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Infrastructure.Providers;

namespace AgentFlow.Api.Voice;

/// <summary>One turn of a phone call: what Twilio sent after listening to the customer.</summary>
public sealed record VoiceTurn
{
    public required string TenantId { get; init; }
    public string ChannelKey { get; init; } = "voice";
    public string? CallSid { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Direction { get; init; }
    public string? SpeechResult { get; init; }
    public string? Confidence { get; init; }
    /// <summary>How many silent turns in a row came before this one.</summary>
    public int Silences { get; init; }
    /// <summary>How many turns the call has had before this one.</summary>
    public int Turn { get; init; }
}

public sealed record VoiceCallRequest
{
    public required string TenantId { get; init; }
    public required string PhoneNumber { get; init; }
    /// <summary>What the call opens with; the voice channel's greeting when empty.</summary>
    public string? Script { get; init; }
    public string ChannelKey { get; init; } = "voice";
    public string? ConnectionId { get; init; }
    public string? RequestedBy { get; init; }
}

public sealed record VoiceCallResult(string ProviderCallId, string ProviderStatus, string PhoneNumber, string ChannelId);

public interface IVoiceConversationService
{
    Task<ChannelDefinition?> ResolveChannelAsync(string tenantId, string channelKey, CancellationToken ct);
    /// <summary>The TwiML that answers a call: the channel's greeting, then listening.</summary>
    Task<string> GreetAsync(string tenantId, string channelKey, CancellationToken ct);
    /// <summary>The customer's words go through the shared inbound pipeline; the assistant's reply is spoken back.</summary>
    Task<string> AnswerAsync(VoiceTurn turn, CancellationToken ct);
    /// <summary>
    /// An outbound call was answered: a person hears the opening and talks with the assistant; a
    /// voicemail or fax is hung up on (the assistant must not talk to a recording).
    /// </summary>
    Task<string> AnsweredAsync(string tenantId, string channelKey, string? script, string? answeredBy, CancellationToken ct);
    /// <summary>Calls a customer; when they answer, the call is a conversation with the assistant.</summary>
    Task<VoiceCallResult> PlaceCallAsync(VoiceCallRequest request, CancellationToken ct);
}

/// <summary>
/// Phone calls are one more channel of the customer's single conversation: Twilio transcribes each
/// turn, <see cref="IInboundMessageIngest"/> treats it exactly as a WhatsApp/web message (customer,
/// session, thread, assistant, inbox) and the reply is spoken with Twilio's TTS.
/// </summary>
public sealed class VoiceConversationService : IVoiceConversationService
{
    public const string GreetingKey = "Greeting";
    public const int MaxSilences = 2;
    /// <summary>Turns per call before it is closed; a loop (a recording talking back) must end.</summary>
    public const int MaxTurns = 20;
    /// <summary>Longest an outbound call may last.</summary>
    public const int MaxCallSeconds = 900;
    private const string Unavailable = "Lo siento, esta línea no está disponible en este momento. Por favor escríbenos por WhatsApp.";
    private const string NotUnderstood = "Disculpa, no te escuché bien. ¿Me lo repites, por favor?";
    private const string Failed = "Disculpa, tuve un problema para procesar tu solicitud. Un asesor te contactará pronto.";

    private readonly IChannelDefinitionRepository _channels;
    private readonly IInboundMessageIngest _ingest;
    private readonly IProviderResolver _providers;
    private readonly ILogger<VoiceConversationService> _logger;
    private readonly string? _publicBaseUrl;
    private readonly string? _defaultCountryCode;

    public VoiceConversationService(
        IChannelDefinitionRepository channels,
        IInboundMessageIngest ingest,
        IProviderResolver providers,
        ILogger<VoiceConversationService> logger,
        IConfiguration? configuration = null)
    {
        _channels = channels;
        _ingest = ingest;
        _providers = providers;
        _logger = logger;
        _publicBaseUrl = configuration?["Webhooks:PublicBaseUrl"];
        _defaultCountryCode = configuration?["Messaging:DefaultCountryCode"];
    }

    public async Task<ChannelDefinition?> ResolveChannelAsync(string tenantId, string channelKey, CancellationToken ct)
    {
        var type = string.Equals(channelKey, "callcenter", StringComparison.OrdinalIgnoreCase) ? ChannelType.CallCenter : ChannelType.Voice;
        var channels = await _channels.GetByTypeAsync(type, tenantId, ct);
        return channels.FirstOrDefault(x => x.Status == ChannelStatus.Active);
    }

    public async Task<string> GreetAsync(string tenantId, string channelKey, CancellationToken ct)
    {
        var channel = await ResolveChannelAsync(tenantId, channelKey, ct);
        if (channel is null)
            return TwilioVoiceTwiml.Response(TwilioVoiceTwiml.Say(Unavailable), TwilioVoiceTwiml.Hangup());
        return Listen(tenantId, channelKey, GreetingOf(channel), silences: 0, turn: 0);
    }

    public async Task<string> AnsweredAsync(string tenantId, string channelKey, string? script, string? answeredBy, CancellationToken ct)
    {
        if (IsMachine(answeredBy))
            return TwilioVoiceTwiml.Response(TwilioVoiceTwiml.Hangup());
        var channel = await ResolveChannelAsync(tenantId, channelKey, ct);
        if (channel is null)
            return TwilioVoiceTwiml.Response(TwilioVoiceTwiml.Hangup());
        return Listen(tenantId, channelKey, string.IsNullOrWhiteSpace(script) ? GreetingOf(channel) : script!, silences: 0, turn: 0);
    }

    /// <summary>Twilio's AnsweredBy: machine_start, machine_end_beep/silence/other, fax; human or unknown is talked to.</summary>
    public static bool IsMachine(string? answeredBy)
        => answeredBy is not null &&
           (answeredBy.StartsWith("machine", StringComparison.OrdinalIgnoreCase) || string.Equals(answeredBy, "fax", StringComparison.OrdinalIgnoreCase));

    public async Task<string> AnswerAsync(VoiceTurn turn, CancellationToken ct)
    {
        var channel = await ResolveChannelAsync(turn.TenantId, turn.ChannelKey, ct);
        if (channel is null)
            return TwilioVoiceTwiml.Response(TwilioVoiceTwiml.Say(Unavailable), TwilioVoiceTwiml.Hangup());

        if (turn.Turn >= MaxTurns)
            return TwilioVoiceTwiml.Response(TwilioVoiceTwiml.Say(TwilioVoiceTwiml.Goodbye), TwilioVoiceTwiml.Hangup());

        var speech = turn.SpeechResult?.Trim();
        if (string.IsNullOrEmpty(speech))
        {
            return turn.Silences + 1 >= MaxSilences
                ? TwilioVoiceTwiml.Response(TwilioVoiceTwiml.Say(TwilioVoiceTwiml.Goodbye), TwilioVoiceTwiml.Hangup())
                : Listen(turn.TenantId, turn.ChannelKey, NotUnderstood, turn.Silences + 1, turn.Turn + 1);
        }

        var customer = CustomerSide(turn.Direction, turn.From, turn.To);
        string reply;
        try
        {
            var result = await _ingest.IngestAsync(new InboundMessage
            {
                TenantId = turn.TenantId,
                Channel = channel,
                Provider = "twilio",
                From = customer ?? string.Empty,
                Text = speech,
                BusinessAddress = BusinessSide(turn.Direction, turn.From, turn.To),
                // No CorrelationId/ExternalId: the pipeline dedupes on them, and every turn of a
                // call shares the CallSid. Twilio has no per-turn id; the call id goes in metadata.
                Metadata = new Dictionary<string, string>
                {
                    ["voice.call_id"] = turn.CallSid ?? string.Empty,
                    ["voice.speech_confidence"] = turn.Confidence ?? string.Empty,
                    ["voice.direction"] = turn.Direction ?? string.Empty
                }
            }, inBackground: false, ct);

            reply = result.Status == InboundStatus.Processed && !string.IsNullOrWhiteSpace(result.Reply?.Content)
                ? result.Reply!.Content
                : Failed;
            if (reply == Failed)
                _logger.LogWarning("Voice turn without a reply. Tenant={TenantId} CallSid={CallSid} Status={Status} Reason={Reason}",
                    turn.TenantId, turn.CallSid, result.Status, result.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice turn failed. Tenant={TenantId} CallSid={CallSid}", turn.TenantId, turn.CallSid);
            reply = Failed;
        }

        return Listen(turn.TenantId, turn.ChannelKey, reply, silences: 0, turn.Turn + 1);
    }

    public async Task<VoiceCallResult> PlaceCallAsync(VoiceCallRequest request, CancellationToken ct)
    {
        var channel = await ResolveChannelAsync(request.TenantId, request.ChannelKey, ct)
            ?? throw new InvalidOperationException("No hay un canal de voz activo. Crea uno en Canales con la conexión de Twilio.");
        var address = CustomerAddress.Normalize(request.PhoneNumber, channel.Type,
            channel.Config.GetValueOrDefault("DefaultCountryCode") ?? _defaultCountryCode);
        if (!address.IsPhone)
            throw new ArgumentException("El número de teléfono no es válido.", nameof(request));

        var resolved = await _providers.ResolveRequiredAsync<IVoiceCallProviderAdapter>(new ProviderResolutionContext
        {
            TenantId = request.TenantId,
            Capability = CommunicationCapabilities.CallOutbound,
            Channel = request.ChannelKey,
            ConnectionId = request.ConnectionId ?? channel.Config.GetValueOrDefault("ConnectionId"),
            Metadata = new Dictionary<string, string> { ["channelId"] = channel.Id }
        }, ct);

        var script = string.IsNullOrWhiteSpace(request.Script) ? GreetingOf(channel) : request.Script!.Trim();
        var result = await resolved.Adapter.PlaceCallAsync(resolved.Connection, new ProviderVoiceCallRequest
        {
            PhoneNumber = address.Value,
            Script = script,
            ConversationUrl = ConversationUrl(request.TenantId, request.ChannelKey, 0, 0, absolute: true),
            AnswerUrl = WebhookUrl(request.TenantId, "answer",
                $"channel={Uri.EscapeDataString(request.ChannelKey)}&script={Uri.EscapeDataString(script)}", absolute: true),
            TimeLimitSeconds = MaxCallSeconds,
            StatusCallbackUrl = WebhookUrl(request.TenantId, "status", $"channel={request.ChannelKey}", absolute: true),
            Metadata = new Dictionary<string, string>
            {
                ["channelId"] = channel.Id,
                ["requestedBy"] = request.RequestedBy ?? string.Empty
            }
        }, ct);

        _logger.LogInformation("Placed voice call. Tenant={TenantId} CallSid={CallSid} Provider={Provider}",
            request.TenantId, result.ProviderCallId, resolved.Adapter.ProviderId);
        return new VoiceCallResult(result.ProviderCallId, result.ProviderStatus, address.Value, channel.Id);
    }

    public static string GreetingOf(ChannelDefinition channel)
        => channel.Config.GetValueOrDefault(GreetingKey) is { Length: > 0 } greeting && !string.IsNullOrWhiteSpace(greeting)
            ? greeting.Trim()
            : TwilioVoiceTwiml.DefaultGreeting;

    /// <summary>The customer on a call: who called (inbound) or who was called (outbound).</summary>
    public static string? CustomerSide(string? direction, string? from, string? to)
        => VoiceStatusCallbackRequest.CustomerOf(direction, from, to);

    private static string? BusinessSide(string? direction, string? from, string? to)
        => direction?.StartsWith("outbound", StringComparison.OrdinalIgnoreCase) == true ? from : to;

    private string Listen(string tenantId, string channelKey, string prompt, int silences, int turn)
        => TwilioVoiceTwiml.Response(
            TwilioVoiceTwiml.Gather(ConversationUrl(tenantId, channelKey, silences, turn, absolute: false), prompt),
            TwilioVoiceTwiml.Say(TwilioVoiceTwiml.Goodbye),
            TwilioVoiceTwiml.Hangup());

    /// <summary>
    /// Where Twilio posts each turn. TwiML returned from a webhook may use a path; TwiML sent with a
    /// new outbound call has no request to be relative to, so it needs the public URL.
    /// </summary>
    public string ConversationUrl(string tenantId, string channelKey, int silences, int turn, bool absolute)
        => WebhookUrl(tenantId, "gather", $"channel={Uri.EscapeDataString(channelKey)}&silences={silences}&turn={turn}", absolute);

    private string WebhookUrl(string tenantId, string action, string query, bool absolute)
    {
        var path = $"/api/v1/tenants/{Uri.EscapeDataString(tenantId)}/webhooks/twilio/voice/{action}?{query}";
        if (!string.IsNullOrWhiteSpace(_publicBaseUrl))
            return _publicBaseUrl!.TrimEnd('/') + path;
        if (absolute)
            throw new InvalidOperationException("Configura Webhooks:PublicBaseUrl para que Twilio pueda avisar a AgentFlow durante la llamada.");
        return path;
    }
}
