using AgentFlow.Abstractions;
using AgentFlow.Api.Channels;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Voice;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;

namespace AgentFlow.Tests.Unit.Communication;

/// <summary>A tenant's lines: each number reaches its own assistant, and calls go out by the line chosen.</summary>
public class ChannelLinesTests
{
    private const string Tenant = "tenant-a";

    private static ChannelDefinition Line(string name, ChannelType type = ChannelType.Voice, string? number = null,
        string? purpose = null, bool isDefault = false, string tenant = Tenant)
    {
        var channel = ChannelDefinition.Create(tenant, name, type);
        channel.Config["ConnectionId"] = "twilio-main";
        if (number is not null) channel.Config[ChannelLines.PhoneNumberKey] = number;
        if (purpose is not null) channel.Config[ChannelLines.PurposeKey] = purpose;
        if (isDefault) channel.Config[ChannelLines.DefaultLineKey] = "true";
        channel.Activate();
        return channel;
    }

    [Fact]
    public void ACallToANumber_ReachesTheLineWithThatNumber_WhateverItsFormat()
    {
        var collections = Line("Cobranza", number: "+19016608125", purpose: "cobranza");
        var sales = Line("Ventas", number: "+52 55 1234 5678", purpose: "ventas");

        Assert.Same(sales, ChannelLines.Pick([collections, sales], "+525512345678"));
        Assert.Same(collections, ChannelLines.Pick([collections, sales], "whatsapp:+1 (901) 660-8125"));
        Assert.Equal(ChannelLines.Purposes.Sales, ChannelLines.PurposeOf(sales));
        Assert.Equal(ChannelLines.Purposes.Collections, ChannelLines.PurposeOf(collections));
    }

    [Fact]
    public void AnUnknownNumber_GoesToTheDefaultLine_ElseToTheLineWithoutANumber()
    {
        var withNumber = Line("Ventas", number: "+525512345678");
        var generic = Line("General");
        var marked = Line("Cobranza", number: "+19016608125", isDefault: true);

        Assert.Same(marked, ChannelLines.Pick([withNumber, generic, marked], "+15550000000"));
        Assert.Same(generic, ChannelLines.Pick([withNumber, generic], null));
    }

    [Fact]
    public void AnInactiveLine_IsNeverPicked()
    {
        var off = Line("Apagada", number: "+525512345678");
        off.Deactivate();
        var on = Line("General");

        Assert.Same(on, ChannelLines.Pick([off, on], "+525512345678"));
        Assert.Null(ChannelLines.Pick([off], "+525512345678"));
    }

    [Fact]
    public void ANumber_BelongsToOneLineOfAKind()
    {
        var first = Line("Cobranza", number: "+19016608125");
        var sameNumberVoice = Line("Otra voz", ChannelType.CallCenter, number: "19016608125");
        var sameNumberWhatsApp = Line("WhatsApp", ChannelType.WhatsApp, number: "+19016608125");

        Assert.Same(first, ChannelLines.NumberTakenBy(sameNumberVoice, [first]));
        // The same number may take calls and WhatsApp messages.
        Assert.Null(ChannelLines.NumberTakenBy(sameNumberWhatsApp, [first]));
    }

    [Fact]
    public void AMetaNumberId_BelongsToOneWhatsAppLine()
    {
        var first = Line("Meta 1", ChannelType.WhatsApp);
        first.Config[ChannelLines.PhoneNumberIdKey] = "1490687614121979";
        var second = Line("Meta 2", ChannelType.WhatsApp);
        second.Config[ChannelLines.PhoneNumberIdKey] = "1490687614121979";

        Assert.Same(first, ChannelLines.NumberTakenBy(second, [first, second]));
    }

