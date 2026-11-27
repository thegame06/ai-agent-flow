using AgentFlow.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgentFlow.Api.Entitlements;

/// <summary>
/// Blocks a controller or action unless the app module is enabled for the tenant in the
/// route (<c>{tenantId}</c>). Responds 403 with <c>errorCode = APP_MODULE_DISABLED</c>.
/// Runs as an authorization filter, before model binding and the action.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresAppModuleAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string ErrorCode = "APP_MODULE_DISABLED";

    public RequiresAppModuleAttribute(string moduleId) => ModuleId = moduleId;

    public string ModuleId { get; }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Leave unauthenticated requests to the authorization pipeline (401, not 403).
        if (context.Result is not null) return;

        if (!context.RouteData.Values.TryGetValue("tenantId", out var raw) ||
            raw is not string tenantId || string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        var entitlements = context.HttpContext.RequestServices.GetRequiredService<IAppEntitlementService>();
        if (await entitlements.IsEnabledAsync(tenantId, ModuleId, context.HttpContext.RequestAborted))
        {
            return;
        }

        context.Result = new ObjectResult(new
        {
            errorCode = ErrorCode,
            moduleId = ModuleId,
            message = $"App module '{ModuleId}' is not enabled for tenant '{tenantId}'."
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
