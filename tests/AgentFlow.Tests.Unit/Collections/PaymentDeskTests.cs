using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Collections;
using Moq;

namespace AgentFlow.Tests.Unit.Collections;

public sealed class PaymentDeskTests
{
    private const string Tenant = "tenant-1";
    private const string Phone = "50581143874@c.us";

    [Fact]
    public void Instructions_WithoutMethods_TellTheAssistantNotToInventAccounts()
    {
        var result = CollectionPaymentDesk.BuildInstructions(null, [Loan("L1", 500, 2000)]);

        Assert.False(result.Configured);
        Assert.Contains("no inventes", result.Message);
        Assert.Empty(result.Methods);
    }

    [Fact]
    public void Instructions_GiveTheCustomerReferences_AndOnlyMethodsInTheirCurrency()
    {
        var settings = new CollectionPaymentSettingsDocument
        {
            TenantId = Tenant,
            Methods =
            [
                new() { Name = "BAC", AccountNumber = "360-123-456", Currency = "NIO" },
                new() { Name = "BAC dolares", AccountNumber = "360-999", Currency = "USD" },
                new() { Name = "Agentes Banpro", Type = "cash" },
                new() { Name = "Cuenta vieja", AccountNumber = "1", Enabled = false },
            ],
            Notes = "Envia el comprobante por este chat."
        };

        var result = CollectionPaymentDesk.BuildInstructions(settings,
        [
            Loan("PR-1", 200, 1000),
            Loan("PR-2", 900, 3000),
            Loan("PR-OLD", 0, 0, CollectionLoanStatus.PaidOff),
        ]);

        Assert.True(result.Configured);
        Assert.Equal("numero de prestamo", result.ReferenceLabel);
        Assert.Equal(["PR-2", "PR-1"], result.Loans.Select(l => l.Reference)); // most overdue first; paid-off left out
        Assert.Equal(["BAC", "Agentes Banpro"], result.Methods.Select(m => m.Name));
        Assert.Equal("Envia el comprobante por este chat.", result.Notes);
    }

    [Fact]
    public void Instructions_ByCustomerNumber_GiveOneReference()
    {
        var settings = new CollectionPaymentSettingsDocument { TenantId = Tenant, ReferenceKind = "customer", Methods = [new() { Name = "BAC" }] };
        var result = CollectionPaymentDesk.BuildInstructions(settings, [Loan("PR-1", 200, 1000), Loan("PR-2", 900, 3000)]);

        var reference = Assert.Single(result.Loans);
        Assert.Equal("CLI-7", reference.Reference);
        Assert.Equal(1100m, reference.OverdueBalance);
    }

