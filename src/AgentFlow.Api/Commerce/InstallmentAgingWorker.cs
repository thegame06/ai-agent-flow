using AgentFlow.Api.Collections;

namespace AgentFlow.Api.Commerce;

/// <summary>
/// Accounts opened by sales on installments have no lender snapshot: once an hour their lateness
/// is recomputed from the schedule, so a missed installment turns the account overdue and the
/// collections reminders, campaigns and assistant pick it up like any other.
/// </summary>
public sealed class InstallmentAgingWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly ICollectionsStore _store;
    private readonly ILogger<InstallmentAgingWorker> _logger;

    public InstallmentAgingWorker(ICollectionsStore store, ILogger<InstallmentAgingWorker> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var aged = await AgeAllAsync(_store, DateTimeOffset.UtcNow, stoppingToken);
                if (aged > 0) _logger.LogInformation("Recomputed {Count} installment accounts from sales", aged);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Installment aging failed; retrying in {Interval}", Interval);
            }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Ages every active sale account; returns how many changed.</summary>
    public static async Task<int> AgeAllAsync(ICollectionsStore store, DateTimeOffset now, CancellationToken ct)
    {
        var changed = 0;
        foreach (var loan in await store.GetActiveSaleLoansAsync(ct))
        {
            var before = (loan.DaysPastDue, loan.OverdueBalance, loan.Status, loan.NextPaymentDate);
            InstallmentSchedule.Age(loan, now);
            if (before == (loan.DaysPastDue, loan.OverdueBalance, loan.Status, loan.NextPaymentDate)) continue;
            await store.UpsertLoanAsync(loan, ct);
            changed++;
        }
        return changed;
    }
}
