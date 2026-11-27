using AgentFlow.Abstractions;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Controllers;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Moq;

namespace AgentFlow.Tests.Unit.Messaging;

public sealed class WhatsAppChannelMetaTests
{
    private const string Tenant = "tenant-1";

    [Fact]
    public async Task Saving_PutsTheAccountInAnEncryptedConnection_AndTheChannelSendsThroughIt()
    {
        var (service, channel, connections, accounts) = Create(new Dictionary<string, string> { ["AuthMode"] = "business" });

        var (view, error) = await service.SaveAsync(Tenant, channel.Id, new WhatsAppChannelMetaRequest
        {
            BusinessAccountId = "waba-1",
            PhoneNumberId = "phone-1",
            AccessToken = "EAAB-secret",
            AppSecret = "app-secret"
        }, "u");

        Assert.Null(error);
        Assert.True(view!.HasToken && view.HasAppSecret);
        Assert.Equal(("business", "meta", $"meta-{channel.Id}", "phone-1", "waba-1"),
            (channel.Config["AuthMode"], channel.Config["Provider"], channel.Config["ConnectionId"], channel.Config["PhoneNumberId"], channel.Config["BusinessAccountId"]));
        Assert.DoesNotContain(channel.Config.Values, v => v.Contains("EAAB"));
        Assert.DoesNotContain("EAAB", connections.Secrets.Values.Single().CipherText); // encrypted at rest

        var account = await accounts.GetForChannelAsync(Tenant, channel.Id);
        Assert.Equal(("waba-1", "EAAB-secret", "app-secret"), (account!.BusinessAccountId, account.AccessToken, account.AppSecret));
        Assert.Equal(channel.Id, Assert.Single(account.Channels).ChannelId);
    }

    [Fact]
    public async Task Saving_WithoutRetypingSecrets_KeepsThem_AndAnOldPlainTokenIsMovedOut()
    {
        // An old channel that kept the Meta token in its plain config.
        var (service, channel, _, accounts) = Create(new Dictionary<string, string> { ["AuthMode"] = "business", ["ApiToken"] = "EAAB-old", ["PhoneNumberId"] = "phone-1" });

        var first = await service.GetAsync(Tenant, channel.Id);
        Assert.True(first!.TokenInPlainConfig);

        await service.SaveAsync(Tenant, channel.Id, new WhatsAppChannelMetaRequest { BusinessAccountId = "waba-1", PhoneNumberId = "phone-1", AppSecret = "s1" }, "u");
        Assert.False(channel.Config.ContainsKey("ApiToken"));
        Assert.Equal("EAAB-old", (await accounts.GetForChannelAsync(Tenant, channel.Id))!.AccessToken);

        // Only the token changes: the app secret stays.
        await service.SaveAsync(Tenant, channel.Id, new WhatsAppChannelMetaRequest { BusinessAccountId = "waba-1", PhoneNumberId = "phone-1", AccessToken = "EAAB-new" }, "u");
        var account = await accounts.GetForChannelAsync(Tenant, channel.Id);
        Assert.Equal(("EAAB-new", "s1"), (account!.AccessToken, account.AppSecret));
    }

    [Fact]
    public async Task Saving_WithoutAnyToken_IsRefused()
    {
        var (service, channel, _, _) = Create(new Dictionary<string, string> { ["AuthMode"] = "business" });
        var (view, error) = await service.SaveAsync(Tenant, channel.Id, new WhatsAppChannelMetaRequest { BusinessAccountId = "waba-1", PhoneNumberId = "p" }, "u");
        Assert.Null(view);
        Assert.Contains("token", error);
    }

    [Fact]
    public void IncomingMessage_GoesToTheChannelOfTheNumberItWasSentTo()
    {
        var cobranza = Channel("Cobranza", new() { ["AuthMode"] = "business", ["PhoneNumberId"] = "111" });
        var ventas = Channel("Ventas", new() { ["AuthMode"] = "business", ["PhoneNumberId"] = "222" });
        var qr = Channel("QR", new() { ["AuthMode"] = "qr" });
        var channels = new[] { qr, cobranza, ventas };

        Assert.Same(ventas, WhatsAppWebhookController.ChannelForNumber(channels, "222"));
        Assert.Same(cobranza, WhatsAppWebhookController.ChannelForNumber(channels, "111"));
        // Unknown number: a Business channel before the QR one.
        Assert.Same(cobranza, WhatsAppWebhookController.ChannelForNumber(channels, "999"));
    }

    [Fact]
    public void ChannelList_NeverShowsCredentials()
    {
        var safe = ChannelDto.SafeConfig(new Dictionary<string, string> { ["ApiToken"] = "EAAB", ["PhoneNumberId"] = "1", ["AppSecret"] = "x" });
        Assert.Equal(("********", "1", "********"), (safe["ApiToken"], safe["PhoneNumberId"], safe["AppSecret"]));
    }

    // ------------------------------------------------------------------

    private static ChannelDefinition Channel(string name, Dictionary<string, string> config)
    {
        var channel = ChannelDefinition.Create(Tenant, name, ChannelType.WhatsApp, config);
        channel.Activate();
        return channel;
    }

    private static (WhatsAppChannelMetaService, ChannelDefinition, InMemoryConnections, MetaWhatsAppAccounts) Create(Dictionary<string, string> config)
    {
        var channel = ChannelDefinition.Create(Tenant, "WhatsApp Cobranza", ChannelType.WhatsApp, config);
        var repo = new Mock<IChannelDefinitionRepository>();
        repo.Setup(r => r.GetByIdAsync(channel.Id, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(channel);
        repo.Setup(r => r.GetByTypeAsync(ChannelType.WhatsApp, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync([channel]);
        repo.Setup(r => r.UpdateAsync(channel, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var connections = new InMemoryConnections();
        var protection = new EphemeralDataProtectionProvider();
        return (new WhatsAppChannelMetaService(repo.Object, connections, protection), channel, connections,
            new MetaWhatsAppAccounts(connections, protection, repo.Object));
    }

    private sealed class InMemoryConnections : ITenantConnectionStore
    {
        public Dictionary<string, TenantConnectionContract> Connections { get; } = [];
        public Dictionary<string, TenantConnectionSecretContract> Secrets { get; } = [];

        public Task<IReadOnlyList<TenantConnectionContract>> GetConnectionsAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantConnectionContract>>(Connections.Values.ToList());
        public Task<TenantConnectionContract?> GetConnectionAsync(string tenantId, string connectionId, CancellationToken ct = default)
            => Task.FromResult(Connections.GetValueOrDefault(connectionId));
        public Task<TenantConnectionContract> UpsertConnectionAsync(TenantConnectionContract connection, CancellationToken ct = default)
        {
            Connections[connection.Id] = connection;
            return Task.FromResult(connection);
        }
        public Task<TenantConnectionSecretContract?> GetSecretAsync(string tenantId, string connectionId, CancellationToken ct = default)
            => Task.FromResult(Secrets.GetValueOrDefault(connectionId));
        public Task<TenantConnectionSecretContract> UpsertSecretAsync(TenantConnectionSecretContract secret, CancellationToken ct = default)
        {
            Secrets[secret.ConnectionId] = secret;
            return Task.FromResult(secret);
        }
        public Task RecordUsageAsync(ConnectionUsageAuditContract entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ConnectionUsageAuditContract>> GetUsageAuditAsync(string tenantId, string connectionId, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConnectionUsageAuditContract>>([]);
    }
}