    [Theory]
    [InlineData("ayer", "2026-11-26")]
    [InlineData("antier", "2026-11-25")]
    [InlineData("hoy en la manana", "2026-11-27")]
    [InlineData("el lunes", "2026-11-23")]        // today is Friday 27: last Monday
    [InlineData("el 28", "2026-10-28")]           // the 28th has not come yet this month
    [InlineData("el 20", "2026-11-20")]
    [InlineData("15/11", "2026-11-15")]
    [InlineData("hace tiempo", null)]
    public void PaymentDate_IsNeverInTheFuture(string text, string? expected)
    {
        var now = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
        var date = CollectionPaymentDesk.ResolvePastDate(text, now);
        Assert.Equal(expected, date?.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void ChatText_CarriesAccountsAndReferencesExactly()
    {
        // The model once turned 0000-PRUEBA-0001 into 2000-PRUEBA-0001: accounts are never retyped by it.
        var settings = new CollectionPaymentSettingsDocument
        {
            TenantId = Tenant,
            Methods =
            [
                new() { Name = "Banco PRUEBA", AccountNumber = "0000-PRUEBA-0001", AccountType = "Ahorro", Currency = "NIO", AccountHolder = "Financiera Demo", Instructions = "Pon tu numero de prestamo en el concepto" },
                new() { Name = "Agentes PRUEBA", Type = "cash" },
            ],
            Notes = "Envia el comprobante por este chat."
        };
        var text = CollectionPaymentDesk.RenderForChat(CollectionPaymentDesk.BuildInstructions(settings, [Loan("PRUEBA-WA-L1", 1350, 8000)]));

        Assert.Contains("*Banco PRUEBA* (transferencia o deposito)", text);
        Assert.Contains("Cuenta: *0000-PRUEBA-0001* · Ahorro · NIO · Titular: Financiera Demo", text);
        Assert.Contains("*Agentes PRUEBA* (efectivo)", text);
        Assert.Contains("Tu referencia (numero de prestamo): *PRUEBA-WA-L1*", text);
        Assert.EndsWith("Envia el comprobante por este chat.", text);
        Assert.DoesNotContain("**", text);
    }

    [Theory]
    [InlineData("Puedes pagar asi:\n[MEDIOS_DE_PAGO]")]
    [InlineData("Puedes pagar asi:\n[medios de pago]")]
    [InlineData("Puedes pagar asi:\n[ MEDIOS_DE_PAGO ]")]
    public void Placeholder_IsReplacedHoweverTheModelWritesIt(string reply)
    {
        Assert.True(CollectionPaymentDesk.HasPlaceholder(reply));
        Assert.Equal("Puedes pagar asi:\nCuenta: *1* ($5)", CollectionPaymentDesk.ReplacePlaceholder(reply, "Cuenta: *1* ($5)"));
    }

    [Theory]
    [InlineData("Claro, Ana. Puedes pagar asi:\n[MEDIOS_DE_PAGO]", false)]
    [InlineData("Claro, Ana. Puedes pagar asi:\n1. Banco PRUEBA, cuenta 2000-PRUEBA-0001.", true)] // described in its own words
    [InlineData("Tu saldo vencido es de 1,350.", null)]                         // not about paying: untouched
    public async Task Transformer_AlwaysDeliversTheExactAccounts(string reply, bool? keepsModelText)
    {
        var (service, store, desk, _) = Create(contact: null);
        store.Loans["PRUEBA-WA-L1"] = Loan("PRUEBA-WA-L1", 1350, 8000);
        desk.Settings = new CollectionPaymentSettingsDocument
        {
            TenantId = Tenant,
            Methods = [new() { Name = "Banco PRUEBA", AccountNumber = "0000-PRUEBA-0001" }]
        };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<ICollectionPaymentDeskStore>(services, desk);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<ICollectionsAssistantService>(services, service);
        var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var transformer = new PaymentInstructionsReplyTransformer(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(provider));
        var channel = AgentFlow.Domain.Aggregates.ChannelDefinition.Create(Tenant, "wa", AgentFlow.Domain.Aggregates.ChannelType.WhatsApp);
        var incoming = AgentFlow.Domain.Aggregates.ChannelMessage.CreateIncoming(Tenant, channel.Id, "s1", Phone, "donde pago?");

        var result = await transformer.TransformAsync(incoming, channel, reply);

        if (keepsModelText is null)
        {
            Assert.Equal(reply, result);
            return;
        }
        Assert.Contains("Cuenta: *0000-PRUEBA-0001*", result);
        Assert.Contains("*PRUEBA-WA-L1*", result);
        Assert.DoesNotContain("[MEDIOS_DE_PAGO]", result);
        Assert.DoesNotContain("2000-PRUEBA", result); // the model's own (wrong) digits never reach the customer
        Assert.StartsWith("Claro, Ana. Puedes pagar asi:", result);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result, "Banco PRUEBA"));
    }

    [Fact]
    public void Placeholder_DropsWhatTheModelRepeatsFromTheBlock()
    {
        var reply = "Claro, Carlos. Puedes pagar asi:\n\n[MEDIOS_DE_PAGO]\n\nCuando pagues, envia el comprobante por este chat.";
        var block = "*Agentes PRUEBA* (efectivo)\n\nTu referencia: *PR-50010*\nCuando pagues, envia el comprobante por este chat.";

        Assert.Equal("Claro, Carlos. Puedes pagar asi:\n\n" + block, CollectionPaymentDesk.ReplacePlaceholder(reply, block));
    }

    [Fact]
    public void WhatsApp_GetsItsOwnBoldAndNoHeadings()
    {
        var text = AgentFlow.Core.Engine.ChannelReplyFormatting.ForWhatsApp("### Opciones\nBanco: **BAC**\n- __nota__");
        Assert.Equal("Opciones\nBanco: *BAC*\n- _nota_", text);
    }

    [Fact]
    public async Task CustomerContext_AlwaysCarriesHowToPay()
    {
        // With the instructions only behind a separate tool, the model kept saying it had no payment data.
        var (service, store, desk, _) = Create(contact: null);
        store.Loans["PR-1"] = Loan("PR-1", 500, 2000);
        desk.Settings = new CollectionPaymentSettingsDocument { TenantId = Tenant, Methods = [new() { Name = "BAC", AccountNumber = "360-1" }] };

        var context = await service.GetContextAsync(Tenant, Phone);

        Assert.NotNull(context.HowToPay);
        Assert.True(context.HowToPay!.Configured);
        Assert.Equal("PR-1", Assert.Single(context.HowToPay.Loans).Reference);
        Assert.Null(Assert.Single(context.HowToPay.Methods).AccountNumber); // the model never gets account digits
        Assert.Contains(CollectionPaymentDesk.Placeholder, context.HowToPay.Message);
    }

