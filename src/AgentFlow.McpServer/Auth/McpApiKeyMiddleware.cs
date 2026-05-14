namespace AgentFlow.McpServer.Auth;

/// <summary>
/// Middleware que extrae el Bearer token y valida con McpApiKeyValidator.
/// /health siempre es público para probes de infraestructura.
/// </summary>
public sealed class McpApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    public McpApiKeyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, McpApiKeyValidator validator)
    {
        if (ctx.Request.Path.StartsWithSegments("/health"))
        {
            await _next(ctx);
            return;
        }

        var bearer = ctx.Request.Headers.Authorization.FirstOrDefault()
                         ?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

        if (!validator.IsValid(bearer, ctx))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "Unauthorized. Provide a valid Bearer token." });
            return;
        }

        await _next(ctx);
    }
}
