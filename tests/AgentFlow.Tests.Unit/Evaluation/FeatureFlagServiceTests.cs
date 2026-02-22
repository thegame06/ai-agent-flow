using AgentFlow.Evaluation;
using Xunit;

namespace AgentFlow.Tests.Unit.Evaluation;

public sealed class FeatureFlagServiceTests
{
    private readonly IFeatureFlagService _service = new InMemoryFeatureFlagService();
    private const string TenantId = "tenant-123";

    [Fact]
    public async Task IsEnabled_FlagDoesNotExist_ReturnsFalse()
    {
        var context = new FeatureFlagContext { AgentId = "agent-1" };
        var result = await _service.IsEnabledAsync(TenantId, "non-existent-flag", context);
        
        Assert.False(result);
    }

    [Fact]
    public async Task IsEnabled_FlagDisabled_ReturnsFalse()
    {
        var flag = new FeatureFlagDefinition
        {
            FlagKey = "test-feature",
            TenantId = TenantId,
            Description = "Test feature",
            IsEnabled = false,
            Targeting = new FeatureFlagTargeting()
        };

        await _service.SetFeatureFlagAsync(TenantId, flag);

        var context = new FeatureFlagContext { AgentId = "agent-1" };
        var result = await _service.IsEnabledAsync(TenantId, "test-feature", context);
        
        Assert.False(result);
    }

    [Fact]
    public async Task IsEnabled_FlagEnabled_NoTargeting_ReturnsTrue()
    {
        var flag = new FeatureFlagDefinition
        {
            FlagKey = "global-feature",
            TenantId = TenantId,
            Description = "Global feature",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting()
        };

        await _service.SetFeatureFlagAsync(TenantId, flag);

        var context = new FeatureFlagContext { AgentId = "agent-1" };
        var result = await _service.IsEnabledAsync(TenantId, "global-feature", context);
        
        Assert.True(result);
    }

    [Fact]
    public async Task IsEnabled_AgentTargeted_CorrectAgent_ReturnsTrue()
    {
        var flag = new FeatureFlagDefinition
        {
            FlagKey = "agent-specific",
            TenantId = TenantId,
            Description = "Feature for specific agents",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting
            {
                AgentIds = new[] { "agent-1", "agent-2" }
            }
        };

        await _service.SetFeatureFlagAsync(TenantId, flag);

        var context = new FeatureFlagContext { AgentId = "agent-1" };
        var result = await _service.IsEnabledAsync(TenantId, "agent-specific", context);
        
        Assert.True(result);
    }

    [Fact]
    public async Task IsEnabled_AgentTargeted_WrongAgent_ReturnsFalse()
    {
        var flag = new FeatureFlagDefinition
        {
            FlagKey = "agent-specific",
            TenantId = TenantId,
            Description = "Feature for specific agents",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting
            {
                AgentIds = new[] { "agent-1", "agent-2" }
            }
        };

        await _service.SetFeatureFlagAsync(TenantId, flag);

        var context = new FeatureFlagContext { AgentId = "agent-3" };
        var result = await _service.IsEnabledAsync(TenantId, "agent-specific", context);
        
        Assert.False(result);
    }

    [Fact]
    public async Task IsEnabled_SegmentTargeted_UserInSegment_ReturnsTrue()
    {
        var flag = new FeatureFlagDefinition
        {
            FlagKey = "premium-feature",
            TenantId = TenantId,
            Description = "Premium users only",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting
            {
                UserSegments = new[] { "premium", "enterprise" }
            }
        };

        await _service.SetFeatureFlagAsync(TenantId, flag);

        var context = new FeatureFlagContext
        {
            UserId = "user-1",
            UserSegments = new[] { "premium", "beta-tester" }
        };

        var result = await _service.IsEnabledAsync(TenantId, "premium-feature", context);
        
        Assert.True(result);
    }

    [Fact]
    public async Task IsEnabled_SegmentTargeted_UserNotInSegment_ReturnsFalse()
    {
        var flag = new FeatureFlagDefinition
        {
            FlagKey = "premium-feature",
            TenantId = TenantId,
            Description = "Premium users only",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting
            {
                UserSegments = new[] { "premium", "enterprise" }
            }
        };

        await _service.SetFeatureFlagAsync(TenantId, flag);

        var context = new FeatureFlagContext
        {
            UserId = "user-1",
            UserSegments = new[] { "standard", "beta-tester" }
        };

        var result = await _service.IsEnabledAsync(TenantId, "premium-feature", context);
        
        Assert.False(result);
    }

