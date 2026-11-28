using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.AuthProfiles;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Voice;
using AgentFlow.Api.Workflow;
using AgentFlow.Application.Memory;
using Moq;

namespace AgentFlow.Tests.Unit.Communication;

/// <summary>A call campaign without a workflow calls each contact and hands the call to the assistant.</summary>
public class CampaignCallTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task ACallCampaign_CallsTheContactWithItsOwnOpening()
    {
        VoiceCallRequest? placed = null;
        var voice = new Mock<IVoiceConversationService>();
        voice.Setup(x => x.PlaceCallAsync(It.IsAny<VoiceCallRequest>(), It.IsAny<CancellationToken>()))
            .Callback<VoiceCallRequest, CancellationToken>((r, _) => placed = r)
            .ReturnsAsync(new VoiceCallResult("CA1", "queued", "+50581143874", "voice-1"));
        var (service, contacts, outcomes) = Create(voice.Object);

        await service.RunNowAsync(Tenant, "camp-1", "admin", CampaignRunTrigger.Manual);

        Assert.NotNull(placed);
        Assert.Equal("+50581143874", placed!.PhoneNumber);
        Assert.Equal("Hola Carlos, te llamamos por tu cuenta.", placed.Script);
        var contact = contacts.Last();
        Assert.Equal(CampaignContactExecutionStatus.Sent, contact.Status);
        Assert.Equal("CA1", contact.ChannelMessageId);
        Assert.Equal("CA1", contact.CallId);
        Assert.Equal(VoiceCallMode.Conversation, placed.Mode);
        Assert.Equal(CampaignCallOutcomeStatus.InProgress, outcomes.Last().Status);
        Assert.Equal("CA1", outcomes.Last().CallId);
    }

    [Fact]
    public async Task ANoticeCampaign_PlacesNoticeCalls()
    {
        VoiceCallRequest? placed = null;
        var voice = new Mock<IVoiceConversationService>();
        voice.Setup(x => x.PlaceCallAsync(It.IsAny<VoiceCallRequest>(), It.IsAny<CancellationToken>()))
            .Callback<VoiceCallRequest, CancellationToken>((r, _) => placed = r)
            .ReturnsAsync(new VoiceCallResult("CA2", "queued", "+50581143874", "voice-1"));
        var (service, _, outcomes) = Create(voice.Object, callMode: "notify");

        await service.RunNowAsync(Tenant, "camp-1", "admin", CampaignRunTrigger.Manual);

        Assert.Equal(VoiceCallMode.Notify, placed!.Mode);
        Assert.Equal("notice", outcomes.Last().NextAction);
    }

    [Fact]
    public async Task ACallThatCannotBePlaced_IsAFailedContactWithTheReason()
    {
        var voice = new Mock<IVoiceConversationService>();
        voice.Setup(x => x.PlaceCallAsync(It.IsAny<VoiceCallRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No hay un canal de voz activo."));
        var (service, contacts, outcomes) = Create(voice.Object);

        await service.RunNowAsync(Tenant, "camp-1", "admin", CampaignRunTrigger.Manual);

        var contact = contacts.Last();
        Assert.Equal(CampaignContactExecutionStatus.Failed, contact.Status);
        Assert.Equal("No hay un canal de voz activo.", contact.SkipReason);
        Assert.Equal(CampaignCallOutcomeStatus.Failed, outcomes.Last().Status);
    }

    private static (CampaignExecutionService Service, List<CampaignContactExecutionContract> Contacts, List<CampaignCallOutcomeContract> Outcomes) Create(IVoiceConversationService voice, string? callMode = null)
    {
        var contacts = new List<CampaignContactExecutionContract>();
        var outcomes = new List<CampaignCallOutcomeContract>();
        var campaign = new CampaignContract
        {
            Id = "camp-1",
            TenantId = Tenant,
            Name = "Llamadas",
            Status = CampaignStatus.Active,
            ExecutionMode = CampaignExecutionMode.Direct,
            ChannelAction = CampaignChannelAction.Call,
            CallScriptDraft = "Hola {{primer_nombre}}, te llamamos por tu cuenta.",
            CallMode = callMode,
            AudienceFilterJson = "{}"
        };
        var store = new Mock<ICampaignStore>();
        store.Setup(x => x.GetCampaignAsync(Tenant, "camp-1", It.IsAny<CancellationToken>())).ReturnsAsync(campaign);
        store.Setup(x => x.GetOptedOutRecipientKeysAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<string>());
        store.Setup(x => x.CreateRunAsync(It.IsAny<CampaignRunContract>(), It.IsAny<CancellationToken>())).ReturnsAsync((CampaignRunContract r, CancellationToken _) => r);
        store.Setup(x => x.UpdateRunAsync(It.IsAny<CampaignRunContract>(), It.IsAny<CancellationToken>())).ReturnsAsync((CampaignRunContract r, CancellationToken _) => r);
        store.Setup(x => x.UpsertCampaignAsync(It.IsAny<CampaignContract>(), It.IsAny<CancellationToken>())).ReturnsAsync((CampaignContract c, CancellationToken _) => c);
        store.Setup(x => x.CreateContactExecutionAsync(It.IsAny<CampaignContactExecutionContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CampaignContactExecutionContract c, CancellationToken _) => { contacts.Add(c); return c; });
        store.Setup(x => x.UpdateContactExecutionAsync(It.IsAny<CampaignContactExecutionContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CampaignContactExecutionContract c, CancellationToken _) => { contacts.Add(c); return c; });
        store.Setup(x => x.CreateCallOutcomeAsync(It.IsAny<CampaignCallOutcomeContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CampaignCallOutcomeContract o, CancellationToken _) => { outcomes.Add(o); return o; });
        store.Setup(x => x.UpdateCallOutcomeAsync(It.IsAny<CampaignCallOutcomeContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CampaignCallOutcomeContract o, CancellationToken _) => { outcomes.Add(o); return o; });

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

        var service = new CampaignExecutionService(
            store.Object,
            audience.Object,
            Mock.Of<IConnectStore>(),
            Mock.Of<IWorkflowTriggerService>(),
            Mock.Of<IAuditMemory>(),
            Mock.Of<IRuntimeModelProfileStore>(),
            Mock.Of<IOutboundMessageSender>(),
            voice: voice);
        return (service, contacts, outcomes);
    }
}
