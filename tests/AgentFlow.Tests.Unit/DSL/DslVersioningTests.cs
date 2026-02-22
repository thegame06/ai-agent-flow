using AgentFlow.DSL;

namespace AgentFlow.Tests.Unit.DSL;

/// <summary>
/// Tests for DslVersioningService — semver comparison, upgrade paths,
/// and change detection between DSL versions.
/// </summary>
public sealed class DslVersioningTests
{
    // ── Version comparison ──

    [Theory]
    [InlineData("1.0.0", null, true, true)]      // First version
    [InlineData("2.0.0", "1.0.0", true, false)]  // Major upgrade
    [InlineData("1.1.0", "1.0.0", true, false)]  // Minor upgrade
    [InlineData("1.0.1", "1.0.0", true, false)]  // Patch upgrade
    [InlineData("1.0.0", "2.0.0", false, false)] // Downgrade
    [InlineData("1.0.0", "1.0.0", false, false)] // Same version
    [InlineData("1.0.0", "1.1.0", false, false)] // Minor downgrade
    public void Compare_VersionValidation(string candidate, string? current, bool expectValid, bool expectFirst)
    {
        var result = DslVersioningService.Compare(candidate, current);

        Assert.Equal(expectValid, result.IsValid);
        Assert.Equal(expectFirst, result.IsFirstVersion);
    }

    [Theory]
    [InlineData("2.0.0", "1.0.0", VersionUpgradeType.Major)]
    [InlineData("1.1.0", "1.0.0", VersionUpgradeType.Minor)]
    [InlineData("1.0.1", "1.0.0", VersionUpgradeType.Patch)]
    public void Compare_UpgradeType_Correct(string candidate, string current, VersionUpgradeType expected)
    {
        var result = DslVersioningService.Compare(candidate, current);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.UpgradeType);
    }

    [Fact]
    public void Compare_InvalidSemver_ReturnsError()
    {
        var result = DslVersioningService.Compare("not.a.version", "1.0.0");
        Assert.False(result.IsValid);
        Assert.Contains("not valid semver", result.ErrorMessage!);
    }

    // ── Change detection ──

    [Fact]
    public void DetectChanges_ModeChange_IsBreaking()
    {
        var candidate = BuildDsl(mode: "autonomous");
        var current = BuildDsl(mode: "hybrid");

        var detection = DslVersioningService.DetectChanges(candidate, current);

        Assert.True(detection.HasBreakingChanges);
        Assert.Equal(VersionUpgradeType.Major, detection.RequiredMinimumUpgrade);
    }

    [Fact]
    public void DetectChanges_ToolsChange_IsBreaking()
    {
        var candidate = BuildDsl(tools: ["ToolA", "ToolB", "ToolC"]);
        var current = BuildDsl(tools: ["ToolA", "ToolB"]);

        var detection = DslVersioningService.DetectChanges(candidate, current);

        Assert.True(detection.HasBreakingChanges);
    }

    [Fact]
    public void DetectChanges_TemperatureChange_IsSafe()
    {
        var candidate = BuildDsl(temperature: 0.3);
        var current = BuildDsl(temperature: 0.2);

        var detection = DslVersioningService.DetectChanges(candidate, current);

        Assert.False(detection.HasBreakingChanges);
        Assert.Equal(VersionUpgradeType.Patch, detection.RequiredMinimumUpgrade);
    }

    [Fact]
    public void DetectChanges_NoChanges_PatchSufficient()
    {
        var dsl = BuildDsl();

        var detection = DslVersioningService.DetectChanges(dsl, dsl);

        Assert.False(detection.HasBreakingChanges);
        Assert.Empty(detection.Changes);
    }

    // ── Lifecycle state machine ──

    [Theory]
    [InlineData(AgentDslStatus.Draft, AgentDslStatus.Validating, true)]
    [InlineData(AgentDslStatus.Draft, AgentDslStatus.Archived, true)]
    [InlineData(AgentDslStatus.Draft, AgentDslStatus.Published, false)]
    [InlineData(AgentDslStatus.Validating, AgentDslStatus.TestPassed, true)]
    [InlineData(AgentDslStatus.Validating, AgentDslStatus.Draft, true)]
    [InlineData(AgentDslStatus.TestPassed, AgentDslStatus.Published, true)]
    [InlineData(AgentDslStatus.Published, AgentDslStatus.Deprecated, true)]
    [InlineData(AgentDslStatus.Published, AgentDslStatus.Draft, false)]        // CRITICAL: Published is immutable
    [InlineData(AgentDslStatus.Published, AgentDslStatus.Archived, false)]     // Must deprecate first
    [InlineData(AgentDslStatus.Deprecated, AgentDslStatus.Archived, true)]
    [InlineData(AgentDslStatus.Archived, AgentDslStatus.Draft, false)]         // Terminal state
    public void Lifecycle_TransitionValidation(AgentDslStatus from, AgentDslStatus to, bool expectValid)
    {
        var result = AgentDslLifecycle.CanTransitionTo(from, to);
        Assert.Equal(expectValid, result.IsSuccess);
    }

    [Fact]
    public void Lifecycle_OnlyPublished_IsExecutable()
    {
        Assert.True(AgentDslLifecycle.IsExecutable(AgentDslStatus.Published));
        Assert.False(AgentDslLifecycle.IsExecutable(AgentDslStatus.Draft));
        Assert.False(AgentDslLifecycle.IsExecutable(AgentDslStatus.Deprecated));
    }

    [Fact]
    public void Lifecycle_PublishedDeprecatedArchived_AreImmutable()
    {
        Assert.True(AgentDslLifecycle.IsImmutable(AgentDslStatus.Published));
        Assert.True(AgentDslLifecycle.IsImmutable(AgentDslStatus.Deprecated));
        Assert.True(AgentDslLifecycle.IsImmutable(AgentDslStatus.Archived));
        Assert.False(AgentDslLifecycle.IsImmutable(AgentDslStatus.Draft));
    }

    // =========================================================================
    // HELPER
    // =========================================================================

    private static AgentDefinitionDsl BuildDsl(
        string mode = "hybrid",
        double temperature = 0.2,
        IReadOnlyList<string>? tools = null)
    {
        return new AgentDefinitionDsl
        {
            Agent = new AgentConfigDsl
            {
                Key = "test_agent",
                Version = "1.0.0",
                Role = "Test",
                Runtime = new RuntimeConfigDsl { Mode = mode, Temperature = temperature },
                ModelRouting = new ModelRoutingConfigDsl { Strategy = "static", Default = "gpt-4o" },
                AuthorizedTools = tools ?? ["ToolA"],
                Flows = [new FlowDsl
                {
                    Name = "main",
                    Trigger = new TriggerDsl(),
                    Steps = (tools ?? ["ToolA"]).Select(t => new StepDsl { Tool = t }).ToList()
                }]
            }
        };
    }
}
