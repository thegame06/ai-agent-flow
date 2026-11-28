using AgentFlow.Abstractions;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.AuthProfiles;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Channels;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Voice;
using AgentFlow.Api.Workflow;
using AgentFlow.Application.Memory;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Moq;

namespace AgentFlow.Tests.Unit.Communication;

/// <summary>A campaign goes out through its line, under that line's rules.</summary>
public class CampaignLineTests
{
    private const string Tenant = "tenant-a";

    private static ChannelDefinition Line(string purpose, string? hours = null)
    {
        var line = ChannelDefinition.Create(Tenant, $"Línea de {purpose}", ChannelType.Voice);
        line.Config[ChannelLines.PurposeKey] = purpose;
        line.Config[ChannelLines.PhoneNumberKey] = "+525512345678";
        if (hours is not null)
        {
            line.Config[ChannelLines.ContactHoursKey] = hours;
            line.Config[ChannelLines.TimeZoneKey] = "UTC";
        }
        line.Activate();
        return line;
    }

    [Fact]
    public async Task ACampaignOnItsLine_CallsFromThatLine()
    {
        var line = Line("collections");
        var h = Harness.Create(line, CampaignType.Collections);

        await h.Service.RunNowAsync(Tenant, "camp-1", "admin", CampaignRunTrigger.Manual);

        Assert.Equal(line.Id, h.Placed!.ChannelKey);
    }

    [Fact]
    public async Task ACollectionsCampaign_DoesNotGoOutThroughASalesLine()
    {
        var h = Harness.Create(Line("sales"), CampaignType.Collections);

        var run = await h.Service.RunNowAsync(Tenant, "camp-1", "admin", CampaignRunTrigger.Manual);

        Assert.Equal(CampaignRunStatus.Cancelled, run.Status);
        Assert.Contains("es de ventas y esta campaña es de cobranza", Reasons(run));
        Assert.Null(h.Placed);
        Assert.Empty(h.Contacts);
    }

    [Fact]
    public async Task ASalesCampaign_MayUseAMarketingOrGeneralLine()
    {
        var marketing = Harness.Create(Line("marketing"), CampaignType.Sales);
        await marketing.Service.RunNowAsync(Tenant, "camp-1", "admin", CampaignRunTrigger.Manual);
        Assert.NotNull(marketing.Placed);

        var general = Harness.Create(Line("general"), CampaignType.Collections);
        await general.Service.RunNowAsync(Tenant, "camp-1", "admin", CampaignRunTrigger.Manual);
        Assert.NotNull(general.Placed);
    }

    [Fact]
    public async Task OutsideTheLinesHours_NobodyIsContacted_AndAScheduledCampaignWaitsForThem()
    {
        var hour = DateTimeOffset.UtcNow.Hour;
        // A two-hour window that starts three hours from now: closed at this moment.
        var hours = $"{(hour + 3) % 24:00}:00-{(hour + 5) % 24:00}:00";
        var h = Harness.Create(Line("collections", hours), CampaignType.Collections);

        var run = await h.Service.RunNowAsync(Tenant, "camp-1", "scheduler", CampaignRunTrigger.Schedule);

        Assert.Equal(CampaignRunStatus.Cancelled, run.Status);
        Assert.Contains("Fuera del horario de contacto", Reasons(run));
        Assert.Null(h.Placed);
        var rescheduled = Assert.Single(h.Upserts).NextRunAt!.Value;
        Assert.InRange(rescheduled - DateTimeOffset.UtcNow, TimeSpan.FromHours(2), TimeSpan.FromHours(3));
    }

    [Fact]
    public async Task ACollectionsCampaign_HonorsTheOptOutsThatApplyToCollections()
    {
        var h = Harness.Create(Line("collections"), CampaignType.Collections,
            optedOutForCollections: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RecipientKeys.From("+50581143874")! });

        await h.Service.RunNowAsync(Tenant, "camp-1", "admin", CampaignRunTrigger.Manual);

