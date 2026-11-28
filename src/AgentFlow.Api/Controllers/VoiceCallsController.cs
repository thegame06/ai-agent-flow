using AgentFlow.Api.Connect;
using AgentFlow.Api.Voice;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

/// <summary>Phone calls with the assistant: call a customer, and check what the voice line needs.</summary>
[ApiController]
[Route("api/v1/tenants/{tenantId}/voice")]
[Authorize]
public sealed class VoiceCallsController : ControllerBase
{
    private readonly IVoiceConversationService _conversation;
    private readonly ITenantConnectionStore _connections;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ITwilioCredentials _twilio;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<VoiceCallsController> _logger;
    private readonly string? _publicBaseUrl;

    public VoiceCallsController(
        IVoiceConversationService conversation,
        ITenantConnectionStore connections,
        ITenantContextAccessor tenantContext,
        ITwilioCredentials twilio,
        IHttpClientFactory http,
        IConfiguration configuration,
        ILogger<VoiceCallsController> logger)
    {
        _logger = logger;
        _conversation = conversation;
        _twilio = twilio;
        _http = http;
        _connections = connections;
        _tenantContext = tenantContext;
        _publicBaseUrl = configuration["Webhooks:PublicBaseUrl"];
    }

    /// <summary>Calls a customer; when they answer, the opening is spoken and the assistant takes the conversation.</summary>
    [HttpPost("calls")]
    public async Task<IActionResult> PlaceCall([FromRoute] string tenantId, [FromBody] PlaceVoiceCallBody body, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        if (string.IsNullOrWhiteSpace(body.PhoneNumber))
            return BadRequest(new { message = "Indica el número a llamar." });

        try
        {
            var result = await _conversation.PlaceCallAsync(new VoiceCallRequest
            {
                TenantId = tenantId,
                PhoneNumber = body.PhoneNumber!,
                Script = body.Script,
                ChannelKey = string.Equals(body.Channel, "callcenter", StringComparison.OrdinalIgnoreCase) ? "callcenter" : "voice",
                ConnectionId = body.ConnectionId,
                RequestedBy = _tenantContext.Current?.UserId
            }, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { message = ex.Message });
        }
    }

