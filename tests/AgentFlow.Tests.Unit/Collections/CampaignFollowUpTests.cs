using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Collections;
using AgentFlow.Api.Connect;
using Moq;

namespace AgentFlow.Tests.Unit.Collections;

public sealed class CampaignFollowUpTests
{
    private const string Tenant = "tenant-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 16, 0, 0, TimeSpan.Zero); // 10:00 in Mexico City

    private static readonly CampaignFollowUpPolicy Policy = new()
    {
        MaxFollowUps = 2,
        Rules =
        [
            new() { Id = "sin-respuesta", Trigger = "no_response", AfterHours = 48, Message = "Hola {{primer_nombre}}, debes {{total_vencido}}" },
            new() { Id = "recordatorio", Trigger = "promise_due", BeforeHours = 24, Message = "Recuerda pagar {{monto_promesa}} el {{fecha_promesa}}" },
            new() { Id = "incumplida", Trigger = "promise_broken", AfterHours = 2, Message = "No vimos tu pago" },
            new() { Id = "asesor", Trigger = "outcome:needs_human", Action = "handoff" }
        ]
    };

    // ── planner ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NoResponse_IsDueOnlyAfterTheWait_AndOnlyOnce()
    {
        var fresh = Contact(sentHoursAgo: 10);
        var old = Contact(sentHoursAgo: 50);

        Assert.Empty(CampaignFollowUpPlanner.Plan(fresh, Policy, Now));
        Assert.Equal("sin-respuesta", Assert.Single(CampaignFollowUpPlanner.Plan(old, Policy, Now)).Id);

        var alreadyDone = old with { FollowUps = [new CampaignFollowUpRecord { RuleId = "sin-respuesta", Action = "message", Status = "sent" }] };
        Assert.Empty(CampaignFollowUpPlanner.Plan(alreadyDone, Policy, Now));
    }

    [Fact]
    public void ACustomerWhoReplied_IsNotChasedForNoResponse()
        => Assert.Empty(CampaignFollowUpPlanner.Plan(Contact(sentHoursAgo: 50) with { RepliedAt = Now.AddHours(-49), Outcome = CampaignOutcomes.Replied }, Policy, Now));

    [Theory]
    [InlineData(CampaignOutcomes.OptOut)]
    [InlineData(CampaignOutcomes.WrongNumber)]
    [InlineData(CampaignOutcomes.AlreadyPaid)]
    public void TerminalOutcomes_StopEveryFollowUp(string outcome)
        => Assert.Empty(CampaignFollowUpPlanner.Plan(Contact(sentHoursAgo: 100) with { Outcome = outcome }, Policy, Now));

    [Fact]
    public void PromiseReminder_FiresTheDayBefore_BrokenPromiseAfterResolution()
    {
        var contact = Contact(sentHoursAgo: 30) with { Outcome = CampaignOutcomes.PromiseToPay, RepliedAt = Now.AddHours(-29) };
        var pending = new CollectionPromiseDocument { Status = CollectionPromiseStatus.Pending, PromisedDate = Now.AddHours(20) };
        var later = new CollectionPromiseDocument { Status = CollectionPromiseStatus.Pending, PromisedDate = Now.AddDays(3) };
        var broken = new CollectionPromiseDocument { Status = CollectionPromiseStatus.Broken, PromisedDate = Now.AddDays(-1), ResolvedAt = Now.AddHours(-3) };

        Assert.Equal("recordatorio", Assert.Single(CampaignFollowUpPlanner.Plan(contact, Policy, Now, pending)).Id);
        Assert.Empty(CampaignFollowUpPlanner.Plan(contact, Policy, Now, later));
        Assert.Equal("incumplida", Assert.Single(CampaignFollowUpPlanner.Plan(contact, Policy, Now, broken)).Id);
    }

