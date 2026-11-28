using AgentFlow.Api.Channels;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

/// <summary>Files customers sent (receipts, documents), served only to their own tenant.</summary>
[ApiController]
[Authorize]
[Route("api/v1/tenants/{tenantId}/media")]
public sealed class MediaController : ControllerBase
{
    private readonly IMediaStore _media;
    private readonly ITenantContextAccessor _tenantContext;

    public MediaController(IMediaStore media, ITenantContextAccessor tenantContext)
    {
        _media = media;
        _tenantContext = tenantContext;
    }

    [HttpGet("{mediaId}")]
    public async Task<IActionResult> Get([FromRoute] string tenantId, [FromRoute] string mediaId, CancellationToken ct)
    {
        var context = _tenantContext.Current;
        if (context is null || (context.TenantId != tenantId && !context.IsPlatformAdmin) ||
            !(context.HasPermission(AgentFlowPermissions.ConnectRead) || context.IsPlatformAdmin))
            return Forbid();

        var file = await _media.GetAsync(tenantId, mediaId, ct);
        if (file is null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Images and PDFs open in the browser; anything else downloads.
        var inline = file.Value.Info.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(file.Value.Info.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
        return inline
            ? File(file.Value.Content, file.Value.Info.ContentType)
            : File(file.Value.Content, file.Value.Info.ContentType, file.Value.Info.FileName ?? "archivo");
    }
}
