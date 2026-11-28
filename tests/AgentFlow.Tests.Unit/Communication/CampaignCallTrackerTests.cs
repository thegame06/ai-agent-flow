using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Communication;

/// <summary>A call campaign's contacts and outcomes follow what happened on the phone.</summary>
public class CampaignCallTrackerTests
{
    private const string Tenant = "tenant-a";

    [Theory]
    [InlineData("ringing", null, null, null)]
    [InlineData("in-progress", "human", CampaignContactExecutionStatus.Connected, CampaignCallOutcomeStatus.InProgress)]
    [InlineData("completed", "human", CampaignContactExecutionStatus.Read, CampaignCallOutcomeStatus.Completed)]
    [InlineData("completed", null, CampaignContactExecutionStatus.Read, CampaignCallOutcomeStatus.Completed)]
    [InlineData("completed", "machine_end_beep", CampaignContactExecutionStatus.Delivered, CampaignCallOutcomeStatus.Voicemail)]
    [InlineData("busy", null, CampaignContactExecutionStatus.Failed, CampaignCallOutcomeStatus.NoAnswer)]
    [InlineData("no-answer", null, CampaignContactExecutionStatus.Failed, CampaignCallOutcomeStatus.NoAnswer)]
    [InlineData("failed", null, CampaignContactExecutionStatus.Failed, CampaignCallOutcomeStatus.Failed)]
    [InlineData("canceled", null, CampaignContactExecutionStatus.Failed, CampaignCallOutcomeStatus.Failed)]
    public void EachTwilioStatus_HasItsMeaningForTheContact(string status, string? answeredBy,
        CampaignContactExecutionStatus? contact, CampaignCallOutcomeStatus? outcome)
    {
        var effect = CampaignCallTracker.Map(status, answeredBy);

        Assert.Equal(contact, effect.ContactStatus);
        Assert.Equal(outcome, effect.OutcomeStatus);
    }

    [Fact]
    public void AVoicemailDetectedOnAnswer_StaysVoicemailWhenTheCallEnds()
        => Assert.Equal(CampaignCallOutcomeStatus.Voicemail, CampaignCallTracker.Map("completed", null, wasVoicemail: true).OutcomeStatus);

    [Fact]
    public async Task AnAnsweredCall_IsReadAndCompleted_WithItsDuration()
    {
        var (tracker, store, outcomes, saved) = Create();

        await tracker.ApplyAsync(Tenant, "CA1", "completed", "human", "42");

        Assert.Equal(CampaignContactExecutionStatus.Read, saved.Contact!.Status);
        Assert.Equal(CampaignCallOutcomeStatus.Completed, saved.Outcome!.Status);
        Assert.Contains("42 s", saved.Outcome.Summary);
        Assert.NotNull(saved.Outcome.EndedAt);
        outcomes.Verify(x => x.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CampaignOutcomeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ANumberThatDoesNotAnswer_IsAFailedContact_MarkedUnreachable()
    {
        var (tracker, _, outcomes, saved) = Create();

        await tracker.ApplyAsync(Tenant, "CA1", "no-answer", null, "0");

        Assert.Equal(CampaignContactExecutionStatus.Failed, saved.Contact!.Status);
        Assert.Equal("No contestó.", saved.Contact.SkipReason);
        Assert.Equal(CampaignCallOutcomeStatus.NoAnswer, saved.Outcome!.Status);
        outcomes.Verify(x => x.RecordAsync(Tenant, "contact-1",
            It.Is<CampaignOutcomeRequest>(r => r.Outcome == CampaignOutcomes.Unreachable && r.Source == CampaignOutcomes.Sources.System),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AKnownOutcome_IsNotReplacedByUnreachable()
    {
        var (tracker, _, outcomes, _) = Create(contact => contact with { Outcome = CampaignOutcomes.PromiseToPay });

        await tracker.ApplyAsync(Tenant, "CA1", "busy", null, null);

        outcomes.Verify(x => x.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CampaignOutcomeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ALateInProgress_DoesNotUndoAFinalStatus()
    {
        var (tracker, store, _, saved) = Create(contact => contact with { Status = CampaignContactExecutionStatus.Read });

        await tracker.ApplyAsync(Tenant, "CA1", "in-progress", "human", null);

        Assert.Null(saved.Contact);
    }

    [Fact]
    public async Task ACallNoCampaignPlaced_IsIgnored()
    {
        var store = new Mock<ICampaignStore>(MockBehavior.Strict);
        store.Setup(x => x.FindContactByCallIdAsync(Tenant, "CA-other", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CampaignContactExecutionContract?)null);
        var tracker = new CampaignCallTracker(store.Object, NullLogger<CampaignCallTracker>.Instance);

        await tracker.ApplyAsync(Tenant, "CA-other", "completed", "human", "10");

        store.VerifyAll();
    }

    private sealed class Saved
    {
        public CampaignContactExecutionContract? Contact { get; set; }
        public CampaignCallOutcomeContract? Outcome { get; set; }
    }

    private static (CampaignCallTracker, Mock<ICampaignStore>, Mock<ICampaignOutcomeService>, Saved) Create(
        Func<CampaignContactExecutionContract, CampaignContactExecutionContract>? adjust = null)
    {
        var contact = new CampaignContactExecutionContract
        {
            Id = "contact-1", TenantId = Tenant, CampaignId = "camp-1", CallId = "CA1", CallOutcomeId = "out-1",
            Status = CampaignContactExecutionStatus.Sent
        };
        contact = adjust?.Invoke(contact) ?? contact;
        var saved = new Saved();
        var store = new Mock<ICampaignStore>();
        store.Setup(x => x.FindContactByCallIdAsync(Tenant, "CA1", It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        store.Setup(x => x.GetCallOutcomeAsync(Tenant, "out-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CampaignCallOutcomeContract { Id = "out-1", TenantId = Tenant, Status = CampaignCallOutcomeStatus.InProgress, Summary = "Llamada CA1." });
        store.Setup(x => x.UpdateContactExecutionAsync(It.IsAny<CampaignContactExecutionContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CampaignContactExecutionContract c, CancellationToken _) => saved.Contact = c);
        store.Setup(x => x.UpdateCallOutcomeAsync(It.IsAny<CampaignCallOutcomeContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CampaignCallOutcomeContract o, CancellationToken _) => saved.Outcome = o);
        var outcomes = new Mock<ICampaignOutcomeService>();
        return (new CampaignCallTracker(store.Object, NullLogger<CampaignCallTracker>.Instance, outcomes.Object), store, outcomes, saved);
    }
}
