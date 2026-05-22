using AgentFlow.Core.Engine;
using AgentFlow.Infrastructure.Providers;

namespace AgentFlow.Tests.Unit.Communication;

public class ProviderRegistryTests
{
    [Fact]
    public void Register_DuplicateProviderId_Throws()
    {
        var registry = new InMemoryProviderRegistry();
        registry.Register(new TwilioVoiceProviderAdapter());

        Assert.Throws<InvalidOperationException>(() => registry.Register(new TwilioVoiceProviderAdapter()));
    }
}
