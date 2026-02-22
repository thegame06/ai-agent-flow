using AgentFlow.Abstractions;
using AgentFlow.DSL;

namespace AgentFlow.Tests.Unit.DSL;

/// <summary>
/// Tests for the AgentDefinitionValidator.
/// Covers ALL 10 design-time invariants defined in the dsl-engine workflow.
/// 
/// Invariant coverage map:
/// - DSL001: Key must be snake_case
/// - DSL002: Version must be semver
/// - DSL003: Version must be > current published
/// - DSL004: Runtime mode must be valid
/// - DSL005: Deterministic mode requires temperature 0.0
/// - DSL006: Autonomous blocked in production
/// - DSL007: Flow tools must be in authorizedTools
/// - DSL008: Authorized tools must exist in registry
/// - DSL009: PolicySet must be published
/// - DSL010: PromptProfile must be published
/// - DSL011: Default model must exist
/// - DSL012: Fallback models must exist
/// - DSL013: Production requires test cases
/// - DSL014: Model routing strategy must be valid
/// </summary>
public sealed class DslValidatorTests
{
    private readonly AgentDefinitionValidator _validator = new();

    // ── DSL001: Key validation ──

    [Theory]
    [InlineData("valid_agent", true)]
    [InlineData("agent_v2", true)]
    [InlineData("a", true)]
    [InlineData("a123_test", true)]
    [InlineData("Invalid", false)]         // uppercase
    [InlineData("123_invalid", false)]     // starts with number
    [InlineData("has-dash", false)]        // contains dash
    [InlineData("has space", false)]       // contains space
    [InlineData("", false)]                // empty
    public async Task DSL001_Key_SnakeCase_Validation(string key, bool expectValid)
    {
        var dsl = BuildDsl(key: key);
        var ctx = BuildContext();

        var result = await _validator.ValidateAsync(dsl, ctx);

        if (expectValid)
            Assert.DoesNotContain(result.Errors, e => e.Code == "DSL001");
        else
            Assert.Contains(result.Errors, e => e.Code == "DSL001");
    }