    [Theory]
    [InlineData("08:00-21:00", 14, true)]
    [InlineData("08:00-21:00", 22, false)]
    [InlineData("08:00-21:00", 7, false)]
    [InlineData("22:00-06:00", 23, true)]
    [InlineData("", 3, true)]
    [InlineData("nonsense", 3, true)]
    public void ContactHours_AreTheLinesLocalTime(string hours, int localHour, bool allowed)
    {
        var line = Line("Cobranza");
        line.Config[ChannelLines.ContactHoursKey] = hours;
        line.Config[ChannelLines.TimeZoneKey] = "UTC";

        Assert.Equal(allowed, ChannelLines.WithinContactHours(line, new DateTimeOffset(2026, 11, 28, localHour, 30, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("3f4a8179fa174610a0e4cef0fb0a73ec", true)]
    [InlineData("voice", false)]
    [InlineData("WhatsApp", false)]
    [InlineData("callcenter", false)]
    [InlineData("../../x", false)]
    [InlineData(null, false)]
    public void ALineIsNamedByItsChannelId_NotByAType(string? key, bool isLine)
        => Assert.Equal(isLine, ChannelLines.IsLineKey(key));

    [Fact]
    public async Task AnInboundCall_IsAnsweredByTheLineOfTheNumberCalled()
    {
        var collections = Line("Cobranza", number: "+19016608125");
        var sales = Line("Ventas", number: "+525512345678");
        sales.Config[VoiceConversationService.GreetingKey] = "Hola, área de ventas.";
        var service = Service([collections, sales]);

        var twiml = await service.GreetAsync(Tenant, "voice", "+525512345678", CancellationToken.None);

        Assert.Contains("Hola, área de ventas.", twiml);
        Assert.Contains($"channel={sales.Id}", twiml);
    }

    [Fact]
    public async Task ACallFromAChosenLine_UsesItsNumberAsCallerId_AndNamesItInEveryCallback()
    {
        var collections = Line("Cobranza", number: "+19016608125", isDefault: true);
        var sales = Line("Ventas", number: "+525512345678");
        ProviderVoiceCallRequest? sent = null;
        var service = Service([collections, sales], r => sent = r);

        var result = await service.PlaceCallAsync(new VoiceCallRequest
        {
            TenantId = Tenant, PhoneNumber = "+50581143874", ChannelKey = sales.Id
        }, CancellationToken.None);

        Assert.Equal(sales.Id, result.ChannelId);
        Assert.Equal("+525512345678", sent!.FromNumber);
        Assert.Contains($"channel={sales.Id}", sent.ConversationUrl);
        Assert.Contains($"channel={sales.Id}", sent.StatusCallbackUrl);
    }

    [Fact]
    public async Task ALineOfAnotherTenant_CannotBeUsed()
    {
        var foreign = Line("Ajena", number: "+525512345678", tenant: "tenant-b");
        var channels = new Mock<IChannelDefinitionRepository>();
        channels.Setup(x => x.GetByIdAsync(foreign.Id, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        var service = new VoiceConversationService(channels.Object, Mock.Of<IInboundMessageIngest>(), Mock.Of<IProviderResolver>(),
            NullLogger<VoiceConversationService>.Instance, Config());

        Assert.Null(await service.ResolveLineAsync(Tenant, foreign.Id, null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlaceCallAsync(
            new VoiceCallRequest { TenantId = Tenant, PhoneNumber = "+50581143874", ChannelKey = foreign.Id }, CancellationToken.None));
    }

    [Fact]
    public async Task AWebhookSignedByAnyOfTheTenantsTwilioAccounts_IsValid_AndNoOtherIs()
    {
        var protection = new EphemeralDataProtectionProvider();
        var protector = protection.CreateProtector("tenant-connections-secrets-v1");
        var store = new Mock<ITenantConnectionStore>();
        store.Setup(x => x.GetConnectionsAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new TenantConnectionContract { Id = "twilio-main", TenantId = Tenant, ConnectorId = "twilio", Config = new Dictionary<string, string> { ["accountSid"] = "AC-main" } },
            new TenantConnectionContract { Id = "twilio-sales", TenantId = Tenant, ConnectorId = "twilio", Config = new Dictionary<string, string> { ["accountSid"] = "AC-sales" } }
        ]);
        store.Setup(x => x.GetSecretAsync(Tenant, "twilio-main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantConnectionSecretContract { CipherText = protector.Protect("{\"authToken\":\"test-token-main\"}") });
        store.Setup(x => x.GetSecretAsync(Tenant, "twilio-sales", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantConnectionSecretContract { CipherText = protector.Protect("{\"authToken\":\"test-token-sales\"}") });
        var validator = new TwilioWebhookSignatureValidator(store.Object, protection, NullLogger<TwilioWebhookSignatureValidator>.Instance);

        Assert.True(await validator.IsValidAsync(Tenant, Signed("test-token-sales", "AC-sales")));
        Assert.True(await validator.IsValidAsync(Tenant, Signed("test-token-main", "AC-main")));
        Assert.False(await validator.IsValidAsync(Tenant, Signed("test-token-of-someone-else", "AC-sales")));
    }

    private static HttpRequest Signed(string authToken, string accountSid)
    {
        var form = new Dictionary<string, string> { ["AccountSid"] = accountSid, ["CallSid"] = "CA1", ["CallStatus"] = "ringing" };
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("agentflow.example.com");
        context.Request.Path = $"/api/v1/tenants/{Tenant}/webhooks/twilio/voice/status";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(form.ToDictionary(x => x.Key, x => new StringValues(x.Value)));
        var url = $"https://agentflow.example.com/api/v1/tenants/{Tenant}/webhooks/twilio/voice/status";
        context.Request.Headers["X-Twilio-Signature"] = TwilioWebhookSignatureValidator.BuildTwilioSignature(url, form, authToken);
        return context.Request;
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Webhooks:PublicBaseUrl"] = "https://agentflow.example.com",
            ["Messaging:DefaultCountryCode"] = "505"
        })
        .Build();

    private static VoiceConversationService Service(IReadOnlyList<ChannelDefinition> lines, Action<ProviderVoiceCallRequest>? sent = null)
    {
        var channels = new Mock<IChannelDefinitionRepository>();
        channels.Setup(x => x.GetByTypeAsync(ChannelType.Voice, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(lines.ToList());
        foreach (var line in lines)
            channels.Setup(x => x.GetByIdAsync(line.Id, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(line);

        var adapter = new Mock<IVoiceCallProviderAdapter>();
        adapter.SetupGet(x => x.ProviderId).Returns("twilio");
        adapter.Setup(x => x.PlaceCallAsync(It.IsAny<ProviderConnectionProfile>(), It.IsAny<ProviderVoiceCallRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderConnectionProfile, ProviderVoiceCallRequest, CancellationToken>((_, r, _) => sent?.Invoke(r))
            .ReturnsAsync(new ProviderVoiceCallResult { ProviderCallId = "CA1", ProviderStatus = "queued" });
        var providers = new Mock<IProviderResolver>();
        providers.Setup(x => x.ResolveRequiredAsync<IVoiceCallProviderAdapter>(It.IsAny<ProviderResolutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedProviderAdapter<IVoiceCallProviderAdapter>(adapter.Object, new ProviderConnectionProfile
            {
                ConnectionId = "twilio-main", TenantId = Tenant, ProviderId = "twilio", ConnectorId = "twilio"
            }));
        return new VoiceConversationService(channels.Object, Mock.Of<IInboundMessageIngest>(), providers.Object,
            NullLogger<VoiceConversationService>.Instance, Config());
    }
}
