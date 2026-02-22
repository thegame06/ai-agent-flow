using System.Security.Claims;
using AgentFlow.Security;

namespace AgentFlow.Api.Middleware;

/// <summary>
/// Extracts TenantContext from JWT claims and makes it available via ITenantContextAccessor.
/// Unicorn Strategy: Security boundary - never trust client-provided tenant info.
/// </summary>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor tenantContextAccessor)
    {
        // Skip for anonymous endpoints
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        try
        {
            // Extract from JWT claims
            var tenantContext = TenantContext.FromClaims(context.User);
            tenantContextAccessor.Set(tenantContext);
            
            // Also make userId available in HttpContext.Items for backward compatibility
            context.Items["UserId"] = tenantContext.UserId;
            context.Items["TenantId"] = tenantContext.TenantId;
        }
        catch (SecurityException ex)
        {
            // Missing required claims (tenant_id or user identity)
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new 
            { 
                error = "Unauthorized", 
                message = ex.Message 
            });
            return;
        }

        await _next(context);
    }
}

public static class TenantContextMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantContextMiddleware>();
    }
}
