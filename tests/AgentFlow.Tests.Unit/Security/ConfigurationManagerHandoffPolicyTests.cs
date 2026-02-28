using AgentFlow.Security;
using Microsoft.Extensions.Configuration;

namespace AgentFlow.Tests.Unit.Security;

public sealed class ConfigurationManagerHandoffPolicyTests
{
    [Fact]
    public void IsAllowed_ReturnsTrue_WhenNoPolicyConfigured()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var policy = new ConfigurationManagerHandoffPolicy(cfg);

        Assert.True(policy.IsAllowed("tenant-1", "manager-agent", "sales-agent"));
    }

    [Fact]
    public void IsAllowed_ReturnsFalse_WhenTargetNotInAllowlist()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HandoffPolicy:Tenants:tenant-1:Managers:manager-agent:0"] = "collections-bot"
            })
            .Build();

        var policy = new ConfigurationManagerHandoffPolicy(cfg);

        Assert.False(policy.IsAllowed("tenant-1", "manager-agent", "sales-agent"));
        Assert.True(policy.IsAllowed("tenant-1", "manager-agent", "collections-bot"));

        var targets = policy.GetAllowedTargets("tenant-1", "manager-agent");
        Assert.Single(targets);
        Assert.Equal("collections-bot", targets[0]);
    }

    [Fact]
    public void IsAllowed_ReturnsFalse_WhenSourceEqualsTarget()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var policy = new ConfigurationManagerHandoffPolicy(cfg);

        Assert.False(policy.IsAllowed("tenant-1", "manager-agent", "manager-agent"));
    }

    [Fact]
    public void Evaluate_ReturnsDefaultAllow_WhenNoPolicyConfigured()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var policy = new ConfigurationManagerHandoffPolicy(cfg);

        var decision = policy.Evaluate("tenant-1", "manager-agent", "sales-agent");

        Assert.True(decision.Allowed);
        Assert.Equal("no_explicit_policy_allow", decision.Reason);
        Assert.False(decision.HasExplicitPolicy);
    }
}