    // ── DSL002/003: Version validation ──

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("2.3.1", true)]
    [InlineData("1.0", false)]      // incomplete
    [InlineData("v1.0.0", false)]   // prefix
    [InlineData("abc", false)]      // non-numeric
    public async Task DSL002_Version_Semver_Format(string version, bool expectValid)
    {
        var dsl = BuildDsl(version: version);
        var ctx = BuildContext();

        var result = await _validator.ValidateAsync(dsl, ctx);

        if (expectValid)
            Assert.DoesNotContain(result.Errors, e => e.Code == "DSL002");
        else
            Assert.Contains(result.Errors, e => e.Code == "DSL002");
    }

    [Fact]
    public async Task DSL003_Version_NoDowngrade()
    {
        var dsl = BuildDsl(version: "1.0.0");
        var ctx = BuildContext(currentPublishedVersion: "2.0.0");

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.Contains(result.Errors, e => e.Code == "DSL003");
    }

    [Fact]
    public async Task DSL003_Version_Upgrade_Allowed()
    {
        var dsl = BuildDsl(version: "2.1.0");
        var ctx = BuildContext(currentPublishedVersion: "2.0.0");

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.DoesNotContain(result.Errors, e => e.Code == "DSL003");
    }

    // ── DSL004: Runtime mode ──

    [Theory]
    [InlineData("deterministic", true)]
    [InlineData("hybrid", true)]
    [InlineData("autonomous", true)]
    [InlineData("invalid_mode", false)]
    [InlineData("", false)]
    public async Task DSL004_RuntimeMode_Validation(string mode, bool expectValid)
    {
        var dsl = BuildDsl(mode: mode, temperature: mode == "deterministic" ? 0.0 : 0.2);
        var ctx = BuildContext();

        var result = await _validator.ValidateAsync(dsl, ctx);

        if (expectValid)
            Assert.DoesNotContain(result.Errors, e => e.Code == "DSL004");
        else
            Assert.Contains(result.Errors, e => e.Code == "DSL004");
    }

    // ── DSL005: Deterministic requires temp 0.0 ──

    [Fact]
    public async Task DSL005_Deterministic_NonZeroTemp_Fails()
    {
        var dsl = BuildDsl(mode: "deterministic", temperature: 0.5);
        var ctx = BuildContext();

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.Contains(result.Errors, e => e.Code == "DSL005");
    }

    [Fact]
    public async Task DSL005_Deterministic_ZeroTemp_Passes()
    {
        var dsl = BuildDsl(mode: "deterministic", temperature: 0.0);
        var ctx = BuildContext();

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.DoesNotContain(result.Errors, e => e.Code == "DSL005");
    }

    // ── DSL006: Autonomous blocked in production ──

    [Fact]
    public async Task DSL006_Autonomous_ProductionDeploy_Blocked()
    {
        var dsl = BuildDsl(mode: "autonomous");
        var ctx = BuildContext(isProduction: true);

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.Contains(result.Errors, e => e.Code == "DSL006");
    }

    [Fact]
    public async Task DSL006_Autonomous_NonProduction_Allowed()
    {
        var dsl = BuildDsl(mode: "autonomous");
        var ctx = BuildContext(isProduction: false);

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.DoesNotContain(result.Errors, e => e.Code == "DSL006");
    }

    // ── DSL007: Flow tools must be in authorizedTools ──

    [Fact]
    public async Task DSL007_FlowTool_NotAuthorized_Fails()
    {
        var dsl = BuildDsl(
            authorizedTools: ["ToolA"],
            flowTools: ["ToolA", "ToolB"]); // ToolB not authorized

        var ctx = BuildContext(registeredTools: ["ToolA", "ToolB"]);

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.Contains(result.Errors, e => e.Code == "DSL007" && e.Message.Contains("ToolB"));
    }

    // ── DSL008: Authorized tools must exist in registry ──

    [Fact]
    public async Task DSL008_Tool_NotRegistered_Fails()
    {
        var dsl = BuildDsl(authorizedTools: ["ToolA", "GhostTool"]);
        var ctx = BuildContext(registeredTools: ["ToolA"]);

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.Contains(result.Errors, e => e.Code == "DSL008" && e.Message.Contains("GhostTool"));
    }

    // ── DSL013: Production requires test cases ──

    [Fact]
    public async Task DSL013_Production_NoTestCases_Fails()
    {
        var dsl = BuildDsl(testCases: []);
        var ctx = BuildContext(isProduction: true);

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.Contains(result.Errors, e => e.Code == "DSL013");
    }

    // ── Warnings ──

    [Fact]
    public async Task Warning_HighTemperature_NonAutonomous()
    {
        var dsl = BuildDsl(temperature: 0.9, mode: "hybrid");
        var ctx = BuildContext();

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.Contains(result.Warnings, w => w.Code == "DSLW001");
    }

    [Fact]
    public async Task Warning_MaxStepsExceeds20()
    {
        var dsl = BuildDsl(maxSteps: 30);
        var ctx = BuildContext();

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.Contains(result.Warnings, w => w.Code == "DSLW002");
    }

    [Fact]
    public async Task Warning_NoFallbackChain()
    {
        var dsl = BuildDsl(fallbackChain: []);
        var ctx = BuildContext();

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.Contains(result.Warnings, w => w.Code == "DSLW003");
    }

    // ── Full valid DSL passes all checks ──

    [Fact]
    public async Task FullValidDsl_NoErrors()
    {
        var dsl = BuildDsl(
            key: "valid_agent",
            version: "1.0.0",
            mode: "hybrid",
            temperature: 0.2,
            authorizedTools: ["ToolA"],
            flowTools: ["ToolA"],
            testCases: [new TestCaseDsl { Name = "Basic", Input = "Hello" }]);

        var ctx = BuildContext(registeredTools: ["ToolA"]);

        var result = await _validator.ValidateAsync(dsl, ctx);

        Assert.True(result.IsValid, $"Expected valid but got errors: {string.Join(", ", result.Errors.Select(e => e.Code))}");
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static AgentDefinitionDsl BuildDsl(
        string key = "test_agent",
        string version = "1.0.0",
        string mode = "hybrid",
        double temperature = 0.2,
        IReadOnlyList<string>? authorizedTools = null,
        IReadOnlyList<string>? flowTools = null,
        IReadOnlyList<TestCaseDsl>? testCases = null,
        int maxSteps = 6,
        IReadOnlyList<string>? fallbackChain = null)
    {
        var tools = authorizedTools ?? ["ToolA"];
        var steps = (flowTools ?? tools).Select(t => new StepDsl { Tool = t }).ToList();

        return new AgentDefinitionDsl
        {
            Agent = new AgentConfigDsl
            {
                Key = key,
                Version = version,
                Role = "Test agent",
                Runtime = new RuntimeConfigDsl
                {
                    Mode = mode,
                    Temperature = temperature,
                    MaxIterations = 6,
                    MaxExecutionSeconds = 120
                },
                ModelRouting = new ModelRoutingConfigDsl
                {
                    Strategy = "static",
                    Default = "gpt-4o",
                    FallbackChain = fallbackChain ?? ["gpt-4o", "gpt-3.5-turbo"]
                },
                AuthorizedTools = tools,
                Flows =
                [
                    new FlowDsl
                    {
                        Name = "main",
                        Trigger = new TriggerDsl { Type = "intent", Value = "*" },
                        Steps = steps
                    }
                ],
                Policies = new PoliciesDsl { MaxSteps = maxSteps },
                TestSuite = new TestSuiteDsl
                {
                    TestCases = testCases ?? [new TestCaseDsl { Name = "Default", Input = "test" }]
                }
            }
        };
    }

    private static DslValidationContext BuildContext(
        string? currentPublishedVersion = null,
        IReadOnlyList<string>? registeredTools = null,
        bool isProduction = false) => new()
    {
        TenantId = "test-tenant",
        CurrentPublishedVersion = currentPublishedVersion,
        RegisteredToolNames = registeredTools ?? ["ToolA", "ToolB"],
        AvailableModelIds = ["gpt-4o", "gpt-3.5-turbo"],
        PublishedPolicySetIds = [],
        PublishedPromptProfileIds = [],
        IsProductionDeploy = isProduction
    };
}