        Assert.Null(h.Placed);
        h.Store.Verify(x => x.GetOptedOutRecipientKeysForAsync(Tenant, ChannelLines.Purposes.Collections, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static string Reasons(CampaignRunContract run)
        => string.Join(" ", System.Text.Json.JsonSerializer.Deserialize<string[]>(run.ErrorSummaryJson!)!);

    [Theory]
    [InlineData(CampaignType.Collections, "collections")]
    [InlineData(CampaignType.Reminder, "collections")]
    [InlineData(CampaignType.Sales, "sales")]
    [InlineData(CampaignType.Reactivation, "sales")]
    [InlineData(CampaignType.Custom, "general")]
    public void ACampaignsPurpose_ComesFromItsType(CampaignType type, string purpose)
        => Assert.Equal(purpose, CampaignChannels.PurposeOf(type));

    private sealed class Harness
    {
        public CampaignExecutionService Service { get; private set; } = null!;
        public Mock<ICampaignStore> Store { get; } = new();
        public List<CampaignContactExecutionContract> Contacts { get; } = [];
        public List<CampaignContract> Upserts { get; } = [];
        public VoiceCallRequest? Placed { get; set; }

        public static Harness Create(ChannelDefinition line, CampaignType type, IReadOnlySet<string>? optedOutForCollections = null)
        {
            var campaign = new CampaignContract
            {
                Id = "camp-1",
                TenantId = Tenant,
                Name = "Llamadas",
                CampaignType = type,
                Status = CampaignStatus.Active,
                ExecutionMode = CampaignExecutionMode.Direct,
                ChannelAction = CampaignChannelAction.Call,
                Channel = line.Id,
                CallScriptDraft = "Hola {{primer_nombre}}.",
                AudienceFilterJson = "{}"
            };
            var harness = new Harness();
            var store = harness.Store;
            var voice = new Mock<IVoiceConversationService>();
            voice.Setup(x => x.PlaceCallAsync(It.IsAny<VoiceCallRequest>(), It.IsAny<CancellationToken>()))
                .Callback<VoiceCallRequest, CancellationToken>((r, _) => harness.Placed = r)
                .ReturnsAsync(new VoiceCallResult("CA1", "queued", "+50581143874", line.Id));

            store.Setup(x => x.GetCampaignAsync(Tenant, "camp-1", It.IsAny<CancellationToken>())).ReturnsAsync(campaign);
            store.Setup(x => x.GetOptedOutRecipientKeysAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<string>());
            store.Setup(x => x.GetOptedOutRecipientKeysForAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(optedOutForCollections ?? new HashSet<string>());
            store.Setup(x => x.CreateRunAsync(It.IsAny<CampaignRunContract>(), It.IsAny<CancellationToken>())).ReturnsAsync((CampaignRunContract r, CancellationToken _) => r);
            store.Setup(x => x.UpdateRunAsync(It.IsAny<CampaignRunContract>(), It.IsAny<CancellationToken>())).ReturnsAsync((CampaignRunContract r, CancellationToken _) => r);
            store.Setup(x => x.UpsertCampaignAsync(It.IsAny<CampaignContract>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CampaignContract c, CancellationToken _) => { harness.Upserts.Add(c); return c; });
            store.Setup(x => x.CreateContactExecutionAsync(It.IsAny<CampaignContactExecutionContract>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CampaignContactExecutionContract c, CancellationToken _) => { harness.Contacts.Add(c); return c; });
            store.Setup(x => x.UpdateContactExecutionAsync(It.IsAny<CampaignContactExecutionContract>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CampaignContactExecutionContract c, CancellationToken _) => { harness.Contacts.Add(c); return c; });
            store.Setup(x => x.CreateCallOutcomeAsync(It.IsAny<CampaignCallOutcomeContract>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CampaignCallOutcomeContract o, CancellationToken _) => o);
            store.Setup(x => x.UpdateCallOutcomeAsync(It.IsAny<CampaignCallOutcomeContract>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CampaignCallOutcomeContract o, CancellationToken _) => o);

            var audience = new Mock<ICampaignAudienceService>();
            audience.Setup(x => x.ResolveAsync(Tenant, It.IsAny<string>(), "camp-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CampaignAudiencePreviewContract
                {
                    EstimatedCount = 1,
                    Contacts =
                    [
                        new CampaignAudienceContactContract
                        {
                            PartyId = "p1",
                            Recipient = "+50581143874",
                            DisplayName = "Carlos Méndez",
                            Variables = new Dictionary<string, string> { ["primer_nombre"] = "Carlos" }
                        }
                    ]
                });

            var channels = new Mock<IChannelDefinitionRepository>();
            channels.Setup(x => x.GetByIdAsync(line.Id, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(line);

            harness.Service = new CampaignExecutionService(
                store.Object,
                audience.Object,
                Mock.Of<IConnectStore>(),
                Mock.Of<IWorkflowTriggerService>(),
                Mock.Of<IAuditMemory>(),
                Mock.Of<IRuntimeModelProfileStore>(),
                Mock.Of<IOutboundMessageSender>(),
                channels: channels.Object,
                voice: voice.Object);
            return harness;
        }
    }
}
