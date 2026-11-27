using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Collections;
using AgentFlow.Api.Commerce;
using AgentFlow.Api.Connect;
using AgentFlow.Domain.Repositories;
using Moq;

namespace AgentFlow.Tests.Unit.Collections;

public sealed class CampaignOutcomesTests
{
    private const string Tenant = "tenant-1";
    // Friday 27 Nov 2026, local time in Mexico City.
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 10, 0, 0, TimeSpan.FromHours(-6));

    // ── reply classifier ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Sí, claro", CampaignOutcomes.Interested)]
    [InlineData("no gracias", CampaignOutcomes.NotInterested)]
    [InlineData("Ya pagué ayer en el OXXO", CampaignOutcomes.AlreadyPaid)]
    [InlineData("Número equivocado, no soy esa persona", CampaignOutcomes.WrongNumber)]
    [InlineData("Ya no me escriban por favor", CampaignOutcomes.OptOut)]
    [InlineData("No puedo pagar, me quedé sin trabajo", CampaignOutcomes.CannotPay)]
    [InlineData("Yo no debo nada, nunca pedí un préstamo", CampaignOutcomes.Dispute)]
    [InlineData("Quiero hablar con un asesor", CampaignOutcomes.NeedsHuman)]
    [InlineData("Ahorita no puedo, márcame más tarde", CampaignOutcomes.CallbackRequested)]
    [InlineData("Te pago el viernes", CampaignOutcomes.PromiseToPay)]
    [InlineData("hola buenas tardes", null)]
    public void Classifier_MapsSpanishRepliesToOutcomes(string reply, string? expected)
        => Assert.Equal(expected, CampaignReplyClassifier.Classify(reply, Now).Outcome);

    [Fact]
    public void Classifier_ExtractsPromiseAmountAndDate()
    {
        var result = CampaignReplyClassifier.Classify("Le pago $1,500 el lunes sin falta", Now);

        Assert.Equal(CampaignOutcomes.PromiseToPay, result.Outcome);
        Assert.Equal(1500m, result.PromiseAmount);
        Assert.Equal(new DateTime(2026, 11, 30), result.PromiseDate!.Value.Date); // next Monday
    }

    [Theory]
    [InlineData("pago mañana", 2026, 11, 28)]
    [InlineData("deposito pasado mañana", 2026, 11, 29)]
    [InlineData("abono el 15", 2026, 12, 15)]       // the 15th already passed this month
    [InlineData("pago el 3/12", 2026, 12, 3)]
    [InlineData("pago en la quincena", 2026, 11, 30)] // after the 15th → end of month
    public void Classifier_ResolvesRelativeDates(string reply, int y, int m, int d)
        => Assert.Equal(new DateTime(y, m, d), CampaignReplyClassifier.Classify(reply, Now).PromiseDate!.Value.Date);

    [Fact]
    public void Classifier_BareDateAfterCollectionsMessage_IsAPromise()
        => Assert.Equal(CampaignOutcomes.PromiseToPay, CampaignReplyClassifier.Classify("el viernes", Now).Outcome);

    // ── renderer and recipient keys ─────────────────────────────────────────────

    [Fact]
    public void Renderer_FillsVariables_AndReportsMissingOnes()
    {
        var (text, missing) = CampaignMessageRenderer.Render(
            "Hola {{ primer_nombre }}, tu saldo vencido es ${{total_vencido}} ({{variables.dias_atraso}} dias). {{folio}}",
            new Dictionary<string, string> { ["primer_nombre"] = "Ana", ["total_vencido"] = "1,200.00", ["dias_atraso"] = "18" });

        Assert.Equal("Hola Ana, tu saldo vencido es $1,200.00 (18 dias). {{folio}}", text);
        Assert.Equal(["folio"], missing);
    }

    [Theory]
    [InlineData("5215512345678@c.us", "+52 55 1234 5678")]
    [InlineData("whatsapp:+5215512345678", "55-1234-5678")]
    public void RecipientKeys_MatchInboundSenderWithStoredPhone(string inbound, string stored)
        => Assert.Equal(RecipientKeys.From(stored), RecipientKeys.From(inbound));

    [Fact]
    public void RecipientKeys_KeepNonPhoneIdentifiersDistinct()
        => Assert.NotEqual(RecipientKeys.From("e2e123a"), RecipientKeys.From("e2e123b"));

    // ── reply attribution ───────────────────────────────────────────────────────

    [Fact]
    public async Task Reply_IsAttributedToTheContact_AndAPromiseCreatesACollectionsPromise()
    {
        var (service, campaigns, collections) = CreateOutcomeService(CampaignType.Collections);
        collections.Loans["L1"] = Loan("L1", "party-1", overdue: 1200m);

        var result = await service.AttributeReplyAsync(Tenant, "5215512345678@c.us", "Te pago 1200 el lunes", Now);

        Assert.NotNull(result);
        Assert.Equal(CampaignOutcomes.PromiseToPay, result!.Outcome);
        Assert.Equal(CampaignOutcomes.Sources.Reply, result.OutcomeSource);
        Assert.Equal(1, result.ReplyCount);
        Assert.Equal(1200m, result.PromiseAmount);
        var promise = Assert.Single(collections.Promises);
        Assert.Equal("party-1", promise.PartyId);
        Assert.Equal(1200m, promise.OverdueAtCreation);
        Assert.Equal("campaign-1", promise.CampaignId);
        Assert.Equal(1, campaigns.Updates);
    }

    [Fact]
    public async Task Reply_DoesNotOverwriteAnOutcomeSetByAnOperator()
    {
        var (service, campaigns, _) = CreateOutcomeService(CampaignType.Collections,
            contact => contact with { Outcome = CampaignOutcomes.CallbackRequested, OutcomeSource = CampaignOutcomes.Sources.Human });

        var result = await service.AttributeReplyAsync(Tenant, "5215512345678@c.us", "no gracias", Now);

        Assert.Equal(CampaignOutcomes.CallbackRequested, result!.Outcome);
        Assert.Equal("no gracias", result.LastReply);
    }

    [Fact]
    public async Task Reply_CollectionsOnlyOutcome_InASalesCampaign_FallsBackToReplied()
    {
        var (service, _, collections) = CreateOutcomeService(CampaignType.Sales);

        var result = await service.AttributeReplyAsync(Tenant, "5215512345678@c.us", "ya pague", Now);

        Assert.Equal(CampaignOutcomes.Replied, result!.Outcome);
        Assert.Empty(collections.Promises);
    }

    [Fact]
    public async Task Reply_FromANumberNoCampaignContacted_IsIgnored()
    {
        var (service, _, _) = CreateOutcomeService(CampaignType.Collections);
        Assert.Null(await service.AttributeReplyAsync(Tenant, "5219999999999@c.us", "si", Now));
    }

    [Fact]
    public void EffectiveOutcome_DerivesUnreachableAndNoResponse()
    {
        var now = DateTimeOffset.UtcNow;
        var failed = new CampaignContactExecutionContract { Status = CampaignContactExecutionStatus.Failed, CreatedAt = now };
        var silent = new CampaignContactExecutionContract { Status = CampaignContactExecutionStatus.Sent, CreatedAt = now.AddDays(-2) };
        var fresh = new CampaignContactExecutionContract { Status = CampaignContactExecutionStatus.Sent, CreatedAt = now.AddHours(-1) };

        Assert.Equal(CampaignOutcomes.Unreachable, CampaignOutcomeService.EffectiveOutcome(failed, now));
        Assert.Equal(CampaignOutcomes.NoResponse, CampaignOutcomeService.EffectiveOutcome(silent, now));
        Assert.Null(CampaignOutcomeService.EffectiveOutcome(fresh, now));
    }

    // ── promise resolution ──────────────────────────────────────────────────────

    [Fact]
    public async Task Promises_AreKeptWhenOverdueDrops_AndBrokenWhenTheDatePasses()
    {
        var store = new CollectionsImportTests.InMemoryCollectionsStore();
        store.Loans["L1"] = Loan("L1", "p1", overdue: 1000m);
        store.Loans["L2"] = Loan("L2", "p2", overdue: 500m);
        var service = new CollectionPromiseService(store);
        await service.CreateAsync(Tenant, "p1", 1000m, Now.AddDays(3), "reply", "t");
        await service.CreateAsync(Tenant, "p2", null, Now.AddDays(1), "human", "t");

        store.Loans["L1"].OverdueBalance = 50m; // paid 950 of 1000 → within tolerance
        var changed = await service.ResolveAsync(Tenant, Now.AddDays(2));

        Assert.Equal(2, changed);
        Assert.Equal(CollectionPromiseStatus.Kept, store.Promises.Single(p => p.PartyId == "p1").Status);
        Assert.Equal(CollectionPromiseStatus.Broken, store.Promises.Single(p => p.PartyId == "p2").Status);
    }

    // ── audience from the collections portfolio ─────────────────────────────────

    [Fact]
    public async Task Audience_FromCollections_OneContactPerCustomer_WithLoanVariables()
    {
        var store = new CollectionsImportTests.InMemoryCollectionsStore();
        store.Loans["A1"] = Loan("A1", "ana", overdue: 300m, dpd: 20, phone: "+5215511111111", name: "Ana Lopez");
        store.Loans["A2"] = Loan("A2", "ana", overdue: 200m, dpd: 45, phone: "+5215511111111", name: "Ana Lopez");
        store.Loans["B1"] = Loan("B1", "beto", overdue: 900m, dpd: 95, phone: "+5215522222222", name: "Beto");
        store.Loans["C1"] = Loan("C1", "caro", overdue: 0m, dpd: 0, phone: "+5215533333333", name: "Caro");

        var campaigns = new Mock<ICampaignStore>();
        var audience = new CampaignAudienceService(
            Mock.Of<ICommerceStore>(), Mock.Of<IConnectStore>(), campaigns.Object, Mock.Of<IChannelSessionRepository>(), store);

        var result = await audience.ResolveAsync(Tenant, """{"source":"collections","stages":["Dpd31To60"]}""", null);

        var ana = Assert.Single(result.Contacts);
        Assert.Equal("ana", ana.PartyId);
        Assert.Equal("500.00", ana.Variables["total_vencido"]);
        Assert.Equal("45", ana.Variables["dias_atraso"]);
        Assert.Equal("2", ana.Variables["num_prestamos"]);
        Assert.Equal("Ana", ana.Variables["primer_nombre"]);

        var all = await audience.ResolveAsync(Tenant, """{"source":"collections","minDaysPastDue":1}""", null);
        Assert.Equal(["beto", "ana"], all.Contacts.Select(c => c.PartyId)); // largest overdue first, current customer excluded
    }

    private static CollectionLoanDocument Loan(string id, string party, decimal overdue, int dpd = 30, string phone = "+5215512345678", string name = "Cliente") => new()
    {
        Id = id,
        TenantId = Tenant,
        LoanExternalId = id,
        PartyId = party,
        CustomerExternalId = party,
        CustomerName = name,
        CustomerPhone = phone,
        TotalBalance = overdue * 3,
        OverdueBalance = overdue,
        DaysPastDue = dpd,
        Stage = DelinquencyStages.FromDaysPastDue(dpd),
        Status = CollectionLoanStatus.Active
    };

    private static (CampaignOutcomeService Service, FakeCampaignStore Campaigns, CollectionsImportTests.InMemoryCollectionsStore Collections) CreateOutcomeService(
        CampaignType type,
        Func<CampaignContactExecutionContract, CampaignContactExecutionContract>? customize = null)
    {
        var contact = new CampaignContactExecutionContract
        {
            Id = "contact-1",
            TenantId = Tenant,
            CampaignId = "campaign-1",
            RunId = "run-1",
            PartyId = "party-1",
            Channel = "whatsapp",
            Recipient = "+52 55 1234 5678",
            RecipientKey = RecipientKeys.From("+52 55 1234 5678"),
            Status = CampaignContactExecutionStatus.Sent,
            CreatedAt = Now.AddHours(-2)
        };
        var campaigns = new FakeCampaignStore(customize is null ? contact : customize(contact),
            new CampaignContract { Id = "campaign-1", TenantId = Tenant, CampaignType = type, Timezone = "America/Mexico_City" });
        var collections = new CollectionsImportTests.InMemoryCollectionsStore();
        return (new CampaignOutcomeService(campaigns.Object, new CollectionPromiseService(collections)), campaigns, collections);
    }

    private sealed class FakeCampaignStore
    {
        private CampaignContactExecutionContract _contact;
        public int Updates { get; private set; }
        public ICampaignStore Object { get; }

        public FakeCampaignStore(CampaignContactExecutionContract contact, CampaignContract campaign)
        {
            _contact = contact;
            var mock = new Mock<ICampaignStore>();
            mock.Setup(s => s.FindLatestContactByRecipientKeyAsync(Tenant, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string key, DateTimeOffset _, CancellationToken _) => key == _contact.RecipientKey ? _contact : null);
            mock.Setup(s => s.GetContactExecutionAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _contact);
            mock.Setup(s => s.UpdateContactExecutionAsync(It.IsAny<CampaignContactExecutionContract>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CampaignContactExecutionContract c, CancellationToken _) => { _contact = c; Updates++; return c; });
            mock.Setup(s => s.GetCampaignAsync(Tenant, campaign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(campaign);
            Object = mock.Object;
        }
    }
}
