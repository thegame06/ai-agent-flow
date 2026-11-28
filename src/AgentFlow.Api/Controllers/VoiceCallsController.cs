using AgentFlow.Api.Connect;
using AgentFlow.Api.Voice;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
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
    private readonly IChannelDefinitionRepository? _channels;
    private readonly string? _publicBaseUrl;

    public VoiceCallsController(
        IVoiceConversationService conversation,
        ITenantConnectionStore connections,
        ITenantContextAccessor tenantContext,
        ITwilioCredentials twilio,
        IHttpClientFactory http,
        IConfiguration configuration,
        ILogger<VoiceCallsController> logger,
        IChannelDefinitionRepository? channels = null)
    {
        _channels = channels;
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
                Mode = VoiceConversationService.ParseMode(body.Mode),
                // The line to call from (its number is the caller id and its assistant takes the call).
                ChannelKey = VoiceConversationService.ChannelKeyOf(body.ChannelId ?? body.Channel),
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
        var verified = twilio is null ? null : await VerifiedCallerIdsAsync(tenantId, twilio.Id, ct);
        if (numbers is { Count: > 0 } && !string.IsNullOrWhiteSpace(voiceNumber) &&
            numbers.FirstOrDefault(n => n.PhoneNumber == voiceNumber) is { AnswersHere: false })
            missing.Add($"En Twilio, apunta el webhook de voz de {voiceNumber} a {incomingUrl} para recibir llamadas.");

        // Every voice line of the tenant: its number, purpose and whether it is the default one.
        var lines = _channels is null
            ? []
            : (await _channels.GetByTypeAsync(ChannelType.Voice, tenantId, ct))
                .Where(x => x.TenantId == tenantId && x.Status == ChannelStatus.Active)
                .Select(x => new
                {
                    channelId = x.Id,
                    name = x.Name,
                    phoneNumber = ChannelLines.NumberOf(x) ?? voiceNumber,
                    ownNumber = ChannelLines.NumberOf(x) is not null,
                    purpose = ChannelLines.PurposeOf(x),
                    isDefault = ChannelLines.IsDefault(x),
                    contactHours = x.Config.GetValueOrDefault(ChannelLines.ContactHoursKey)
                })
                .Cast<object>()
                .ToList();

        return Ok(new
        {
            ready = missing.Count == 0,
            missing,
            lines,
            channelId = channel?.Id,
            channelName = channel?.Name,
            connectionId = twilio?.Id,
            voicePhoneNumber = voiceNumber,
            incomingWebhookUrl = incomingUrl,
            twilioNumbers = numbers,
            // A Twilio trial account can only call these numbers (verified in Twilio's console).
            verifiedCallerIds = verified,
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

    /// <summary>
    /// What Twilio knows about a call: status, numbers, timing and the errors or warnings it logged.
    /// The answer to "it did not ring": a carrier rejection or a blocked destination shows up here.
    /// </summary>
    [HttpGet("calls/{callSid}")]
    public async Task<IActionResult> GetCall([FromRoute] string tenantId, [FromRoute] string callSid, [FromQuery] string? connectionId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        if (string.IsNullOrWhiteSpace(callSid) || !callSid.StartsWith("CA", StringComparison.Ordinal) || !callSid.All(char.IsAsciiLetterOrDigit))
            return BadRequest(new { message = "El id de la llamada no es válido (empieza con CA)." });

        var connections = await _connections.GetConnectionsAsync(tenantId, ct);
        var twilioConnections = connections
            .Where(x => string.Equals(x.ConnectorId, "twilio", StringComparison.OrdinalIgnoreCase) && (connectionId is null || x.Id == connectionId))
            .ToList();
        // A call belongs to one of the tenant's Twilio accounts: ask each until one knows it.
        foreach (var connection in twilioConnections)
        {
            var account = await _twilio.GetAsync(tenantId, connection.Id, ct);
            if (account is null) continue;
            var client = _http.CreateClient();
            var auth = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{account.AccountSid}:{account.AuthToken}")));

            using var callRequest = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twilio.com/2010-04-01/Accounts/{account.AccountSid}/Calls/{callSid}.json");
            callRequest.Headers.Authorization = auth;
            using var callResponse = await client.SendAsync(callRequest, ct);
            if (callResponse.StatusCode == System.Net.HttpStatusCode.NotFound) continue;
            if (!callResponse.IsSuccessStatusCode)
                return StatusCode(502, new { message = $"Twilio respondió {(int)callResponse.StatusCode} al consultar la llamada." });
            using var call = System.Text.Json.JsonDocument.Parse(await callResponse.Content.ReadAsStringAsync(ct));
            string? Field(string name) => call.RootElement.TryGetProperty(name, out var v) && v.ValueKind != System.Text.Json.JsonValueKind.Null ? v.ToString() : null;

            var notices = new List<object>();
            using (var noticeRequest = new HttpRequestMessage(HttpMethod.Get,
                       $"https://api.twilio.com/2010-04-01/Accounts/{account.AccountSid}/Calls/{callSid}/Notifications.json"))
            {
                noticeRequest.Headers.Authorization = auth;
                using var noticeResponse = await client.SendAsync(noticeRequest, ct);
                if (noticeResponse.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(await noticeResponse.Content.ReadAsStringAsync(ct));
                    if (doc.RootElement.TryGetProperty("notifications", out var list))
                        foreach (var n in list.EnumerateArray())
                            notices.Add(new
                            {
                                errorCode = n.TryGetProperty("error_code", out var code) ? code.ToString() : null,
                                level = n.TryGetProperty("log", out var log) ? (log.ToString() == "0" ? "error" : "warning") : null,
                                message = n.TryGetProperty("message_text", out var text) ? Uri.UnescapeDataString(text.ToString().Replace('+', ' ')) : null,
                                moreInfo = n.TryGetProperty("more_info", out var info) ? info.ToString() : null
                            });
                }
            }

            return Ok(new
            {
                callSid,
                connectionId = connection.Id,
                status = Field("status"),
                direction = Field("direction"),
                from = Field("from_formatted") ?? Field("from"),
                to = Field("to_formatted") ?? Field("to"),
                startTime = Field("start_time"),
                endTime = Field("end_time"),
                durationSeconds = Field("duration"),
                answeredBy = Field("answered_by"),
                price = Field("price"),
                priceUnit = Field("price_unit"),
                notifications = notices
            });
        }
        return NotFound(new { message = "Ninguna cuenta de Twilio del tenant tiene esa llamada." });
    }

    /// <summary>The numbers verified in the Twilio account: the only ones a trial account may call.</summary>
    private async Task<List<string>?> VerifiedCallerIdsAsync(string tenantId, string connectionId, CancellationToken ct)
    {
        try
        {
            var account = await _twilio.GetAsync(tenantId, connectionId, ct);
            if (account is null) return null;
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twilio.com/2010-04-01/Accounts/{account.AccountSid}/OutgoingCallerIds.json?PageSize=50");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{account.AccountSid}:{account.AuthToken}")));
            using var response = await _http.CreateClient().SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("outgoing_caller_ids").EnumerateArray()
                .Select(n => n.GetProperty("phone_number").GetString() ?? string.Empty)
                .Where(n => n.Length > 0)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list the Twilio account's verified caller ids.");
            return null;
        }
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
    /// <summary>conversation (default): the assistant takes the call. notify: the script is read twice and the call ends.</summary>
    public string? Mode { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Script { get; init; }
    /// <summary>"voice" or "callcenter": the default line of that kind.</summary>
    public string? Channel { get; init; }
    /// <summary>The line (channel id) to call from; wins over <see cref="Channel"/>.</summary>
    public string? ChannelId { get; init; }
    public string? ConnectionId { get; init; }
}
