using AgentFlow.Abstractions;
using AgentFlow.Api.Connect;
using AgentFlow.Core.Engine;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Extensions;
using Moq;
using AgentFlow.Infrastructure.Providers;

namespace AgentFlow.Tests.Unit.Communication;

public class TenantProviderResolverTests
{
    [Fact]
    public async Task ResolveRequiredAsync_TwilioVoiceCapability_ReturnsTwilioVoiceAdapter()
    {
        var store = new Mock<ITenantConnectionStore>();
        store.Setup(x => x.GetConnectionsAsync("tenant-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TenantConnectionContract
                {
                    Id = "conn-1",
                    TenantId = "tenant-a",
                    Name = "Twilio Main",
                    Type = TenantConnectionType.Messaging,
                    ConnectorId = "twilio",
                    Config = new Dictionary<string, string>
                    {
                        ["provider"] = "twilio",
                        ["accountSid"] = "AC123",
                        ["fromPhoneNumber"] = "+15550001111"
                    }
                }
            ]);
        var dataProtection = DataProtectionProvider.Create("agentflow-tests");
        var protector = dataProtection.CreateProtector("tenant-connections-secrets-v1");
        var cipherText = protector.Protect("{\"authToken\":\"secret-token\"}");

        store.Setup(x => x.GetSecretAsync("tenant-a", "conn-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantConnectionSecretContract
            {
                ConnectionId = "conn-1",
                TenantId = "tenant-a",
                CipherText = cipherText
            });

        var registry = new InMemoryProviderRegistry();
        registry.Register(new TwilioVoiceProviderAdapter());

        var resolver = new TenantProviderResolver(store.Object, dataProtection, registry);

        var result = await resolver.ResolveRequiredAsync<IVoiceCallProviderAdapter>(new ProviderResolutionContext
        {
            TenantId = "tenant-a",
            Capability = CommunicationCapabilities.CallOutbound,
            Channel = "voice"
        });

        Assert.Equal("twilio", result.Adapter.ProviderId);
        Assert.Equal("conn-1", result.Connection.ConnectionId);
        Assert.Equal("secret-token", result.Connection.Secret["authToken"]);
    }

    [Fact]
    public async Task ResolveRequiredAsync_WithoutSecret_Throws()
    {
        var store = new Mock<ITenantConnectionStore>();
        store.Setup(x => x.GetConnectionsAsync("tenant-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TenantConnectionContract
                {
                    Id = "conn-1",
                    TenantId = "tenant-a",
                    Name = "Twilio Main",
                    Type = TenantConnectionType.Messaging,
                    ConnectorId = "twilio",
                    Config = new Dictionary<string, string> { ["provider"] = "twilio" }
                }
            ]);
        store.Setup(x => x.GetSecretAsync("tenant-a", "conn-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantConnectionSecretContract?)null);

        var dataProtection = DataProtectionProvider.Create("agentflow-tests");

        var registry = new InMemoryProviderRegistry();
        registry.Register(new TwilioVoiceProviderAdapter());
        var resolver = new TenantProviderResolver(store.Object, dataProtection, registry);

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveRequiredAsync<IVoiceCallProviderAdapter>(
            new ProviderResolutionContext
            {
                TenantId = "tenant-a",
                Capability = CommunicationCapabilities.CallOutbound,
                Channel = "voice"
            }));
    }
}
