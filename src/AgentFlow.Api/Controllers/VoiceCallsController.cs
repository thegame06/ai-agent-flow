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
    private readonly string? _publicBaseUrl;

    public VoiceCallsController(
        IVoiceConversationService conversation,
        ITenantConnectionStore connections,
        ITenantContextAccessor tenantContext,
        IConfiguration configuration)
    {
        _conversation = conversation;
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

        return Ok(new
        {
            ready = missing.Count == 0,
            missing,
            channelId = channel?.Id,
            channelName = channel?.Name,
            connectionId = twilio?.Id,
            voicePhoneNumber = voiceNumber,
            incomingWebhookUrl = baseUrl is null ? null : $"{baseUrl}/api/v1/tenants/{tenantId}/webhooks/twilio/voice/incoming",
            statusCallbackUrl = baseUrl is null ? null : $"{baseUrl}/api/v1/tenants/{tenantId}/webhooks/twilio/voice/status"
        });
    }

    private bool CanAccess(string tenantId)
    {
        var context = _tenantContext.Current;
        return context is not null && (context.TenantId == tenantId || context.IsPlatformAdmin);
    }
}

public sealed record PlaceVoiceCallBody
{
    public string? PhoneNumber { get; init; }
    public string? Script { get; init; }
    public string? Channel { get; init; }
    public string? ConnectionId { get; init; }
}