    [Fact]
    public async Task ReportedPayment_IsSavedForReview_AndTheCampaignSaysAlreadyPaid()
    {
        var contact = new CampaignContactExecutionContract { Id = "c1", TenantId = Tenant, CampaignId = "camp", PartyId = "p1", RecipientKey = "8114387400" };
        var (service, store, desk, outcomes) = Create(contact);
        store.Loans["PR-1"] = Loan("PR-1", 500, 2000);

        var result = await service.ReportPaymentAsync(Tenant, Phone, 500m, "ayer", "TRX-88", "BAC", null);

        Assert.True(result.Ok);
        var report = Assert.Single(desk.Reports);
        Assert.Equal(500m, report.Amount);
        Assert.Equal("TRX-88", report.Reference);
        Assert.Equal("PR-1", report.LoanId); // single open loan
        Assert.NotNull(report.PaidOn);
        Assert.Equal(CollectionPaymentReportStatus.Pending, report.Status);
        outcomes.Verify(o => o.RecordAsync(Tenant, "c1",
            It.Is<CampaignOutcomeRequest>(r => r.Outcome == CampaignOutcomes.AlreadyPaid && r.Notes!.Contains("TRX-88")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReportedPayment_WithoutDetails_IsStillSaved_AndSaysWhatIsMissing()
    {
        var (service, store, desk, _) = Create(contact: null);
        store.Loans["PR-1"] = Loan("PR-1", 500, 2000);

        var result = await service.ReportPaymentAsync(Tenant, Phone, null, null, null, null, null);

        Assert.True(result.Ok);
        Assert.Contains("monto", result.Message);
        Assert.Contains("referencia", result.Message);
        Assert.Single(desk.Reports);
    }

    [Fact]
    public async Task ConfirmingAReport_RegistersThePaymentOnTheLoan()
    {
        var store = new CollectionsImportTests.InMemoryCollectionsStore();
        store.Loans["PR-1"] = Loan("PR-1", 500, 2000);
        var desk = new InMemoryDesk();
        desk.Reports.Add(new CollectionPaymentReportDocument { Id = "r1", TenantId = Tenant, PartyId = "p1", Amount = 300m, Reference = "TRX-1" });
        var reviews = new CollectionPaymentReportService(desk, store, new CollectionPaymentService(store, new CollectionPromiseService(store)));

        var result = await reviews.ConfirmAsync(Tenant, "r1", null, null, null, "advisor-1");

        Assert.True(result.Ok);
        var payment = Assert.Single(store.Payments);
        Assert.Equal(300m, payment.Amount);
        Assert.Equal("reported", payment.Source);
        Assert.Equal(200m, store.Loans["PR-1"].OverdueBalance);
        Assert.Equal(CollectionPaymentReportStatus.Confirmed, desk.Reports[0].Status);
        Assert.Equal(payment.Id, desk.Reports[0].PaymentId);

        var again = await reviews.ConfirmAsync(Tenant, "r1", null, null, null, "advisor-1");
        Assert.False(again.Ok); // reviewed once
        Assert.Single(store.Payments);
    }

    // ------------------------------------------------------------------

    private static CollectionLoanDocument Loan(string id, decimal overdue, decimal total, CollectionLoanStatus status = CollectionLoanStatus.Active) => new()
    {
        Id = id,
        TenantId = Tenant,
        LoanExternalId = id,
        PartyId = "p1",
        CustomerExternalId = "CLI-7",
        CustomerName = "Ana Lopez",
        CustomerPhone = "+505 8114 3874",
        Currency = "NIO",
        OverdueBalance = overdue,
        OverduePrincipal = overdue,
        TotalBalance = total,
        Status = status
    };

    private static (CollectionsAssistantService, CollectionsImportTests.InMemoryCollectionsStore, InMemoryDesk, Mock<ICampaignOutcomeService>) Create(CampaignContactExecutionContract? contact)
    {
        var store = new CollectionsImportTests.InMemoryCollectionsStore();
        var campaigns = new Mock<ICampaignStore>();
        campaigns.Setup(c => c.FindLatestContactByRecipientKeyAsync(Tenant, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contact);
        var outcomes = new Mock<ICampaignOutcomeService>();
        var desk = new InMemoryDesk();
        var service = new CollectionsAssistantService(store, new CollectionPromiseService(store), campaigns.Object, outcomes.Object, paymentDesk: desk);
        return (service, store, desk, outcomes);
    }

    private sealed class InMemoryDesk : ICollectionPaymentDeskStore
    {
        public CollectionPaymentSettingsDocument? Settings { get; set; }
        public List<CollectionPaymentReportDocument> Reports { get; } = [];

        public Task<CollectionPaymentSettingsDocument?> GetSettingsAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(Settings);
        public Task SaveSettingsAsync(CollectionPaymentSettingsDocument settings, CancellationToken ct = default) { Settings = settings; return Task.CompletedTask; }
        public Task InsertReportAsync(CollectionPaymentReportDocument report, CancellationToken ct = default) { Reports.Add(report); return Task.CompletedTask; }
        public Task UpdateReportAsync(CollectionPaymentReportDocument report, CancellationToken ct = default) => Task.CompletedTask;
        public Task<CollectionPaymentReportDocument?> GetReportAsync(string tenantId, string reportId, CancellationToken ct = default)
            => Task.FromResult(Reports.FirstOrDefault(r => r.Id == reportId));
        public Task<IReadOnlyList<CollectionPaymentReportDocument>> GetReportsAsync(string tenantId, CollectionPaymentReportStatus? status, int limit = 200, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CollectionPaymentReportDocument>>(Reports.Where(r => status == null || r.Status == status).ToList());
    }
}
