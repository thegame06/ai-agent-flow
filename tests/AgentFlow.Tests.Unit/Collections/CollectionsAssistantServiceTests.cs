using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Collections;
using Moq;

namespace AgentFlow.Tests.Unit.Collections;

public sealed class CollectionsAssistantServiceTests
{
    private const string Tenant = "tenant-1";

    [Fact]
    public async Task Context_IsFoundByTheWhatsAppSender_AndSummarizesOpenLoans()
    {
        var (service, store, _, _) = Create(contact: null);
        store.Loans["L1"] = Loan("L1", overdue: 1200m, total: 5000m, dpd: 25);
        store.Loans["L2"] = Loan("L2", overdue: 150m, total: 900m, dpd: 40);
        store.Loans["L3"] = Loan("L3", overdue: 0m, total: 0m, dpd: 0, status: CollectionLoanStatus.PaidOff);

        var context = await service.GetContextAsync(Tenant, "50581143874@c.us");

        Assert.True(context.Found);
        Assert.Equal("Ana", context.FirstName);
        Assert.Equal(1350m, context.OverdueBalance);
        Assert.Equal(40, context.MaxDaysPastDue);
        Assert.Equal(2, context.Loans.Count); // paid-off loan left out
        Assert.Equal("NIO", context.Currency);
    }

    [Fact]
    public async Task Context_ForAnUnknownNumber_SaysSo()
    {
        var (service, _, _, _) = Create(contact: null);
        var context = await service.GetContextAsync(Tenant, "+15550000000");
        Assert.False(context.Found);
    }

    [Fact]
    public async Task Promise_GoesThroughTheCampaignOutcome_WhenTheCustomerWasContacted()
    {
        var contact = new CampaignContactExecutionContract { Id = "c1", TenantId = Tenant, CampaignId = "camp", PartyId = "p1", Recipient = "+50581143874" };
        var (service, _, _, outcomes) = Create(contact);
        var date = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(3), TimeSpan.Zero);

        var result = await service.RegisterPromiseAsync(Tenant, "50581143874@c.us", 500m, date, null, "por WhatsApp");

        Assert.True(result.Ok);
        outcomes.Verify(o => o.RecordAsync(Tenant, "c1",
            It.Is<CampaignOutcomeRequest>(r => r.Outcome == CampaignOutcomes.PromiseToPay && r.Source == CampaignOutcomes.Sources.Agent && r.PromiseAmount == 500m && r.PromiseDate == date),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Promise_InThePast_IsRejected()
    {
        var (service, _, _, _) = Create(contact: null);
        var result = await service.RegisterPromiseAsync(Tenant, "+50581143874", 100m, DateTimeOffset.UtcNow.AddDays(-5), null, null);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Promise_WhenTextIsResolvedDeterministically_NotByTheModel()
    {
        var (service, store, _, _) = Create(contact: null);
        store.Loans["L1"] = Loan("L1", overdue: 1200m, total: 5000m, dpd: 25);

        var result = await service.RegisterPromiseAsync(Tenant, "+50581143874", 300m, promisedDate: null, whenText: "el lunes", notes: null);

        Assert.True(result.Ok);
        var promise = Assert.Single(store.Promises);
        Assert.Equal(DayOfWeek.Monday, promise.PromisedDate.DayOfWeek);
        Assert.Contains("lunes", result.Message);
    }

    [Fact]
    public async Task Promise_WithAVagueDate_IsRefused_EvenIfTheModelSuppliesADate()
    {
        var (service, store, _, _) = Create(contact: null);
        store.Loans["L1"] = Loan("L1", overdue: 1200m, total: 5000m, dpd: 25);

        var result = await service.RegisterPromiseAsync(Tenant, "+50581143874", null,
            promisedDate: DateTimeOffset.UtcNow.AddDays(7), whenText: "en diciembre", notes: null);

        Assert.False(result.Ok);
        Assert.Empty(store.Promises);
    }

    [Fact]
    public async Task NewPromise_ReplacesTheEarlierPendingOne()
    {
        var store = new CollectionsImportTests.InMemoryCollectionsStore();
        store.Loans["L1"] = Loan("L1", overdue: 1200m, total: 5000m, dpd: 25);
        var promises = new CollectionPromiseService(store);
        await promises.CreateAsync(Tenant, "p1", 500m, DateTimeOffset.UtcNow.AddDays(7), "reply", "t");
        await promises.CreateAsync(Tenant, "p1", 300m, DateTimeOffset.UtcNow.AddDays(3), "agent", "t");

        Assert.Equal(CollectionPromiseStatus.Cancelled, store.Promises[0].Status);
        Assert.Equal(CollectionPromiseStatus.Pending, store.Promises[1].Status);
    }

    [Fact]
    public async Task Handoff_FlagsTheContactForAPerson()
    {
        var contact = new CampaignContactExecutionContract { Id = "c1", TenantId = Tenant, CampaignId = "camp", Recipient = "+50581143874" };
        var (service, _, campaigns, _) = Create(contact);

        var result = await service.HandoffAsync(Tenant, "+50581143874", "Quiere un convenio a 3 meses");

        Assert.True(result.Ok);
        campaigns.Verify(c => c.UpdateContactExecutionAsync(
            It.Is<CampaignContactExecutionContract>(x => x.NeedsAttention && x.AttentionReason == "Quiere un convenio a 3 meses"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static CollectionLoanDocument Loan(string id, decimal overdue, decimal total, int dpd, CollectionLoanStatus status = CollectionLoanStatus.Active) => new()
    {
        Id = id,
        TenantId = Tenant,
        LoanExternalId = id,
        PartyId = "p1",
        CustomerName = "Ana Lopez",
        CustomerPhone = "+505 8114 3874",
        Currency = "NIO",
        OverdueBalance = overdue,
        TotalBalance = total,
        DaysPastDue = dpd,
        Status = status
    };

    private static (CollectionsAssistantService, CollectionsImportTests.InMemoryCollectionsStore, Mock<ICampaignStore>, Mock<ICampaignOutcomeService>) Create(CampaignContactExecutionContract? contact)
    {
        var store = new CollectionsImportTests.InMemoryCollectionsStore();
        var campaigns = new Mock<ICampaignStore>();
        campaigns.Setup(c => c.FindLatestContactByRecipientKeyAsync(Tenant, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contact);
        campaigns.Setup(c => c.UpdateContactExecutionAsync(It.IsAny<CampaignContactExecutionContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CampaignContactExecutionContract c, CancellationToken _) => c);
        var outcomes = new Mock<ICampaignOutcomeService>();
        var service = new CollectionsAssistantService(store, new CollectionPromiseService(store), campaigns.Object, outcomes.Object);
        return (service, store, campaigns, outcomes);
    }
}
