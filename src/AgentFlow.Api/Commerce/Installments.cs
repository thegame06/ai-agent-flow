using AgentFlow.Api.Collections;

namespace AgentFlow.Api.Commerce;

/// <summary>How a sale on installments is paid: the down payment today, the rest in equal installments.</summary>
public sealed record InstallmentPlanRequest
{
    public int Count { get; init; }
    /// <summary>weekly | biweekly | monthly</summary>
    public string Frequency { get; init; } = "monthly";
    public decimal DownPayment { get; init; }
    /// <summary>When the first installment is due; one period after the sale when empty.</summary>
    public DateTimeOffset? FirstDueDate { get; init; }
}

public static class InstallmentPlanner
{
    public const int MinCount = 2;
    public const int MaxCount = 60;
    public static readonly string[] Frequencies = ["weekly", "biweekly", "monthly"];

    /// <summary>Why the plan cannot be used for a sale of <paramref name="total"/>, or null when it can.</summary>
    public static string? Validate(InstallmentPlanRequest? plan, decimal total)
    {
        if (plan is null) return "Indica el plan de cuotas: número de cuotas y frecuencia.";
        if (plan.Count is < MinCount or > MaxCount) return $"El número de cuotas debe estar entre {MinCount} y {MaxCount}.";
        if (!Frequencies.Contains(Normalize(plan.Frequency))) return "La frecuencia debe ser semanal, quincenal o mensual.";
        if (plan.DownPayment < 0) return "La prima no puede ser negativa.";
        if (plan.DownPayment >= total) return "La prima cubre todo el total: regístrala como venta de contado.";
        return null;
    }

    /// <summary>"semanal" / "quincenal" / "mensual" and the English names map to weekly / biweekly / monthly.</summary>
    public static string Normalize(string? frequency) => (frequency ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "weekly" or "semanal" => "weekly",
        "biweekly" or "quincenal" or "fortnightly" => "biweekly",
        "monthly" or "mensual" => "monthly",
        var other => other
    };

    public static DateTimeOffset Advance(DateTimeOffset date, string frequency, int periods) => Normalize(frequency) switch
    {
        "weekly" => date.AddDays(7 * periods),
        "biweekly" => date.AddDays(14 * periods),
        _ => date.AddMonths(periods)
    };

    /// <summary>
    /// Equal installments of the financed amount (total minus down payment), rounded to cents; the
    /// last one absorbs the rounding so they add up exactly.
    /// </summary>
    public static List<CollectionInstallment> Build(decimal total, InstallmentPlanRequest plan, DateTimeOffset soldAt)
    {
        var financed = total - plan.DownPayment;
        var regular = Math.Round(financed / plan.Count, 2, MidpointRounding.AwayFromZero);
        // Due dates are days, kept at noon UTC so they read as the same date in any time zone of the Americas.
        var soldDay = new DateTimeOffset(soldAt.UtcDateTime.Date.AddHours(12), TimeSpan.Zero);
        var first = plan.FirstDueDate ?? Advance(soldDay, plan.Frequency, 1);
        var list = new List<CollectionInstallment>(plan.Count);
        for (var i = 0; i < plan.Count; i++)
        {
            var amount = i == plan.Count - 1 ? financed - regular * (plan.Count - 1) : regular;
            list.Add(new CollectionInstallment
            {
                Number = i + 1,
                DueDate = Advance(first, plan.Frequency, i),
                Amount = amount,
                PaidAmount = 0,
                Status = "pending"
            });
        }
        return list;
    }

    public static string FrequencyLabel(string frequency) => Normalize(frequency) switch
    {
        "weekly" => "semanal",
        "biweekly" => "quincenal",
        _ => "mensual"
    };
}

/// <summary>Keeps an account with an installment schedule up to date: payments and lateness.</summary>
public static class InstallmentSchedule
{
    /// <summary>Applies a payment to the oldest unpaid installments first.</summary>
    public static void ApplyPayment(CollectionLoanDocument loan, decimal amount)
    {
        var remaining = amount;
        foreach (var installment in loan.Installments.OrderBy(i => i.Number))
        {
            if (remaining <= 0) break;
            var due = installment.Amount - installment.PaidAmount;
            if (due <= 0) continue;
            var paid = Math.Min(due, remaining);
            installment.PaidAmount += paid;
            remaining -= paid;
        }
    }

    /// <summary>
    /// Recomputes the account from its schedule as of <paramref name="today"/>: what is overdue, how
    /// many days late, the delinquency stage, the next payment and the balance. Accounts born from a
    /// sale have no lender snapshot, so this is what makes reminders and collections follow them.
    /// </summary>
    public static void Age(CollectionLoanDocument loan, DateTimeOffset today)
    {
        if (loan.Installments.Count == 0) return;
        var day = today.UtcDateTime.Date;
        decimal overdue = 0;
        DateTime? oldestUnpaid = null;
        foreach (var installment in loan.Installments.OrderBy(i => i.Number))
        {
            var unpaid = installment.Amount - installment.PaidAmount;
            var dueDay = installment.DueDate.UtcDateTime.Date;
            installment.Status = unpaid <= 0 ? "paid"
                : dueDay < day ? "overdue"
                : installment.PaidAmount > 0 ? "partial"
                : "pending";
            if (unpaid > 0 && dueDay < day)
            {
                overdue += unpaid;
                oldestUnpaid ??= dueDay;
            }
        }

        loan.OverdueBalance = overdue;
        loan.OverduePrincipal = overdue;
        loan.OverdueInterest = 0;
        loan.OverduePenalties = 0;
        loan.OverdueFees = 0;
        loan.TotalBalance = loan.Installments.Sum(i => Math.Max(0, i.Amount - i.PaidAmount));
        loan.DaysPastDue = oldestUnpaid is { } oldest ? (int)(day - oldest).TotalDays : 0;
        loan.Stage = DelinquencyStages.FromDaysPastDue(loan.DaysPastDue);

        var next = loan.Installments.OrderBy(i => i.Number).FirstOrDefault(i => i.Amount - i.PaidAmount > 0);
        loan.NextPaymentDate = next?.DueDate;
        loan.NextPaymentAmount = next is null ? null : next.Amount - next.PaidAmount;
        if (loan.TotalBalance <= 0) loan.Status = CollectionLoanStatus.PaidOff;
        loan.SnapshotDate = today;
        loan.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
