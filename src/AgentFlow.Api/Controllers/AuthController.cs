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
        // BYPASS: For Phase 2, we accept any login to unlock the UI.
        // In real production, we would validate against a DB here.
        
        var token = GenerateJwtToken(request.Email);

        return Ok(new
        {
            accessToken = token,
            user = new
            {
                id = "user-1",
                email = request.Email,
                displayName = "Admin User",
                role = "admin",
                photoURL = "https://api-dev-minimal-v6.vercel.app/assets/images/avatar/avatar-1.webp",
                tenantId = "tenant-1"
            }
        });
    }

    [HttpPost("sign-up")]
    [AllowAnonymous]
    public IActionResult SignUp([FromBody] SignUpRequest request)
    {
        // BYPASS: Simple auto-login for signups during dev
        var token = GenerateJwtToken(request.Email);

        return Ok(new
        {
            accessToken = token,
            user = new
            {
                id = Guid.NewGuid().ToString(),
                email = request.Email,
                displayName = $"{request.FirstName} {request.LastName}",
                role = "admin",
                tenantId = "tenant-1"
            }
        });
    }

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
                displayName = "Admin User",
                role = "admin",
                tenantId = context.TenantId,
                permissions = context.Permissions
            }
        });
    }

    private string GenerateJwtToken(string email)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is missing.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user-1"),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, "admin"),
            new("tenant_id", "tenant-1"),
            new("tenant_slug", "default-tenant"),
            // Core permissions to unlock everything for dev
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
