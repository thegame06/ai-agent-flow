using System.Security.Cryptography;
using System.Text;
using AgentFlow.Api.Connect;
using AgentFlow.Infrastructure.Channels.WhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace AgentFlow.Api.Security;

/// <summary>Checks Meta's X-Hub-Signature-256: HMAC-SHA256 of the raw body with the app secret.</summary>
public static class MetaWebhookSignature
{
    public const string Header = "X-Hub-Signature-256";

    public static bool IsValid(byte[] body, string? header, IEnumerable<string> appSecrets)
    {
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;
        byte[] received;
        try
        {
            received = Convert.FromHexString(header["sha256=".Length..].Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        foreach (var secret in appSecrets.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
        {
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
            if (CryptographicOperations.FixedTimeEquals(expected, received)) return true;
        }
        return false;
    }

    public static string Sign(byte[] body, string appSecret)
        => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body)).ToLowerInvariant();
}

/// <summary>
/// Rejects Meta webhook calls that are not signed with the app secret (platform WhatsApp:ApiSecret
/// or the tenant's Meta connection "appSecret"). Without any secret configured the call is only
/// accepted in Development: in any other environment an unsigned webhook would let anyone inject
/// customer messages.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireMetaSignatureAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AgentFlow.Webhooks.Meta");
        var environment = services.GetRequiredService<IHostEnvironment>();

        var secrets = new List<string>();
        var platformSecret = services.GetService<IOptions<WhatsAppOptions>>()?.Value.ApiSecret;
        if (!string.IsNullOrWhiteSpace(platformSecret)) secrets.Add(platformSecret);
        if (context.RouteData.Values.TryGetValue("tenantId", out var tenant) && tenant is string tenantId && services.GetService<IMetaWhatsAppAccounts>() is { } accounts)
        {
            try
            {
                secrets.AddRange((await accounts.GetAllAsync(tenantId, http.RequestAborted)).Select(a => a.AppSecret).OfType<string>());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read the tenant's Meta app secrets");
            }
        }

        if (secrets.Count == 0)
        {
            if (environment.IsDevelopment())
            {
                logger.LogWarning("Meta webhook accepted WITHOUT signature check: no app secret configured (Development only).");
                await next();
                return;
            }
            logger.LogError("Meta webhook rejected: no app secret configured to verify {Header}.", MetaWebhookSignature.Header);
            context.Result = new UnauthorizedObjectResult(new { error = "webhook_signature_not_configured" });
            return;
        }

        http.Request.EnableBuffering();
        using var buffer = new MemoryStream();
        await http.Request.Body.CopyToAsync(buffer, http.RequestAborted);
        http.Request.Body.Position = 0;

        if (!MetaWebhookSignature.IsValid(buffer.ToArray(), http.Request.Headers[MetaWebhookSignature.Header], secrets))
        {
            logger.LogWarning("Meta webhook rejected: invalid {Header}.", MetaWebhookSignature.Header);
            context.Result = new UnauthorizedObjectResult(new { error = "invalid_signature" });
            return;
        }

        await next();
    }
}

/// <summary>
/// The QR bridge must send its shared key (X-Bridge-Key, or Authorization: Bearer). Without a key
/// configured the call is only accepted in Development.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireQrBridgeKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string Header = "X-Bridge-Key";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var expected = services.GetService<IOptions<WhatsAppOptions>>()?.Value.QrBridgeApiKey;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AgentFlow.Webhooks.QrBridge");

        if (string.IsNullOrWhiteSpace(expected))
        {
            if (!services.GetRequiredService<IHostEnvironment>().IsDevelopment())
            {
                logger.LogError("QR bridge call rejected: WhatsApp:QrBridgeApiKey is not configured.");
                context.Result = new UnauthorizedObjectResult(new { error = "bridge_key_not_configured" });
            }
            return Task.CompletedTask;
        }

        var headers = context.HttpContext.Request.Headers;
        var given = headers[Header].ToString();
        if (string.IsNullOrWhiteSpace(given))
        {
            var auth = headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) given = auth["Bearer ".Length..].Trim();
        }

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
        {
            logger.LogWarning("QR bridge call rejected: missing or wrong {Header}.", Header);
            context.Result = new UnauthorizedObjectResult(new { error = "invalid_bridge_key" });
        }
        return Task.CompletedTask;
    }
}
