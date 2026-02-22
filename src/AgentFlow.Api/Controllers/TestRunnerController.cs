using AgentFlow.Abstractions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/test-runner")]
[Authorize]
public sealed class TestRunnerController : ControllerBase
{
    private readonly IAgentTestRunner _testRunner;
    private readonly ITenantContextAccessor _tenantContext;

    public TestRunnerController(IAgentTestRunner testRunner, ITenantContextAccessor tenantContext)
    {
        _testRunner = testRunner;
        _tenantContext = tenantContext;
    }

    [HttpPost("suite")]
    public async Task<IActionResult> RunSuite(string tenantId, [FromBody] TestSuiteRequest request)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        // Enforce tenant isolation in the request
        if (request.TenantId != tenantId)
            return BadRequest("TenantId mismatch between URL and body.");

        var result = await _testRunner.RunSuiteAsync(request);
        return Ok(result);
    }

    [HttpPost("case")]
    public async Task<IActionResult> RunCase(string tenantId, [FromBody] TestCaseRequest request)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        // Enforce tenant isolation in the request
        if (request.TenantId != tenantId)
            return BadRequest("TenantId mismatch between URL and body.");

        var result = await _testRunner.RunCaseAsync(request);
        return Ok(result);
    }
}
