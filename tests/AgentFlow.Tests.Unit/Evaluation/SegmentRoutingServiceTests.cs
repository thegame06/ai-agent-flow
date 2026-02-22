using AgentFlow.Evaluation;
using Xunit;

namespace AgentFlow.Tests.Unit.Evaluation;

public sealed class SegmentRoutingServiceTests
{
    private readonly ISegmentRoutingService _service = new InMemorySegmentRoutingService();
    private const string TenantId = "tenant-123";
    private const string AgentId = "agent-main";

    [Fact]
    public async Task SelectAgent_NoConfiguration_ReturnsOriginal()
    {
        var context = new SegmentRoutingContext
        {
            UserId = "user-1",
            UserSegments = new[] { "premium" }
        };

        var decision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, context);

        Assert.Equal(AgentId, decision.SelectedAgentId);
        Assert.False(decision.WasRouted);
        Assert.Contains("No segment routing configured", decision.Reason);
    }

    [Fact]
    public async Task SelectAgent_RoutingDisabled_ReturnsOriginal()
    {
        var config = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = false,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "premium-rule",
                    MatchSegments = new[] { "premium" },
                    TargetAgentId = "agent-premium"
                }
            }
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config);

        var context = new SegmentRoutingContext
        {
            UserId = "user-1",
            UserSegments = new[] { "premium" }
        };

        var decision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, context);

        Assert.Equal(AgentId, decision.SelectedAgentId);
        Assert.False(decision.WasRouted);
        Assert.Contains("disabled", decision.Reason);
    }

    [Fact]
    public async Task SelectAgent_SingleRule_Match_RoutesToTarget()
    {
        var config = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = true,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "premium-rule",
                    MatchSegments = new[] { "premium" },
                    TargetAgentId = "agent-premium",
                    Priority = 10
                }
            }
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config);

        var context = new SegmentRoutingContext
        {
            UserId = "user-1",
            UserSegments = new[] { "premium" }
        };

        var decision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, context);

        Assert.Equal("agent-premium", decision.SelectedAgentId);
        Assert.True(decision.WasRouted);
        Assert.NotNull(decision.MatchedRule);
        Assert.Equal("premium-rule", decision.MatchedRule!.RuleName);
    }

    [Fact]
    public async Task SelectAgent_SingleRule_NoMatch_ReturnsOriginal()
    {
        var config = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = true,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "enterprise-rule",
                    MatchSegments = new[] { "enterprise" },
                    TargetAgentId = "agent-enterprise"
                }
            }
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config);

        var context = new SegmentRoutingContext
        {
            UserId = "user-1",
            UserSegments = new[] { "premium" }
        };

        var decision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, context);

        Assert.Equal(AgentId, decision.SelectedAgentId);
        Assert.False(decision.WasRouted);
        Assert.Contains("No rules matched", decision.Reason);
    }

    [Fact]
    public async Task SelectAgent_MultipleRules_HighestPriorityWins()
    {
        var config = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = true,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "vip-rule",
                    MatchSegments = new[] { "vip" },
                    TargetAgentId = "agent-vip",
                    Priority = 100
                },
                new SegmentRoutingRule
                {
                    RuleName = "premium-rule",
                    MatchSegments = new[] { "premium" },
                    TargetAgentId = "agent-premium",
                    Priority = 50
                }
            }
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config);

        // User has both segments - should match highest priority
        var context = new SegmentRoutingContext
        {
            UserId = "user-1",
            UserSegments = new[] { "premium", "vip" }
        };

        var decision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, context);

        Assert.Equal("agent-vip", decision.SelectedAgentId);
        Assert.Equal("vip-rule", decision.MatchedRule!.RuleName);
    }

    [Fact]
    public async Task SelectAgent_RequireAllSegments_AllPresent_Matches()
    {
        var config = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = true,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "vip-beta-rule",
                    MatchSegments = new[] { "vip", "beta-tester" },
                    TargetAgentId = "agent-experimental",
                    RequireAllSegments = true
                }
            }
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config);

        var context = new SegmentRoutingContext
        {
            UserId = "user-1",
            UserSegments = new[] { "vip", "beta-tester", "premium" }
        };

        var decision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, context);

        Assert.Equal("agent-experimental", decision.SelectedAgentId);
        Assert.True(decision.WasRouted);
    }

    [Fact]
    public async Task SelectAgent_RequireAllSegments_OneMissing_DoesNotMatch()
    {
        var config = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = true,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "vip-beta-rule",
                    MatchSegments = new[] { "vip", "beta-tester" },
                    TargetAgentId = "agent-experimental",
                    RequireAllSegments = true
                }
            }
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config);

        var context = new SegmentRoutingContext
        {
            UserId = "user-1",
            UserSegments = new[] { "vip", "premium" } // Missing beta-tester
        };

        var decision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, context);

        Assert.Equal(AgentId, decision.SelectedAgentId);
        Assert.False(decision.WasRouted);
    }

    [Fact]
    public async Task SelectAgent_ORLogic_AnySegmentMatches()
    {
        var config = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = true,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "high-value-rule",
                    MatchSegments = new[] { "premium", "enterprise", "vip" },
                    TargetAgentId = "agent-advanced",
                    RequireAllSegments = false // OR logic
                }
            }
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config);

        var context = new SegmentRoutingContext
        {
            UserId = "user-1",
            UserSegments = new[] { "premium" } // Only one segment present
        };

        var decision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, context);

        Assert.Equal("agent-advanced", decision.SelectedAgentId);
        Assert.True(decision.WasRouted);
    }

    [Fact]
    public async Task SelectAgent_DefaultTarget_NoRulesMatch_UsesDefault()
    {
        var config = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = true,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "vip-rule",
                    MatchSegments = new[] { "vip" },
                    TargetAgentId = "agent-vip"
                }
            },
            DefaultTargetAgentId = "agent-basic"
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config);

        var context = new SegmentRoutingContext
        {
            UserId = "user-1",
            UserSegments = new[] { "standard" }
        };

        var decision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, context);

        Assert.Equal("agent-basic", decision.SelectedAgentId);
        Assert.True(decision.WasRouted);
        Assert.Contains("default target", decision.Reason);
    }

    [Fact]
    public async Task SelectAgent_ComplexScenario_RealisticTiering()
    {
        // Realistic scenario: Free → Basic, Premium → Advanced, Enterprise → VIP
        var config = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = true,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "enterprise",
                    MatchSegments = new[] { "enterprise" },
                    TargetAgentId = "agent-vip",
                    Priority = 100
                },
                new SegmentRoutingRule
                {
                    RuleName = "premium",
                    MatchSegments = new[] { "premium" },
                    TargetAgentId = "agent-advanced",
                    Priority = 50
                }
            },
            DefaultTargetAgentId = "agent-basic" // Free tier
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config);

        // Test enterprise user
        var enterpriseContext = new SegmentRoutingContext
        {
            UserId = "user-enterprise",
            UserSegments = new[] { "enterprise", "premium" }
        };
        var enterpriseDecision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, enterpriseContext);
        Assert.Equal("agent-vip", enterpriseDecision.SelectedAgentId);

        // Test premium user
        var premiumContext = new SegmentRoutingContext
        {
            UserId = "user-premium",
            UserSegments = new[] { "premium" }
        };
        var premiumDecision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, premiumContext);
        Assert.Equal("agent-advanced", premiumDecision.SelectedAgentId);

        // Test free user
        var freeContext = new SegmentRoutingContext
        {
            UserId = "user-free",
            UserSegments = new[] { "standard" }
        };
        var freeDecision = await _service.SelectAgentForSegmentAsync(TenantId, AgentId, freeContext);
        Assert.Equal("agent-basic", freeDecision.SelectedAgentId);
    }

    [Fact]
    public async Task GetSegmentRouting_Exists_ReturnsConfiguration()
    {
        var config = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = true,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "test-rule",
                    MatchSegments = new[] { "test" },
                    TargetAgentId = "agent-test"
                }
            }
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config);

        var retrieved = await _service.GetSegmentRoutingAsync(TenantId, AgentId);

        Assert.NotNull(retrieved);
        Assert.Equal(AgentId, retrieved!.AgentId);
        Assert.True(retrieved.IsEnabled);
        Assert.Single(retrieved.Rules);
    }

    [Fact]
    public async Task GetSegmentRouting_DoesNotExist_ReturnsNull()
    {
        var retrieved = await _service.GetSegmentRoutingAsync(TenantId, "non-existent-agent");
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task SetSegmentRouting_Update_OverwritesExisting()
    {
        var config1 = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = true,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "rule-v1",
                    MatchSegments = new[] { "test" },
                    TargetAgentId = "agent-v1"
                }
            }
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config1);

        var config2 = new SegmentRoutingConfiguration
        {
            AgentId = AgentId,
            TenantId = TenantId,
            IsEnabled = false,
            Rules = new[]
            {
                new SegmentRoutingRule
                {
                    RuleName = "rule-v2",
                    MatchSegments = new[] { "prod" },
                    TargetAgentId = "agent-v2"
                }
            }
        };

        await _service.SetSegmentRoutingAsync(TenantId, AgentId, config2);

        var retrieved = await _service.GetSegmentRoutingAsync(TenantId, AgentId);

        Assert.NotNull(retrieved);
        Assert.False(retrieved!.IsEnabled);
        Assert.Single(retrieved.Rules);
        Assert.Equal("rule-v2", retrieved.Rules[0].RuleName);
    }
}
