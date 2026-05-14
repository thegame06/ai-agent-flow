namespace AgentFlow.McpServer.Auth;

/// <summary>
/// Valida el API key que los agentes deben enviar como Bearer token.
/// La key se configura en appsettings.json bajo McpServer:ApiKey.
/// Si está vacía, el server solo acepta llamadas desde localhost (dev mode).
/// </summary>
public sealed class McpApiKeyValidator
{
    private readonly string? _configuredKey;

    public McpApiKeyValidator(IConfiguration config)
    {
        _configuredKey = config["McpServer:ApiKey"];
    }

    public bool IsValid(string? bearerToken, HttpContext ctx)
    {
        // Dev mode: no key configured → accept only localhost
        if (string.IsNullOrWhiteSpace(_configuredKey))
            return ctx.Connection.RemoteIpAddress?.ToString() is "127.0.0.1" or "::1";

        return !string.IsNullOrWhiteSpace(bearerToken) &&
               string.Equals(bearerToken, _configuredKey, StringComparison.Ordinal);
    }
}