    /// <summary>What the voice line needs: an active voice channel, a Twilio number for calls and a public URL.</summary>
    [HttpGet("setup")]
    public async Task<IActionResult> Setup([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var channel = await _conversation.ResolveChannelAsync(tenantId, "voice", ct);
        var connections = await _connections.GetConnectionsAsync(tenantId, ct);
        var twilio = connections.FirstOrDefault(x => x.Id == channel?.Config.GetValueOrDefault("ConnectionId"))
            ?? connections.FirstOrDefault(x => string.Equals(x.ConnectorId, "twilio", StringComparison.OrdinalIgnoreCase));
        var voiceNumber = twilio?.Config.GetValueOrDefault("voicePhoneNumber");
        var baseUrl = _publicBaseUrl?.TrimEnd('/');

        var missing = new List<string>();
        if (channel is null) missing.Add("Crea y activa un canal de Voz en Canales.");
        if (twilio is null) missing.Add("Crea la integración de Twilio.");
        else if (string.IsNullOrWhiteSpace(voiceNumber)) missing.Add("Guarda en la integración de Twilio el número de voz (voicePhoneNumber); el remitente de WhatsApp no puede llamar.");
        if (string.IsNullOrWhiteSpace(baseUrl)) missing.Add("Configura Webhooks:PublicBaseUrl.");

        var incomingUrl = baseUrl is null ? null : $"{baseUrl}/api/v1/tenants/{tenantId}/webhooks/twilio/voice/incoming";
        var numbers = twilio is null ? null : await TwilioNumbersAsync(tenantId, twilio.Id, incomingUrl, ct);
        if (numbers is { Count: > 0 } && !string.IsNullOrWhiteSpace(voiceNumber) &&
            numbers.FirstOrDefault(n => n.PhoneNumber == voiceNumber) is { AnswersHere: false })
            missing.Add($"En Twilio, apunta el webhook de voz de {voiceNumber} a {incomingUrl} para recibir llamadas.");

        return Ok(new
        {
            ready = missing.Count == 0,
            missing,
            channelId = channel?.Id,
            channelName = channel?.Name,
            connectionId = twilio?.Id,
            voicePhoneNumber = voiceNumber,
            incomingWebhookUrl = incomingUrl,
            twilioNumbers = numbers,
            statusCallbackUrl = baseUrl is null ? null : $"{baseUrl}/api/v1/tenants/{tenantId}/webhooks/twilio/voice/status"
        });
    }

    /// <summary>
    /// Points a Twilio number's incoming calls at AgentFlow (its Voice URL), so calls to it are
    /// answered by the assistant, and saves it as the number outbound calls are placed from.
    /// </summary>
    [HttpPost("setup/connect-number")]
    public async Task<IActionResult> ConnectNumber([FromRoute] string tenantId, [FromBody] ConnectVoiceNumberBody body, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        if (string.IsNullOrWhiteSpace(body.PhoneNumber)) return BadRequest(new { message = "Indica el número de Twilio." });
        var baseUrl = _publicBaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return UnprocessableEntity(new { message = "Configura Webhooks:PublicBaseUrl." });

        var channel = await _conversation.ResolveChannelAsync(tenantId, "voice", ct);
        var connections = await _connections.GetConnectionsAsync(tenantId, ct);
        var twilio = connections.FirstOrDefault(x => x.Id == (body.ConnectionId ?? channel?.Config.GetValueOrDefault("ConnectionId")))
            ?? connections.FirstOrDefault(x => string.Equals(x.ConnectorId, "twilio", StringComparison.OrdinalIgnoreCase));
        if (twilio is null) return UnprocessableEntity(new { message = "Crea la integración de Twilio." });
        var account = await _twilio.GetAsync(tenantId, twilio.Id, ct);
        if (account is null) return UnprocessableEntity(new { message = "La integración de Twilio no tiene Account SID o token." });

        var client = _http.CreateClient();
        var auth = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{account.AccountSid}:{account.AuthToken}")));
        using var find = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.twilio.com/2010-04-01/Accounts/{account.AccountSid}/IncomingPhoneNumbers.json?PhoneNumber={Uri.EscapeDataString(body.PhoneNumber!.Trim())}");
        find.Headers.Authorization = auth;
        using var found = await client.SendAsync(find, ct);
        if (!found.IsSuccessStatusCode) return StatusCode(502, new { message = $"Twilio respondió {(int)found.StatusCode} al buscar el número." });
        using var foundDoc = System.Text.Json.JsonDocument.Parse(await found.Content.ReadAsStringAsync(ct));
        var number = foundDoc.RootElement.GetProperty("incoming_phone_numbers").EnumerateArray().FirstOrDefault();
        if (number.ValueKind != System.Text.Json.JsonValueKind.Object)
            return NotFound(new { message = "Ese número no está en la cuenta de Twilio." });

        var incomingUrl = $"{baseUrl}/api/v1/tenants/{tenantId}/webhooks/twilio/voice/incoming";
        using var update = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{account.AccountSid}/IncomingPhoneNumbers/{number.GetProperty("sid").GetString()}.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["VoiceUrl"] = incomingUrl,
                ["VoiceMethod"] = "POST",
                ["StatusCallback"] = $"{baseUrl}/api/v1/tenants/{tenantId}/webhooks/twilio/voice/status",
                ["StatusCallbackMethod"] = "POST"
            })
        };
        update.Headers.Authorization = auth;
        using var updated = await client.SendAsync(update, ct);
        if (!updated.IsSuccessStatusCode) return StatusCode(502, new { message = $"Twilio respondió {(int)updated.StatusCode} al configurar el número." });

        var config = new Dictionary<string, string>(twilio.Config, StringComparer.OrdinalIgnoreCase) { ["voicePhoneNumber"] = body.PhoneNumber!.Trim() };
        await _connections.UpsertConnectionAsync(twilio with { Config = config, UpdatedAt = DateTimeOffset.UtcNow }, ct);
        return Ok(new { phoneNumber = body.PhoneNumber!.Trim(), voiceUrl = incomingUrl, connectionId = twilio.Id });
    }

    /// <summary>The account's Twilio numbers that can take calls, and whether each one's voice webhook points here.</summary>
    private async Task<List<TwilioVoiceNumber>?> TwilioNumbersAsync(string tenantId, string connectionId, string? incomingUrl, CancellationToken ct)
    {
        try
        {
            var account = await _twilio.GetAsync(tenantId, connectionId, ct);
            if (account is null)
            {
                _logger.LogWarning("Twilio connection {ConnectionId} has no account SID or auth token.", connectionId);
                return null;
            }
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twilio.com/2010-04-01/Accounts/{account.AccountSid}/IncomingPhoneNumbers.json?PageSize=50");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{account.AccountSid}:{account.AuthToken}")));
            using var response = await _http.CreateClient().SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twilio refused listing the account's numbers: {Status}", (int)response.StatusCode);
                return null;
            }
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var list = new List<TwilioVoiceNumber>();
            foreach (var n in doc.RootElement.GetProperty("incoming_phone_numbers").EnumerateArray())
            {
                var canCall = n.TryGetProperty("capabilities", out var caps) && caps.TryGetProperty("voice", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
                if (!canCall) continue;
                var voiceUrl = n.TryGetProperty("voice_url", out var u) ? u.GetString() : null;
                list.Add(new TwilioVoiceNumber(
                    n.GetProperty("phone_number").GetString() ?? string.Empty,
                    voiceUrl,
                    incomingUrl is not null && string.Equals(voiceUrl?.TrimEnd('/'), incomingUrl, StringComparison.OrdinalIgnoreCase)));
            }
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list the Twilio account's numbers.");
            return null;
        }
    }

    private bool CanAccess(string tenantId)
    {
        var context = _tenantContext.Current;
        return context is not null && (context.TenantId == tenantId || context.IsPlatformAdmin);
    }
}

public sealed record TwilioVoiceNumber(string PhoneNumber, string? VoiceUrl, bool AnswersHere);

public sealed record ConnectVoiceNumberBody
{
    public string? PhoneNumber { get; init; }
    public string? ConnectionId { get; init; }
}

public sealed record PlaceVoiceCallBody
{
    public string? PhoneNumber { get; init; }
    public string? Script { get; init; }
    public string? Channel { get; init; }
    public string? ConnectionId { get; init; }
}