    [Fact]
    public async Task IsEnabled_RolloutPercentage_10Percent_Distributes()
    {
        var flag = new FeatureFlagDefinition
        {
            FlagKey = "canary-rollout",
            TenantId = TenantId,
            Description = "Gradual rollout",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting
            {
                RolloutPercentage = 0.10 // 10%
            }
        };

        await _service.SetFeatureFlagAsync(TenantId, flag);

        int enabledCount = 0;
        int totalUsers = 1000;

        for (int i = 0; i < totalUsers; i++)
        {
            var context = new FeatureFlagContext { UserId = $"user-{i}" };
            var result = await _service.IsEnabledAsync(TenantId, "canary-rollout", context);
            
            if (result)
                enabledCount++;
        }

        double actualRatio = (double)enabledCount / totalUsers;
        
        // Should be roughly 10% ± 3%
        Assert.InRange(actualRatio, 0.07, 0.13);
    }

    [Fact]
    public async Task IsEnabled_RolloutPercentage_SameUser_Deterministic()
    {
        var flag = new FeatureFlagDefinition
        {
            FlagKey = "beta-feature",
            TenantId = TenantId,
            Description = "Beta rollout",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting
            {
                RolloutPercentage = 0.50 // 50%
            }
        };

        await _service.SetFeatureFlagAsync(TenantId, flag);

        var context = new FeatureFlagContext { UserId = "user-123" };
        
        var result1 = await _service.IsEnabledAsync(TenantId, "beta-feature", context);
        var result2 = await _service.IsEnabledAsync(TenantId, "beta-feature", context);
        var result3 = await _service.IsEnabledAsync(TenantId, "beta-feature", context);
        
        // Same user = same result (deterministic)
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }

    [Fact]
    public async Task GetEnabledFeatures_MultipleFlags_ReturnsOnlyEnabled()
    {
        var flag1 = new FeatureFlagDefinition
        {
            FlagKey = "feature-1",
            TenantId = TenantId,
            Description = "Feature 1",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting()
        };

        var flag2 = new FeatureFlagDefinition
        {
            FlagKey = "feature-2",
            TenantId = TenantId,
            Description = "Feature 2",
            IsEnabled = false,
            Targeting = new FeatureFlagTargeting()
        };

        var flag3 = new FeatureFlagDefinition
        {
            FlagKey = "feature-3",
            TenantId = TenantId,
            Description = "Feature 3",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting
            {
                UserSegments = new[] { "premium" }
            }
        };

        await _service.SetFeatureFlagAsync(TenantId, flag1);
        await _service.SetFeatureFlagAsync(TenantId, flag2);
        await _service.SetFeatureFlagAsync(TenantId, flag3);

        var context = new FeatureFlagContext
        {
            UserId = "user-1",
            UserSegments = new[] { "premium" }
        };

        var enabledFeatures = await _service.GetEnabledFeaturesAsync(TenantId, context);
        
        Assert.Equal(2, enabledFeatures.Count);
        Assert.Contains("feature-1", enabledFeatures);
        Assert.Contains("feature-3", enabledFeatures);
        Assert.DoesNotContain("feature-2", enabledFeatures);
    }

    [Fact]
    public async Task SetFeatureFlag_Update_OverwritesExisting()
    {
        var flag1 = new FeatureFlagDefinition
        {
            FlagKey = "test-flag",
            TenantId = TenantId,
            Description = "Original",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting()
        };

        await _service.SetFeatureFlagAsync(TenantId, flag1);

        var context = new FeatureFlagContext();
        var result1 = await _service.IsEnabledAsync(TenantId, "test-flag", context);
        Assert.True(result1);

        // Update: disable
        var flag2 = new FeatureFlagDefinition
        {
            FlagKey = "test-flag",
            TenantId = TenantId,
            Description = "Updated",
            IsEnabled = false,
            Targeting = new FeatureFlagTargeting()
        };

        await _service.SetFeatureFlagAsync(TenantId, flag2);

        var result2 = await _service.IsEnabledAsync(TenantId, "test-flag", context);
        Assert.False(result2);
    }
}