    [Fact]
    public void Handoff_RunsAlongsideAMessage_ButNeverTwoMessagesAtOnce()
    {
        var policy = Policy with
        {
            Rules = [.. Policy.Rules, new() { Id = "otro", Trigger = "no_response", AfterHours = 1, Message = "x" }]
        };
        var contact = Contact(sentHoursAgo: 50);

        var due = CampaignFollowUpPlanner.Plan(contact, policy, Now);

        Assert.Single(due, r => r.Action == "message");
        Assert.Equal("sin-respuesta", due[0].Id);
    }

    [Fact]
    public void MaxFollowUps_CapsMessages()
    {
        var contact = Contact(sentHoursAgo: 50) with
        {
            FollowUps =
            [
                new CampaignFollowUpRecord { RuleId = "a", Action = "message", Status = "sent" },
                new CampaignFollowUpRecord { RuleId = "b", Action = "message", Status = "sent" }
            ]
        };
        Assert.Empty(CampaignFollowUpPlanner.Plan(contact, Policy, Now));
    }

    // ── contact window and schedules ────────────────────────────────────────────

    [Fact]
    public void ContactWindow_UsesTheCampaignTimezone()
    {
        var policy = CampaignContactPolicy.From(new CampaignContract
        {
            Timezone = "America/Mexico_City",
            ExecutionWindowJson = """{"startHour":8,"endHour":20,"days":[1,2,3,4,5,6]}""",
            ThrottleJson = """{"maxPerRecipientPerDay":2}"""
        });

        Assert.Equal(2, policy.MaxPerRecipientPerDay);
        Assert.True(policy.IsOpen(Now));                              // Friday 10:00 local
        Assert.False(policy.IsOpen(new DateTimeOffset(2026, 11, 28, 3, 0, 0, TimeSpan.Zero)));   // Friday 21:00 local
        Assert.False(policy.IsOpen(new DateTimeOffset(2026, 11, 29, 18, 0, 0, TimeSpan.Zero)));  // Sunday
        // Friday 21:00 → next opening is Saturday 08:00 local = 14:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 14, 0, 0, TimeSpan.Zero),
            policy.NextOpen(new DateTimeOffset(2026, 11, 28, 3, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void DailyAndWeeklySchedules_AreWallClockInTheCampaignTimezone()
    {
        // 10:00 UTC = 04:00 in Mexico City; "09:00" daily → today 09:00 local = 15:00 UTC.
        var at = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 15, 0, 0, TimeSpan.Zero), CampaignExecutionService.ComputeDaily("09:00", at, "America/Mexico_City"));
        // Weekly keeps the minutes ("Monday:09:30" used to lose them).
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 15, 30, 0, TimeSpan.Zero), CampaignExecutionService.ComputeWeekly("Monday:09:30", at, "America/Mexico_City"));
    }

    // ── follow-up service ───────────────────────────────────────────────────────

    [Fact]
    public async Task Service_SendsWithTodaysDebt_FlagsHandoffs_AndRecordsEachRuleOnce()
    {
        var silent = Contact(sentHoursAgo: 50, id: "c1", recipient: "+525511111111", party: "p1");
        var wantsHuman = Contact(sentHoursAgo: 5, id: "c2", recipient: "+525522222222", party: "p2") with
        {
            Outcome = CampaignOutcomes.NeedsHuman, OutcomeAt = Now.AddHours(-1), RepliedAt = Now.AddHours(-1)
        };
        var (service, store, outbound, collections) = CreateService([silent, wantsHuman]);
        collections.Loans["L1"] = new CollectionLoanDocument { Id = "L1", TenantId = Tenant, LoanExternalId = "L1", PartyId = "p1", OverdueBalance = 750m, TotalBalance = 3000m, DaysPastDue = 40, Status = CollectionLoanStatus.Active };

        var summary = await service.RunForCampaignAsync(Tenant, "camp-1", Now);

        Assert.Equal((1, 1, 0), (summary.Sent, summary.HandedOff, summary.Failed));
        outbound.Verify(o => o.SendAsync(Tenant, "whatsapp", "+525511111111", "Hola Ana, debes 750.00", It.IsAny<string>(), "camp-1",
            It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(store.Contacts["c2"].NeedsAttention);
        Assert.Equal("Pidio hablar con un asesor", store.Contacts["c2"].AttentionReason);
        Assert.Equal("sent", Assert.Single(store.Contacts["c1"].FollowUps!).Status);

        var again = await service.RunForCampaignAsync(Tenant, "camp-1", Now.AddHours(1));
        Assert.Equal(0, again.Sent + again.HandedOff);
    }

    [Fact]
    public async Task Service_RespectsWindow_DailyCap_AndSettledDebt()
    {
        var contact = Contact(sentHoursAgo: 50, id: "c1", recipient: "+525511111111", party: "p1");
        var (service, store, outbound, collections) = CreateService([contact], window: """{"startHour":8,"endHour":20}""", throttle: """{"maxPerRecipientPerDay":1}""");

        var night = await service.RunForCampaignAsync(Tenant, "camp-1", new DateTimeOffset(2026, 11, 28, 4, 0, 0, TimeSpan.Zero));
        Assert.Equal(1, night.Deferred);

        store.Contacts["other"] = Contact(sentHoursAgo: 1, id: "other", recipient: "+525511111111") with { CampaignId = "camp-2" };
        var capped = await service.RunForCampaignAsync(Tenant, "camp-1", Now);
        Assert.Equal(1, capped.Limited);

        store.Contacts.Remove("other");
        collections.Loans["L1"] = new CollectionLoanDocument { Id = "L1", TenantId = Tenant, PartyId = "p1", OverdueBalance = 0m, TotalBalance = 900m, Status = CollectionLoanStatus.Active };
        var paid = await service.RunForCampaignAsync(Tenant, "camp-1", Now);
        Assert.Equal(1, paid.Settled);
        outbound.VerifyNoOtherCalls();
    }

    // ── payments ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Payment_LowersOverdue_ClosesTheLoanStage_AndKeepsThePromise()
    {
        var store = new CollectionsImportTests.InMemoryCollectionsStore();
        store.Loans["PR-1"] = new CollectionLoanDocument
        {
            Id = "loan-1", TenantId = Tenant, LoanExternalId = "PR-1", PartyId = "p1", TotalBalance = 5000m,
            OverduePrincipal = 400m, OverdueInterest = 80m, OverduePenalties = 20m, OverdueBalance = 500m,
            DaysPastDue = 25, Stage = DelinquencyStage.Dpd1To30, Status = CollectionLoanStatus.Active
        };
        var promises = new CollectionPromiseService(store);
        await promises.CreateAsync(Tenant, "p1", 500m, Now.AddDays(2), "reply", "t");
        var service = new CollectionPaymentService(store, promises);

        var result = await service.RegisterAsync(Tenant, [new CollectionPaymentInput { LoanId = "PR-1", Amount = 500m, Reference = "REF-1" }], "api", "t");
        var duplicate = await service.RegisterAsync(Tenant, [new CollectionPaymentInput { LoanId = "PR-1", Amount = 500m, Reference = "REF-1" }], "api", "t");

        var loan = store.Loans["PR-1"];
        Assert.Equal((1, 1), (result.Applied, result.PromisesResolved));
        Assert.Equal((0m, 4500m, 0, DelinquencyStage.Current), (loan.OverdueBalance, loan.TotalBalance, loan.DaysPastDue, loan.Stage));
        Assert.Equal(CollectionPromiseStatus.Kept, Assert.Single(store.Promises).Status);
        Assert.Equal((0, 1), (duplicate.Applied, duplicate.Duplicates));
    }

    [Fact]
    public void Payment_CoversPenaltiesFirst_AndTreatsAnUnexplainedOverdueAsPrincipal()
    {
        var loan = new CollectionLoanDocument { TotalBalance = 1000m, OverdueBalance = 300m, OverduePenalties = 50m };
        CollectionPaymentService.Apply(loan, 100m, Now);
        Assert.Equal((0m, 200m, 200m, 900m), (loan.OverduePenalties, loan.OverduePrincipal, loan.OverdueBalance, loan.TotalBalance));
    }

    [Fact]
    public void Classifier_DoubtIsNotAPromise()
    {
        Assert.Null(CampaignReplyClassifier.Classify("no se si pueda pagar el viernes", Now).Outcome);
        Assert.Null(CampaignReplyClassifier.Classify("tal vez te pago el lunes", Now).Outcome);
        Assert.Equal(CampaignOutcomes.PromiseToPay, CampaignReplyClassifier.Classify("si, te pago el lunes", Now).Outcome);
    }

    [Fact]
    public void StageStrategy_CreatesFourDraftCampaignsWithFollowUps()
    {
        var campaigns = CollectionsStrategyTemplates.Build(Tenant, "America/Managua", "whatsapp", "u", Now);

        Assert.Equal(4, campaigns.Count);
        Assert.All(campaigns, c =>
        {
            Assert.False(c.Enabled);
            Assert.Equal(CampaignStatus.Draft, c.Status);
            Assert.NotNull(CampaignFollowUpPolicy.Parse(c.FollowupPolicyJson));
            Assert.Contains("\"source\":\"collections\"", c.AudienceFilterJson);
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static CampaignContactExecutionContract Contact(double sentHoursAgo, string id = "c1", string recipient = "+525511111111", string? party = null) => new()
    {
        Id = id,
        TenantId = Tenant,
        CampaignId = "camp-1",
        RunId = "run-1",
        PartyId = party,
        Channel = "whatsapp",
        Recipient = recipient,
        RecipientKey = RecipientKeys.From(recipient),
        Status = CampaignContactExecutionStatus.Sent,
        Variables = new Dictionary<string, string> { ["primer_nombre"] = "Ana", ["total_vencido"] = "1,200.00" },
        CreatedAt = Now.AddHours(-sentHoursAgo),
        UpdatedAt = Now.AddHours(-sentHoursAgo)
    };

    private static (CampaignFollowUpService, FakeStore, Mock<IOutboundMessageSender>, CollectionsImportTests.InMemoryCollectionsStore) CreateService(
        IEnumerable<CampaignContactExecutionContract> contacts, string? window = null, string? throttle = null)
    {
        var campaign = new CampaignContract
        {
            Id = "camp-1", TenantId = Tenant, Channel = "whatsapp", Timezone = "America/Mexico_City", Enabled = true,
            FollowupPolicyJson = System.Text.Json.JsonSerializer.Serialize(Policy, CampaignFollowUpPolicy.Json),
            ExecutionWindowJson = window, ThrottleJson = throttle
        };
        var store = new FakeStore(campaign, contacts);
        var outbound = new Mock<IOutboundMessageSender>();
        outbound.Setup(o => o.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutboundSendResult(true, "ch", "msg", "inbox", null));
        var collections = new CollectionsImportTests.InMemoryCollectionsStore();
        return (new CampaignFollowUpService(store.Object, outbound.Object, collections), store, outbound, collections);
    }

    private sealed class FakeStore
    {
        public Dictionary<string, CampaignContactExecutionContract> Contacts { get; }
        public ICampaignStore Object { get; }

        public FakeStore(CampaignContract campaign, IEnumerable<CampaignContactExecutionContract> contacts)
        {
            Contacts = contacts.ToDictionary(c => c.Id);
            var mock = new Mock<ICampaignStore>();
            mock.Setup(s => s.GetCampaignAsync(Tenant, campaign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(campaign);
            mock.Setup(s => s.GetOptedOutRecipientKeysAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<string>());
            mock.Setup(s => s.GetContactExecutionsByCampaignAsync(Tenant, campaign.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Contacts.Values.Where(c => c.CampaignId == campaign.Id).ToList());
            mock.Setup(s => s.GetContactExecutionsByRecipientKeyAsync(Tenant, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string key, int _, CancellationToken _) => Contacts.Values.Where(c => c.RecipientKey == key).ToList());
            mock.Setup(s => s.UpdateContactExecutionAsync(It.IsAny<CampaignContactExecutionContract>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CampaignContactExecutionContract c, CancellationToken _) => { Contacts[c.Id] = c; return c; });
            Object = mock.Object;
        }
    }
}
