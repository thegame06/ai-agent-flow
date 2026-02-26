using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ITenantContextAccessor _tenantContext;

    public AuthController(IConfiguration configuration, ITenantContextAccessor tenantContext)
    {
        _configuration = configuration;
        _tenantContext = tenantContext;
    }

    [HttpPost("sign-in")]
    [AllowAnonymous]
    public IActionResult SignIn([FromBody] SignInRequest request)
    {
        var adminEmail = _configuration["Auth:Admin:Email"] ?? "admin@agentflow.local";
        var adminPassword = _configuration["Auth:Admin:Password"] ?? "ChangeMeNow!123";
        var adminTenantId = _configuration["Auth:Admin:TenantId"] ?? "tenant-1";

        if (!string.Equals(request.Email?.Trim(), adminEmail, StringComparison.OrdinalIgnoreCase) || request.Password != adminPassword)
        {
            return Unauthorized(new { message = "Invalid credentials" });
        }

        var token = GenerateJwtToken(request.Email!, adminTenantId);

        return Ok(new
        {
            accessToken = token,
            user = new
            {
                id = "admin-user-1",
                email = adminEmail,
                displayName = "Platform Admin",
                role = "platform_admin",
                photoURL = "https://api-dev-minimal-v6.vercel.app/assets/images/avatar/avatar-1.webp",
                tenantId = adminTenantId
            }
        });
    }

    [HttpPost("sign-up")]
    [AllowAnonymous]
    public IActionResult SignUp([FromBody] SignUpRequest request)
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = "User sign-up is disabled until IAM module is implemented." });

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var context = _tenantContext.Current;
        if (context == null) return Unauthorized();

        return Ok(new
        {
            user = new
            {
                id = context.UserId,
                email = context.UserEmail,
                displayName = "Platform Admin",
                role = context.IsPlatformAdmin ? "platform_admin" : "tenant_user",
                tenantId = context.TenantId,
                permissions = context.Permissions
            }
        });
    }

    private string GenerateJwtToken(string email, string tenantId)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is missing.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "admin-user-1"),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, "platform_admin"),
            new(ClaimTypes.Role, "admin"),
            new("tenant_id", tenantId),
            new("tenant_slug", tenantId),
            // super permissions for local admin until IAM module is implemented
            new("permission", AgentFlowPermissions.PlatformAdmin),
            new("permission", AgentFlowPermissions.AgentRead),
            new("permission", AgentFlowPermissions.AgentCreate),
            new("permission", AgentFlowPermissions.AgentUpdate),
            new("permission", AgentFlowPermissions.AgentPublish),
            new("permission", AgentFlowPermissions.ExecutionTrigger),
            new("permission", AgentFlowPermissions.ExecutionRead),
            new("permission", AgentFlowPermissions.AuditRead),
            new("permission", AgentFlowPermissions.ToolRead),
            new("permission", AgentFlowPermissions.TenantManage)
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.Now.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record SignInRequest(string Email, string Password);
public record SignUpRequest(string Email, string Password, string FirstName, string LastName);
