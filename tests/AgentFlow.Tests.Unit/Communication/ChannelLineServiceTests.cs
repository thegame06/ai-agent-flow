using AgentFlow.Abstractions;
using AgentFlow.Api.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Moq;

namespace AgentFlow.Tests.Unit.Communication;

/// <summary>Setting up lines: one owner per number, one default per kind, readable rules.</summary>
public class ChannelLineServiceTests
{
    private const string Tenant = "tenant-a";

    private static ChannelDefinition Channel(string name, ChannelType type = ChannelType.Voice, string tenant = Tenant, string? number = null)
    {
        var channel = ChannelDefinition.Create(tenant, name, type);
        if (number is not null)
        {
            channel.Config[ChannelLines.PhoneNumberKey] = number;
            channel.Config[ChannelLineService.PhoneDigitsKey] = ChannelLines.Digits(number);
        }
        channel.Activate();
        return channel;
    }

    private static (ChannelLineService Service, Mock<IChannelDefinitionRepository> Repo) Create(
        IReadOnlyList<ChannelDefinition> mine, IReadOnlyList<ChannelDefinition>? platform = null)
    {
        var repo = new Mock<IChannelDefinitionRepository>();
        repo.Setup(x => x.GetAllAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(mine.ToList());
        foreach (var channel in mine)
            repo.Setup(x => x.GetByIdAsync(channel.Id, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(channel);
        repo.Setup(x => x.UpdateAsync(It.IsAny<ChannelDefinition>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var everyone = mine.Concat(platform ?? []).ToList();
        repo.Setup(x => x.FindAcrossTenantsByConfigAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string value, CancellationToken _) =>
                everyone.Where(c => c.Config.GetValueOrDefault(key) == value).ToList());
        return (new ChannelLineService(repo.Object), repo);
    }

    [Fact]
    public async Task ALine_GetsItsNumberPurposeHoursAndDefault()
    {
        var voice = Channel("Voz");
        var (service, _) = Create([voice]);

        var (line, error) = await service.ApplyAsync(Tenant, voice.Id, new ChannelLineSettings
        {
            PhoneNumber = "+52 55 1234 5678", Purpose = "cobranza", ContactHours = "8:00-21:00", DefaultLine = true
        }, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal("+525512345678", line!.PhoneNumber);
        Assert.Equal(ChannelLines.Purposes.Collections, line.Purpose);
        Assert.Equal("08:00-21:00", line.ContactHours);
        Assert.True(line.DefaultLine);
    }

    [Fact]
    public async Task ANumberAlreadyOnAnotherLineOfTheTenant_IsRefusedByName()
    {
        var first = Channel("Cobranza", number: "+525512345678");
        var second = Channel("Ventas");
        var (service, _) = Create([first, second]);

        var (line, error) = await service.ApplyAsync(Tenant, second.Id, new ChannelLineSettings { PhoneNumber = "525512345678" }, CancellationToken.None);

        Assert.Null(line);
        Assert.Equal("Ese número ya es de la línea «Cobranza».", error);
    }

    [Fact]
    public async Task ANumberOfAnotherTenant_IsRefused_WithoutNamingThatTenant()
    {
        var mine = Channel("Voz");
        var theirs = Channel("Su línea", tenant: "tenant-b", number: "+525512345678");
        var (service, _) = Create([mine], [theirs]);

        var (_, error) = await service.ApplyAsync(Tenant, mine.Id, new ChannelLineSettings { PhoneNumber = "+525512345678" }, CancellationToken.None);

        Assert.StartsWith("Ese número ya está registrado en otra cuenta", error);
        Assert.DoesNotContain("Su línea", error);
    }

    [Fact]
    public async Task ChoosingADefaultLine_ReleasesTheOtherDefaultOfItsKind()
    {
        var old = Channel("Antigua");
        old.Config[ChannelLines.DefaultLineKey] = "true";
        var whatsapp = Channel("WhatsApp", ChannelType.WhatsApp);
        whatsapp.Config[ChannelLines.DefaultLineKey] = "true";
        var chosen = Channel("Nueva");
        var (service, _) = Create([old, whatsapp, chosen]);

        await service.ApplyAsync(Tenant, chosen.Id, new ChannelLineSettings { DefaultLine = true }, CancellationToken.None);

        Assert.False(ChannelLines.IsDefault(old));
        Assert.True(ChannelLines.IsDefault(whatsapp)); // another kind keeps its default
        Assert.True(ChannelLines.IsDefault(chosen));
    }

    [Theory]
    [InlineData("12345", null, null, "código de país")]
    [InlineData(null, "rifas", null, "cobranza, ventas, marketing o general")]
    [InlineData(null, null, "todo el día", "08:00-21:00")]
    public async Task UnreadableSettings_AreExplained(string? number, string? purpose, string? hours, string expected)
    {
        var voice = Channel("Voz");
        var (service, _) = Create([voice]);

        var (_, error) = await service.ApplyAsync(Tenant, voice.Id, new ChannelLineSettings
        {
            PhoneNumber = number, Purpose = purpose, ContactHours = hours
        }, CancellationToken.None);

        Assert.Contains(expected, error);
    }

    [Fact]
    public async Task ASharedWebhook_FindsTheOneTenantThatOwnsTheNumber()
    {
        var theirs = Channel("Ventas B", tenant: "tenant-b", number: "+525512345678");
        var (service, _) = Create([], [theirs]);

        var owner = await service.FindOwnerAsync("+52 55 1234 5678", null, ChannelLines.IsVoice, CancellationToken.None);

        Assert.Same(theirs, owner);
    }

    [Fact]
    public async Task ANumberClaimedByTwoTenants_IsRoutedToNobody()
    {
        var b = Channel("B", tenant: "tenant-b", number: "+525512345678");
        var c = Channel("C", tenant: "tenant-c", number: "+525512345678");
        var (service, _) = Create([], [b, c]);

        Assert.Null(await service.FindOwnerAsync("+525512345678", null, ChannelLines.IsVoice, CancellationToken.None));
    }

    [Fact]
    public async Task AMetaNumberId_FindsItsWhatsAppLine()
    {
        var line = Channel("Meta B", ChannelType.WhatsApp, tenant: "tenant-b");
        line.Config[ChannelLines.PhoneNumberIdKey] = "1490687614121979";
        var (service, _) = Create([], [line]);

        Assert.Same(line, await service.FindOwnerAsync(null, "1490687614121979", t => t == ChannelType.WhatsApp, CancellationToken.None));
    }
}
