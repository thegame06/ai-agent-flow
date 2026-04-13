using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;
using AgentFlow.Evaluation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Engine;

public sealed class AgentBrainResolverTests
{
    private const string TenantId = "tenant-1";
    private readonly Mock<IAgentBrain> _skBrain = new();
    private readonly Mock<IAgentBrain> _mafBrain = new();

    [Fact]
    public async Task ResolveAsync_TenantOnlyFlagEnabled_UsesMaf()
    {
        var flags = new InMemoryFeatureFlagService();
        await flags.SetFeatureFlagAsync(TenantId, new FeatureFlagDefinition
        {
            FlagKey = AgentBrainResolver.ProviderFlagKey,
            TenantId = TenantId,
            Description = "Tenant rollout to MAF",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting()
        });

        var resolver = BuildResolver(flags, BrainProvider.SemanticKernel);

        var result = await resolver.ResolveAsync(
            TenantId,
            "agent-a",
            new AgentBrainExecutionContext { UserId = "user-1" });

        Assert.Equal(BrainProvider.MicrosoftAgentFramework, result.Provider);
        Assert.Equal("feature_flag", result.ResolutionSource);
        Assert.Same(_mafBrain.Object, result.Brain);
    }

    [Fact]
    public async Task ResolveAsync_AgentOnlyFlagEnabled_UsesMaf()
    {
        var flags = new InMemoryFeatureFlagService();
        await flags.SetFeatureFlagAsync(TenantId, new FeatureFlagDefinition
        {
            FlagKey = AgentBrainResolver.ProviderFlagKey,
            TenantId = TenantId,
            Description = "Agent scoped rollout to MAF",
            IsEnabled = true,
            Targeting = new FeatureFlagTargeting
            {
                AgentIds = ["agent-target"]
            }
        });

        var resolver = BuildResolver(flags, BrainProvider.SemanticKernel);

        var result = await resolver.ResolveAsync(
            TenantId,
            "agent-target",
            new AgentBrainExecutionContext { UserId = "user-1" });

        Assert.Equal(BrainProvider.MicrosoftAgentFramework, result.Provider);
        Assert.Equal("feature_flag", result.ResolutionSource);
        Assert.Same(_mafBrain.Object, result.Brain);
    }

    [Fact]
    public async Task ResolveAsync_NoOverrides_FallsBackToDefaultProvider()
    {
        var resolver = BuildResolver(new InMemoryFeatureFlagService(), BrainProvider.SemanticKernel);

        var result = await resolver.ResolveAsync(
            TenantId,
            "agent-a",
            new AgentBrainExecutionContext { UserId = "user-1" });

        Assert.Equal(BrainProvider.SemanticKernel, result.Provider);
        Assert.Equal("default", result.ResolutionSource);
        Assert.Same(_skBrain.Object, result.Brain);
    }

    [Fact]
    public async Task ResolveAsync_InvalidTenantOverride_IgnoresOverrideAndFallsBackToDefault()
    {
        var resolver = BuildResolver(new InMemoryFeatureFlagService(), BrainProvider.MicrosoftAgentFramework);

        var result = await resolver.ResolveAsync(
            TenantId,
            "agent-a",
            new AgentBrainExecutionContext
            {
                Metadata = new Dictionary<string, string>
                {
                    [AgentBrainResolver.TenantOverrideMetadataKey] = "NotAValidProvider"
                }
            });

        Assert.Equal(BrainProvider.MicrosoftAgentFramework, result.Provider);
        Assert.Equal("default", result.ResolutionSource);
        Assert.Same(_mafBrain.Object, result.Brain);
    }

    private AgentBrainResolver BuildResolver(IFeatureFlagService featureFlags, BrainProvider defaultProvider)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentBrain:DefaultProvider"] = defaultProvider.ToString()
            })
            .Build();

        return new AgentBrainResolver(
            featureFlags,
            provider => provider == BrainProvider.MicrosoftAgentFramework ? _mafBrain.Object : _skBrain.Object,
            configuration,
            NullLogger<AgentBrainResolver>.Instance);
    }
}
