using AgentFlow.Api.Collections;
using AgentFlow.Api.Commerce;
using Moq;

namespace AgentFlow.Tests.Unit.Collections;

public sealed class CollectionsImportTests
{
    private const string Tenant = "tenant-1";
    private static readonly DateTimeOffset Cutoff = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    // ── CSV parser ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parser_ReadsTemplate_OneCustomerWithTwoLoans()
    {
        var result = CollectionsCsvParser.Parse(CollectionsCsvParser.BuildTemplate());

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.TotalRows);
        Assert.All(result.Rows, r => Assert.Equal("CLI-001", r.Row.CustomerId));
        var first = result.Rows[0].Row;
        Assert.Equal("PR-1001", first.LoanId);
        Assert.Equal(12500m, first.TotalBalance);
        Assert.Equal(18, first.DaysPastDue);
        Assert.Equal(new DateTimeOffset(2026, 12, 5, 0, 0, 0, TimeSpan.Zero), first.NextPaymentDate);
    }

    [Fact]
    public void Parser_AcceptsSpanishHeaders_Semicolons_DecimalComma_AndQuotes()
    {
        var csv = "Cliente_ID;Nombre;Teléfono;Préstamo_ID;Saldo Total;Días Atraso;Estado\n" +
                  "C-9;\"Pérez; Juan\";5550001;L-1;1234,50;45;castigado\n";

        var result = CollectionsCsvParser.Parse(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows).Row;
        Assert.Equal("Pérez; Juan", row.FullName);
        Assert.Equal(1234.50m, row.TotalBalance);
        Assert.Equal(45, row.DaysPastDue);
        Assert.Equal("castigado", row.Status);
    }

    [Fact]
    public void Parser_ReportsBadValuesWithSpreadsheetRowNumbers()
    {
        var csv = "customer_id,loan_id,total_balance,next_payment_date\n" +
                  "C1,L1,100,2026-12-01\n" +
                  "\n" +
                  "C2,L2,abc,31/02/x\n";

        var result = CollectionsCsvParser.Parse(csv);

        Assert.Single(result.Rows);
        Assert.Equal(2, result.TotalRows);
        Assert.Contains(result.Errors, e => e.Row == 4 && e.Field == "total_balance");
        Assert.Contains(result.Errors, e => e.Row == 4 && e.Field == "next_payment_date");
    }

    [Fact]
    public void Parser_RequiresLoanIdBalanceAndCustomerColumns()
    {
        var result = CollectionsCsvParser.Parse("name,amount\nAna,10\n");

        Assert.Empty(result.Rows);
        Assert.Contains(result.Errors, e => e.Field == "loan_id");
        Assert.Contains(result.Errors, e => e.Field == "total_balance");
        Assert.Contains(result.Errors, e => e.Field == "customer_id");
    }

    // ── import service ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_GroupsLoansByCustomer_ComputesStage_AndSnapshots()
    {
        var (service, store, parties) = CreateService();
        var rows = Rows(
            new CollectionLoanRow { CustomerId = "C1", FullName = "Ana", Phone = "5550001", LoanId = "L1", TotalBalance = 1000, OverduePrincipal = 100, OverdueInterest = 20, DaysPastDue = 45 },
            new CollectionLoanRow { CustomerId = "C1", FullName = "Ana", Phone = "5550001", LoanId = "L2", TotalBalance = 500, DaysPastDue = 0 },
            new CollectionLoanRow { CustomerId = "C2", FullName = "Luis", LoanId = "L3", TotalBalance = 300, OldestDueDate = Cutoff.AddDays(-95) });

        var result = await service.ImportAsync(Tenant, rows, [], rows.Count, Cutoff, "csv", "cartera.csv", dryRun: false, "user-1");

        Assert.Equal((3, 2, 3, 0, 0), (result.ValidRows, result.Customers, result.LoansCreated, result.LoansUpdated, result.Failed));
        parties.Verify(p => p.UpsertPartyByChannelIdentityAsync(Tenant, "external", "C1", "Ana", "customer", null, null, "5550001", null, "Ana", It.IsAny<CancellationToken>()), Times.Once);
        var l1 = store.Loans["L1"];
        Assert.Equal(120m, l1.OverdueBalance);
        Assert.Equal(DelinquencyStage.Dpd31To60, l1.Stage);
        Assert.Equal(l1.PartyId, store.Loans["L2"].PartyId); // same customer, two loans
        Assert.Equal(95, store.Loans["L3"].DaysPastDue);     // derived from the oldest due date
        Assert.Equal(DelinquencyStage.Dpd90Plus, store.Loans["L3"].Stage);
        Assert.Equal(3, store.Snapshots.Count);
        Assert.Single(store.Batches);
    }

    [Fact]
    public async Task Import_ReimportUpdatesTheSameLoan_InsteadOfDuplicating()
    {
        var (service, store, _) = CreateService();
        await service.ImportAsync(Tenant, Rows(new CollectionLoanRow { CustomerId = "C1", LoanId = "L1", TotalBalance = 1000, DaysPastDue = 10 }), [], 1, Cutoff, "csv", null, false, "u");
        var firstId = store.Loans["L1"].Id;

        var second = await service.ImportAsync(Tenant, Rows(new CollectionLoanRow { CustomerId = "C1", LoanId = "L1", TotalBalance = 800, DaysPastDue = 0, Status = "activo" }), [], 1, Cutoff.AddDays(1), "csv", null, false, "u");

        Assert.Equal((0, 1), (second.LoansCreated, second.LoansUpdated));
        Assert.Single(store.Loans);
        Assert.Equal(firstId, store.Loans["L1"].Id);
        Assert.Equal(800m, store.Loans["L1"].TotalBalance);
        Assert.Equal(2, store.Snapshots.Count); // one per cut-off date
    }

    [Fact]
    public async Task Import_DryRun_ValidatesWithoutWriting()
    {
        var (service, store, parties) = CreateService();
        var rows = Rows(
            new CollectionLoanRow { CustomerId = "C1", LoanId = "L1", TotalBalance = 100 },
            new CollectionLoanRow { CustomerId = "C1", LoanId = "L1", TotalBalance = 100 },          // duplicated loan id
            new CollectionLoanRow { LoanId = "L2", TotalBalance = 50 },                              // no customer
            new CollectionLoanRow { CustomerId = "C3", LoanId = "L3", TotalBalance = -1, Status = "x" });

        var result = await service.ImportAsync(Tenant, rows, [], rows.Count, Cutoff, "csv", null, dryRun: true, "u");

        Assert.True(result.DryRun);
        Assert.Equal((1, 1, 3), (result.ValidRows, result.LoansCreated, result.Failed));
        // Rows are numbered like a spreadsheet: the header is row 1, data starts at row 2.
        Assert.Contains(result.Errors, e => e.Row == 3 && e.Field == "loan_id");
        Assert.Contains(result.Errors, e => e.Row == 4 && e.Field == "customer_id");
        Assert.Contains(result.Errors, e => e.Row == 5 && e.Field == "total_balance");
        Assert.Contains(result.Errors, e => e.Row == 5 && e.Field == "status");
        Assert.Empty(store.Loans);
        Assert.Empty(store.Batches);
        parties.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, DelinquencyStage.Current)]
    [InlineData(1, DelinquencyStage.Dpd1To30)]
    [InlineData(30, DelinquencyStage.Dpd1To30)]
    [InlineData(31, DelinquencyStage.Dpd31To60)]
    [InlineData(61, DelinquencyStage.Dpd61To90)]
    [InlineData(91, DelinquencyStage.Dpd90Plus)]
    public void Stage_FollowsDaysPastDueBuckets(int dpd, DelinquencyStage expected)
        => Assert.Equal(expected, DelinquencyStages.FromDaysPastDue(dpd));

    private static IReadOnlyList<(int RowNumber, CollectionLoanRow Row)> Rows(params CollectionLoanRow[] rows)
        => rows.Select((row, i) => (i + 2, row)).ToList();

    private static (CollectionsImportService Service, InMemoryCollectionsStore Store, Mock<ICommerceStore> Parties) CreateService()
    {
        var store = new InMemoryCollectionsStore();
        var parties = new Mock<ICommerceStore>();
        parties.Setup(p => p.UpsertPartyByChannelIdentityAsync(
                Tenant, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, string identifier, string? _, string _, string? _, string? _, string? _, string? _, string? _, CancellationToken _) =>
                new CommercePartyDocument { Id = $"party-{identifier}", TenantId = Tenant, Identifier = identifier });
        return (new CollectionsImportService(store, parties.Object), store, parties);
    }

    internal sealed class InMemoryCollectionsStore : ICollectionsStore
    {
        public Dictionary<string, CollectionLoanDocument> Loans { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, CollectionLoanSnapshotDocument> Snapshots { get; } = new();
        public List<CollectionImportBatchDocument> Batches { get; } = [];

        public Task<IReadOnlyDictionary<string, CollectionLoanDocument>> GetLoansByExternalIdsAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, CollectionLoanDocument>>(Loans.Where(l => ids.Contains(l.Key)).ToDictionary(l => l.Key, l => l.Value));
        public Task UpsertLoanAsync(CollectionLoanDocument loan, CancellationToken ct = default) { Loans[loan.LoanExternalId] = loan; return Task.CompletedTask; }
        public Task UpsertSnapshotAsync(CollectionLoanSnapshotDocument s, CancellationToken ct = default) { Snapshots[s.Id] = s; return Task.CompletedTask; }
        public Task<IReadOnlyList<CollectionLoanDocument>> GetLoansAsync(string tenantId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CollectionLoanDocument>>(Loans.Values.ToList());
        public Task<IReadOnlyList<CollectionLoanDocument>> GetLoansByPartyAsync(string tenantId, string partyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CollectionLoanDocument>>(Loans.Values.Where(l => l.PartyId == partyId).ToList());
        public Task<CollectionLoanDocument?> GetLoanAsync(string tenantId, string loanId, CancellationToken ct = default) => Task.FromResult(Loans.Values.FirstOrDefault(l => l.Id == loanId));
        public Task<IReadOnlyList<CollectionLoanSnapshotDocument>> GetSnapshotsAsync(string tenantId, string loanId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CollectionLoanSnapshotDocument>>(Snapshots.Values.Where(s => s.LoanId == loanId).ToList());
        public Task InsertImportBatchAsync(CollectionImportBatchDocument batch, CancellationToken ct = default) { Batches.Add(batch); return Task.CompletedTask; }
        public Task<IReadOnlyList<CollectionImportBatchDocument>> GetImportBatchesAsync(string tenantId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CollectionImportBatchDocument>>(Batches);
        public List<CollectionPaymentDocument> Payments { get; } = [];
        public Task InsertPaymentAsync(CollectionPaymentDocument payment, CancellationToken ct = default) { Payments.Add(payment); return Task.CompletedTask; }
        public Task<IReadOnlyList<CollectionPaymentDocument>> GetPaymentsByLoanAsync(string tenantId, string loanId, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CollectionPaymentDocument>>(Payments.Where(p => p.LoanId == loanId).ToList());
        public Task<bool> PaymentReferenceExistsAsync(string tenantId, string reference, CancellationToken ct = default) => Task.FromResult(Payments.Any(p => p.Reference == reference));
        public List<CollectionPromiseDocument> Promises { get; } = [];
        public Task InsertPromiseAsync(CollectionPromiseDocument promise, CancellationToken ct = default) { Promises.Add(promise); return Task.CompletedTask; }
        public Task UpdatePromiseAsync(CollectionPromiseDocument promise, CancellationToken ct = default) => Task.CompletedTask;
        public Task<CollectionPromiseDocument?> GetPromiseAsync(string tenantId, string promiseId, CancellationToken ct = default) => Task.FromResult(Promises.FirstOrDefault(p => p.Id == promiseId));
        public Task<IReadOnlyList<CollectionPromiseDocument>> GetPromisesAsync(string tenantId, CollectionPromiseStatus? status = null, string? partyId = null, int limit = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CollectionPromiseDocument>>(Promises.Where(p => (status == null || p.Status == status) && (partyId == null || p.PartyId == partyId)).ToList());
    }
}
