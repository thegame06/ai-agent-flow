using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Collections;
using Xunit;

namespace AgentFlow.Tests.Unit.Collections;

public sealed class RecoveryReportTests
{
    private static readonly DateTimeOffset From = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 11, 30, 23, 59, 59, TimeSpan.Zero);

    [Fact]
    public void Payment_IsAttributedToTheLastContactWithinTheWindow()
    {
        var report = Build(
            loans: [Loan("L1", "P1"), Loan("L2", "P2"), Loan("L3", "P3")],
            contacts:
            [
                Contact("c-old", "camp-a", "P1", From.AddDays(-5), overdue: 1000),
                Contact("c-new", "camp-b", "P1", From.AddDays(2), overdue: 1000),
                Contact("c-stale", "camp-a", "P3", From.AddDays(-40), overdue: 900),
            ],
            payments:
            [
                Payment("L1", "P1", 600, From.AddDays(4)),  // last touch: camp-b
                Payment("L2", "P2", 300, From.AddDays(4)),  // never contacted
                Payment("L3", "P3", 200, From.AddDays(1)),  // contact 41 days before: outside the window
            ]);

        Assert.Equal(1100m, report.Totals.Recovered);
        Assert.Equal(600m, report.Totals.Attributed);
        Assert.Equal(500m, report.Totals.Unattributed);
        Assert.Equal(3, report.Totals.CustomersPaid);
        Assert.Equal(1, report.Totals.AttributedCustomers);
        Assert.Equal(2m, report.Totals.AvgDaysToPay);

        var campB = Assert.Single(report.ByCampaign, g => g.Key == "camp-b");
        Assert.Equal(600m, campB.Recovered);
        Assert.Equal("Campaña B", campB.Label);
        // camp-a only touched customers before the period and none of its contacts led to a payment.
        Assert.DoesNotContain(report.ByCampaign, g => g.Key == "camp-a");
    }

    [Fact]
    public void CutOffDecrease_CountsOnlyWhatNoRegisteredPaymentExplains()
    {
        var report = Build(
            loans: [Loan("L1", "P1"), Loan("L2", "P2")],
            snapshots:
            [
                Snapshot("L1", From.AddDays(-3), 800),
                Snapshot("L1", From.AddDays(10), 300),   // -500: 300 registered + 200 unexplained
                Snapshot("L2", From.AddDays(-3), 1000),
                Snapshot("L2", From.AddDays(10), 0, CollectionLoanStatus.ChargedOff), // written off: not money
            ],
            payments: [Payment("L1", "P1", 300, From.AddDays(5))]);

        Assert.Equal(500m, report.Totals.Recovered);
        Assert.Equal(2, report.Totals.Events);
        Assert.Contains(report.Recent, e => e.Kind == "cutoff" && e.Amount == 200m);
        Assert.Contains(report.Recent, e => e.Kind == "payment" && e.Amount == 300m);
    }

    [Fact]
    public void FunnelAndRate_CountEachCustomerOverdueOnce()
    {
        var replied = Contact("c1", "camp-a", "P1", From.AddDays(1), overdue: 1000, stage: "1-30 días") with
        {
            RepliedAt = From.AddDays(1).AddHours(1),
            Outcome = CampaignOutcomes.PromiseToPay,
            OutcomeSource = CampaignOutcomes.Sources.Agent
        };
        var report = Build(
            loans: [Loan("L1", "P1"), Loan("L2", "P2")],
            contacts:
            [
                replied,
                Contact("c1-followup", "camp-a", "P1", From.AddDays(3), overdue: 1000, stage: "1-30 días"),
                Contact("c2", "camp-a", "P2", From.AddDays(1), overdue: 3000, stage: "31-60 días"),
                Contact("c3", "camp-a", "P3", From.AddDays(1), overdue: 500, status: CampaignContactExecutionStatus.Failed),
            ],
            payments: [Payment("L1", "P1", 1000, From.AddDays(6))]);

        Assert.Equal(4, report.Funnel.Contacts);
        Assert.Equal(3, report.Funnel.Reached);
        Assert.Equal(1, report.Funnel.Replied);
        Assert.Equal(1, report.Funnel.Promised);
        Assert.Equal(1, report.Funnel.Paid); // attributed to the follow-up, the last touch

        Assert.Equal(4000m, report.Totals.ManagedOverdue); // P1 once, P2; P3 was never reached
        Assert.Equal(0.25m, report.Totals.RecoveryRate);

        var stage = Assert.Single(report.ByStage, g => g.Label == "1-30 días");
        Assert.Equal(1000m, stage.Recovered);
        Assert.Equal(1m, stage.RecoveryRate);
        Assert.Contains(report.Resolution, r => r.Key == "assistant" && r.Contacts == 1);
    }

    [Fact]
    public void Promises_KeptRateIgnoresOpenOnes_AndPaymentsUnderAPromiseAreFlagged()
    {
        var report = Build(
            loans: [Loan("L1", "P1")],
            promises:
            [
                Promise("P1", From.AddDays(1), From.AddDays(5), CollectionPromiseStatus.Kept, 500),
                Promise("P2", From.AddDays(1), From.AddDays(5), CollectionPromiseStatus.Broken, 200),
                Promise("P3", From.AddDays(2), From.AddDays(40), CollectionPromiseStatus.Pending, null),
                Promise("P4", From.AddDays(2), From.AddDays(9), CollectionPromiseStatus.Cancelled, 900),
            ],
            payments: [Payment("L1", "P1", 500, From.AddDays(4))]);

        Assert.Equal(3, report.Promises.Created);
        Assert.Equal(700m, report.Promises.PromisedAmount);
        Assert.Equal(0.5m, report.Promises.KeptRate);
        Assert.Equal(1, report.Promises.Cancelled);
        Assert.Equal("NIO", report.Currency);
        Assert.Equal(500m, report.Promises.RecoveredWithPromise);
        Assert.True(Assert.Single(report.Recent).WithPromise);
        Assert.Equal(30, report.Daily.Count);
        Assert.Equal(500m, report.Daily.Single(d => d.Date == From.AddDays(4).UtcDateTime.Date).Unattributed);
    }

    // ------------------------------------------------------------------

    private static RecoveryReport Build(
        CollectionLoanDocument[]? loans = null,
        CampaignContactExecutionContract[]? contacts = null,
        CollectionPaymentDocument[]? payments = null,
        CollectionLoanSnapshotDocument[]? snapshots = null,
        CollectionPromiseDocument[]? promises = null)
        => RecoveryReportBuilder.Build(new RecoveryInputs
        {
            From = From,
            To = To,
            Loans = loans ?? [],
            Contacts = contacts ?? [],
            Payments = payments ?? [],
            Snapshots = snapshots ?? [],
            Promises = promises ?? [],
            CampaignNames = new Dictionary<string, string> { ["camp-a"] = "Campaña A", ["camp-b"] = "Campaña B" }
        });

    private static CollectionLoanDocument Loan(string id, string party) => new()
    {
        Id = id,
        TenantId = "t",
        LoanExternalId = $"EXT-{id}",
        PartyId = party,
        CustomerName = $"Cliente {party}",
        Currency = "NIO"
    };

    private static CampaignContactExecutionContract Contact(
        string id, string campaign, string party, DateTimeOffset at, decimal overdue,
        string stage = "1-30 días", CampaignContactExecutionStatus status = CampaignContactExecutionStatus.Sent) => new()
    {
        Id = id,
        TenantId = "t",
        CampaignId = campaign,
        PartyId = party,
        Status = status,
        CreatedAt = at,
        Variables = new Dictionary<string, string>
        {
            ["total_vencido"] = overdue.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture),
            ["etapa"] = stage
        }
    };

    private static CollectionPaymentDocument Payment(string loan, string party, decimal amount, DateTimeOffset at) => new()
    {
        TenantId = "t",
        LoanId = loan,
        PartyId = party,
        Amount = amount,
        PaidAt = at
    };

    private static CollectionLoanSnapshotDocument Snapshot(string loan, DateTimeOffset at, decimal overdue, CollectionLoanStatus status = CollectionLoanStatus.Active) => new()
    {
        Id = $"{loan}:{at:yyyyMMdd}",
        TenantId = "t",
        LoanId = loan,
        SnapshotDate = at,
        OverdueBalance = overdue,
        Status = status
    };

    private static CollectionPromiseDocument Promise(string party, DateTimeOffset created, DateTimeOffset date, CollectionPromiseStatus status, decimal? amount) => new()
    {
        TenantId = "t",
        PartyId = party,
        CreatedAt = created,
        PromisedDate = date,
        Status = status,
        Amount = amount
    };
}
