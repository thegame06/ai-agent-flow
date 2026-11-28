using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Channels;
using AgentFlow.Api.Collections;
using AgentFlow.Api.Commerce;
using AgentFlow.Domain.Aggregates;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Commerce;

/// <summary>Selling from the catalog, cash or on installments, and how an installment sale is collected.</summary>
public class SalesTests
{
    private const string Tenant = "tenant-a";
    private static readonly DateTimeOffset SoldAt = new(2026, 11, 28, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Installments_SplitTheFinancedAmount_AndAddUpExactly()
    {
        var plan = new InstallmentPlanRequest { Count = 3, Frequency = "monthly", DownPayment = 100 };

        var installments = InstallmentPlanner.Build(1100m, plan, SoldAt);

        Assert.Equal([333.33m, 333.33m, 333.34m], installments.Select(i => i.Amount).ToArray());
        Assert.Equal(1000m, installments.Sum(i => i.Amount));
        Assert.Equal([new DateTime(2026, 12, 28), new DateTime(2027, 1, 28), new DateTime(2027, 2, 28)],
            installments.Select(i => i.DueDate.UtcDateTime.Date).ToArray());
        Assert.All(installments, i => Assert.Equal(12, i.DueDate.UtcDateTime.Hour));
    }

    [Theory]
    [InlineData("semanal", 7)]
    [InlineData("quincenal", 14)]
    [InlineData("weekly", 7)]
    [InlineData("biweekly", 14)]
    public void TheFrequency_SetsTheGapBetweenInstallments(string frequency, int days)
    {
        var installments = InstallmentPlanner.Build(400m, new InstallmentPlanRequest { Count = 2, Frequency = frequency }, SoldAt);

        Assert.Equal(days, (installments[1].DueDate - installments[0].DueDate).TotalDays);
    }

    [Theory]
    [InlineData(1, "monthly", 0, "entre 2 y 60")]
    [InlineData(4, "anual", 0, "frecuencia")]
    [InlineData(4, "monthly", -1, "negativa")]
    [InlineData(4, "monthly", 1000, "contado")]
    public void APlanThatCannotWork_IsRejectedWithAReason(int count, string frequency, decimal downPayment, string reason)
        => Assert.Contains(reason, InstallmentPlanner.Validate(new InstallmentPlanRequest { Count = count, Frequency = frequency, DownPayment = downPayment }, 1000m));

    [Fact]
    public void AMissedInstallment_MakesTheAccountOverdue()
    {
        var loan = SaleLoan(InstallmentPlanner.Build(3000m, new InstallmentPlanRequest { Count = 3, Frequency = "monthly" }, SoldAt));

        InstallmentSchedule.Age(loan, new DateTimeOffset(2027, 1, 7, 18, 0, 0, TimeSpan.Zero));

        Assert.Equal(1000m, loan.OverdueBalance);
        Assert.Equal(10, loan.DaysPastDue);
        Assert.Equal(DelinquencyStage.Dpd1To30, loan.Stage);
        Assert.Equal(3000m, loan.TotalBalance);
        Assert.Equal("overdue", loan.Installments[0].Status);
        Assert.Equal(new DateTime(2026, 12, 28), loan.NextPaymentDate!.Value.UtcDateTime.Date);
    }

    [Fact]
    public void APayment_CoversTheOldestInstallmentsFirst_AndTheAccountCatchesUp()
    {
        var loan = SaleLoan(InstallmentPlanner.Build(3000m, new InstallmentPlanRequest { Count = 3, Frequency = "monthly" }, SoldAt));
        InstallmentSchedule.Age(loan, new DateTimeOffset(2027, 1, 7, 0, 0, 0, TimeSpan.Zero));

        CollectionPaymentService.Apply(loan, 1500m, new DateTimeOffset(2027, 1, 7, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal("paid", loan.Installments[0].Status);
        Assert.Equal(500m, loan.Installments[1].PaidAmount);
        Assert.Equal(0m, loan.OverdueBalance);
        Assert.Equal(0, loan.DaysPastDue);
        Assert.Equal(1500m, loan.TotalBalance);
        Assert.Equal(500m, loan.NextPaymentAmount);
        Assert.Equal(CollectionLoanStatus.Active, loan.Status);
    }

    [Fact]
    public void PayingEveryInstallment_ClosesTheAccount()
    {
        var loan = SaleLoan(InstallmentPlanner.Build(2000m, new InstallmentPlanRequest { Count = 2, Frequency = "monthly" }, SoldAt));

        CollectionPaymentService.Apply(loan, 2000m, SoldAt.AddDays(1));

        Assert.Equal(CollectionLoanStatus.PaidOff, loan.Status);
        Assert.All(loan.Installments, i => Assert.Equal("paid", i.Status));
    }

    [Fact]
    public async Task TheAgingWorker_SavesOnlyAccountsThatChanged()
    {
        var late = SaleLoan(InstallmentPlanner.Build(2000m, new InstallmentPlanRequest { Count = 2, Frequency = "monthly" }, SoldAt));
        var onTime = SaleLoan(InstallmentPlanner.Build(2000m, new InstallmentPlanRequest { Count = 2, Frequency = "monthly" }, SoldAt.AddMonths(3)));
        var now = new DateTimeOffset(2027, 1, 7, 0, 0, 0, TimeSpan.Zero);
        InstallmentSchedule.Age(onTime, now);
        var store = new Mock<ICollectionsStore>();
        store.Setup(x => x.GetActiveSaleLoansAsync(It.IsAny<CancellationToken>())).ReturnsAsync([late, onTime]);

        var changed = await InstallmentAgingWorker.AgeAllAsync(store.Object, now, CancellationToken.None);

        Assert.Equal(1, changed);
        store.Verify(x => x.UpsertLoanAsync(late, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(x => x.UpsertLoanAsync(onTime, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ACashSale_UsesCatalogPrices_AndTakesStockOut()
    {
        var (commerce, collections, service) = Setup();

        var result = await service.CreateAsync(new NewSale
        {
            TenantId = Tenant,
            PartyId = "party-1",
            PaymentMethod = "contado",
            Lines = [new SaleLine { Sku = "RTR", Quantity = 2 }, new SaleLine { Sku = "SRV" }]
        }, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("cash", result.Sale!.PaymentMethod);
        Assert.Equal("sale_created", result.Sale.State);
        Assert.Equal(2 * 3600m + 1200m, result.Sale.Total);
        Assert.Null(result.Sale.Financing);
        commerce.Verify(x => x.AdjustInventoryAsync(Tenant, "RTR", -2, "sale_created", result.Sale.Id, It.IsAny<CancellationToken>()), Times.Once);
        commerce.Verify(x => x.AdjustInventoryAsync(Tenant, "SRV", It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        collections.Verify(x => x.UpsertLoanAsync(It.IsAny<CollectionLoanDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnInstallmentSale_OpensTheCustomersAccountInCollections()
    {
        var (_, collections, service) = Setup();
        CollectionLoanDocument? opened = null;
        collections.Setup(x => x.UpsertLoanAsync(It.IsAny<CollectionLoanDocument>(), It.IsAny<CancellationToken>()))
            .Callback<CollectionLoanDocument, CancellationToken>((l, _) => opened = l)
            .Returns(Task.CompletedTask);

        var result = await service.CreateAsync(new NewSale
        {
            TenantId = Tenant,
            PartyId = "party-1",
            PaymentMethod = "cuotas",
            Installments = new InstallmentPlanRequest { Count = 4, Frequency = "mensual", DownPayment = 1600 },
            Lines = [new SaleLine { Sku = "CCTV" }]
        }, CancellationToken.None);

        Assert.True(result.Ok);
        var sale = result.Sale!;
        Assert.Equal("installments", sale.PaymentMethod);
        Assert.Equal("financed", sale.State);
        Assert.Equal(8000m, sale.Financing!.FinancedAmount);
        Assert.All(sale.Financing.Installments, i => Assert.Equal(2000m, i.Amount));
        Assert.NotNull(opened);
        Assert.Equal("sale", opened!.Origin);
        Assert.Equal(sale.Id, opened.SaleId);
        Assert.Equal(SalesService.LoanExternalIdFor(sale.Id), opened.LoanExternalId);
        Assert.Equal("party-1", opened.PartyId);
        Assert.Equal("+50581143874", opened.CustomerPhone);
        Assert.Equal(8000m, opened.TotalBalance);
        Assert.Equal(2000m, opened.NextPaymentAmount);
        Assert.Equal(opened.Id, sale.Financing.CollectionLoanId);
    }

    [Fact]
    public async Task AnInstallmentSaleWithoutAPlan_IsRejected()
    {
        var (commerce, _, service) = Setup();

        var result = await service.CreateAsync(new NewSale
        {
            TenantId = Tenant, PartyId = "party-1", PaymentMethod = "installments", Lines = [new SaleLine { Sku = "CCTV" }]
        }, CancellationToken.None);

        Assert.Equal(SaleError.InvalidPlan, result.Error);
        commerce.Verify(x => x.CreateSaleAsync(It.IsAny<CommerceSaleDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SellingMoreThanInStock_IsRejected()
    {
        var (_, _, service) = Setup();

        var result = await service.CreateAsync(new NewSale
        {
            TenantId = Tenant, PartyId = "party-1", Lines = [new SaleLine { Sku = "RTR", Quantity = 11 }]
        }, CancellationToken.None);

        Assert.Equal(SaleError.InsufficientStock, result.Error);
        Assert.Equal("RTR", result.Sku);
    }

    [Fact]
    public async Task TheStoreTaxRate_IsChargedWhenTheSaleDoesNotSayOtherwise()
    {
        var (commerce, _, service) = Setup(taxRate: 0.15m);

        var result = await service.CreateAsync(new NewSale
        {
            TenantId = Tenant, PartyId = "party-1", Lines = [new SaleLine { Sku = "SRV" }]
        }, CancellationToken.None);

        Assert.Equal(180m, result.Sale!.Tax);
        Assert.Equal(1380m, result.Sale.Total);
    }

    [Fact]
    public async Task AStoreTaxTypedAsAPercentage_IsReadAsOne()
    {
        var (_, _, service) = Setup(taxRate: 15m);

        var result = await service.CreateAsync(new NewSale
        {
            TenantId = Tenant, PartyId = "party-1", Lines = [new SaleLine { Sku = "SRV" }]
        }, CancellationToken.None);

        Assert.Equal(180m, result.Sale!.Tax);
    }

    [Fact]
    public async Task AQuote_PricesFromTheCatalog_AndNamesWhatIsMissing()
    {
        var (_, _, service) = Setup();

        var ok = await service.QuoteAsync(Tenant, [new SaleLine { Sku = "CCTV" }], CancellationToken.None);
        var missing = await service.QuoteAsync(Tenant, [new SaleLine { Sku = "SKU1234" }], CancellationToken.None);

        Assert.Equal(9600m, ok.Total);
        Assert.Equal("NIO", ok.Currency);
        Assert.Contains("SKU1234", missing.Error);
    }

    [Theory]
    [InlineData("Tu referencia es [REFERENCIA_VENTA].", "Tu referencia es 494AA1B7.")]
    [InlineData("Listo. [referencia_venta]", "Listo. 494AA1B7")]
    [InlineData("Sin etiqueta.", "Sin etiqueta.")]
    public void TheSaleReference_IsWrittenBySystem_NotCopiedByTheModel(string reply, string expected)
        => Assert.Equal(expected, SaleReferenceReplyTransformer.Render(reply, new CommerceSaleDocument { Id = "494aa1b72f6a419cb769f3c19d01305f" }));

    [Fact]
    public void WithoutASale_TheReferencePlaceholderIsDropped()
        => Assert.Equal("Gracias.", SaleReferenceReplyTransformer.Render("Gracias. [REFERENCIA_VENTA]", null));

    [Fact]
    public void AReplyToASalesCampaign_IsAnsweredByItsAssistant()
    {
        var session = ChannelSession.Create(Tenant, "wa-1", ChannelType.WhatsApp, "+50581143874");
        session.LinkAgent("collections-agent");

        var change = InboundMessageIngest.CampaignAssistantFor(session, new CampaignContract { Id = "camp-1", AssistantId = "sales-agent" });

        Assert.Equal("sales-agent", change!.AgentId);
    }

    [Fact]
    public void AReplyToACampaignWithoutAssistant_GoesBackToTheChannelsOwn_OnlyIfACampaignChangedIt()
    {
        var byCampaign = ChannelSession.Create(Tenant, "wa-1", ChannelType.WhatsApp, "+50581143874");
        byCampaign.LinkAgent("sales-agent");
        byCampaign.Metadata[InboundMessageIngest.CampaignAgentKey] = "camp-1";
        var byOperator = ChannelSession.Create(Tenant, "wa-1", ChannelType.WhatsApp, "+50581143875");
        byOperator.LinkAgent("vip-agent");
        var plain = new CampaignContract { Id = "camp-2" };

        Assert.Null(InboundMessageIngest.CampaignAssistantFor(byCampaign, plain)!.AgentId);
        Assert.Null(InboundMessageIngest.CampaignAssistantFor(byOperator, plain));
    }

    [Fact]
    public void AnAlreadyRoutedConversation_IsNotChangedAgain()
    {
        var session = ChannelSession.Create(Tenant, "wa-1", ChannelType.WhatsApp, "+50581143874");
        session.LinkAgent("sales-agent");
        session.Metadata[InboundMessageIngest.CampaignAgentKey] = "camp-1";

        Assert.Null(InboundMessageIngest.CampaignAssistantFor(session, new CampaignContract { Id = "camp-1", AssistantId = "sales-agent" }));
    }

    private static CollectionLoanDocument SaleLoan(List<CollectionInstallment> installments) => new()
    {
        TenantId = Tenant,
        LoanExternalId = "VENTA-1",
        Origin = "sale",
        Status = CollectionLoanStatus.Active,
        Installments = installments,
        TotalBalance = installments.Sum(i => i.Amount)
    };

    private static (Mock<ICommerceStore> Commerce, Mock<ICollectionsStore> Collections, SalesService Service) Setup(decimal taxRate = 0m)
    {
        var commerce = new Mock<ICommerceStore>();
        commerce.Setup(x => x.GetStoreSettingsAsync(Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceStoreSettingsDocument { TenantId = Tenant, Currency = "NIO", TaxRate = taxRate });
        commerce.Setup(x => x.GetPartyByIdAsync(Tenant, "party-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommercePartyDocument { Id = "party-1", TenantId = Tenant, FullName = "Carlos Méndez", Phone = "+50581143874" });
        var items = new[]
        {
            new CommerceInventoryItemDocument { TenantId = Tenant, Sku = "RTR", Name = "Router WiFi 6", UnitPrice = 3600, OnHand = 10, TracksInventory = true },
            new CommerceInventoryItemDocument { TenantId = Tenant, Sku = "SRV", Name = "Instalación de internet", UnitPrice = 1200, ItemType = "service", TracksInventory = false },
            new CommerceInventoryItemDocument { TenantId = Tenant, Sku = "CCTV", Name = "Instalación de cámaras", UnitPrice = 9600, ItemType = "service", TracksInventory = false }
        };
        foreach (var item in items)
            commerce.Setup(x => x.GetInventoryBySkuAsync(Tenant, item.Sku, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        commerce.Setup(x => x.CalculateSaleAsync(Tenant, It.IsAny<IReadOnlyList<CommerceLineItem>>(), It.IsAny<decimal?>(), It.IsAny<decimal?>(), It.IsAny<bool>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns<string, IReadOnlyList<CommerceLineItem>, decimal?, decimal?, bool, decimal, CancellationToken>((_, lines, _, _, applyTax, rate, _) =>
            {
                var subtotal = lines.Sum(l => l.Subtotal);
                var tax = applyTax ? Math.Round(subtotal * rate, 2) : 0m;
                return Task.FromResult(new SaleCalculationResult(subtotal, 0m, tax, subtotal + tax));
            });
        commerce.Setup(x => x.CreateSaleAsync(It.IsAny<CommerceSaleDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommerceSaleDocument s, CancellationToken _) => s);
        commerce.Setup(x => x.UpdateSaleAsync(It.IsAny<CommerceSaleDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommerceSaleDocument s, CancellationToken _) => s);
        var collections = new Mock<ICollectionsStore>();
        return (commerce, collections, new SalesService(commerce.Object, NullLogger<SalesService>.Instance, collections.Object));
    }
}
