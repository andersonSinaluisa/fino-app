using Nexo.Domain.Common;

namespace Nexo.Domain.Budgets;

/// <summary>How often a budget's amount "refills".</summary>
public enum BudgetPeriod
{
    Weekly,
    Biweekly,
    Monthly,

    /// <summary>A single explicit range [StartDate, EndDate]. Never recurs.</summary>
    Custom,
}

/// <summary>
/// Used to order budgets in lists and to decide which insight wins the single
/// Home slot. It never changes how money is counted.
/// </summary>
public enum BudgetPriority
{
    Essential,
    Important,
    Flexible,
}

/// <summary>
/// Visual state of a budget. Thresholds live in <see cref="BudgetCalculator"/>
/// so the client never re-derives them.
/// </summary>
public enum BudgetUsageLevel
{
    /// <summary>Below 70 %.</summary>
    Normal,

    /// <summary>70 % – 89.9 %.</summary>
    Attention,

    /// <summary>90 % – 99.9 %.</summary>
    NearLimit,

    /// <summary>100 % or more.</summary>
    Exceeded,
}

/// <summary>
/// One occurrence of a budget: an inclusive range of LOCAL calendar dates
/// (the user's time zone decides where a day starts, never UTC).
/// </summary>
public readonly record struct BudgetWindow(DateOnly Start, DateOnly End)
{
    public int Days => End.DayNumber - Start.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    public bool Overlaps(BudgetWindow other) => Start <= other.End && other.Start <= End;
}

/// <summary>
/// Pure period arithmetic. Budget occurrences (septiembre, octubre, ...) are
/// DERIVED from the budget's definition instead of being stored as rows: the
/// definition plus the movements are enough to rebuild any past period, so
/// "moving to October" never mutates or destroys September -- there is simply
/// nothing to mutate. Only the amount can change over time, and that history is
/// kept in <see cref="BudgetAmountRevision"/>.
/// </summary>
public static class BudgetSchedule
{
    /// <summary>
    /// The occurrence of the budget that contains <paramref name="date"/>, or null
    /// when the budget does not apply on that day (before it starts, after its end,
    /// or after the only window of a non-recurring budget).
    /// </summary>
    public static BudgetWindow? WindowContaining(
        BudgetPeriod period,
        bool isRecurring,
        DateOnly startDate,
        DateOnly? endDate,
        DateOnly date)
    {
        if (date < startDate)
        {
            return null;
        }

        if (endDate is { } last && date > last)
        {
            return null;
        }

        var window = period switch
        {
            BudgetPeriod.Custom => new BudgetWindow(startDate, endDate ?? startDate),
            BudgetPeriod.Weekly => FixedLength(startDate, date, 7),
            BudgetPeriod.Biweekly => FixedLength(startDate, date, 14),
            BudgetPeriod.Monthly => MonthlyContaining(startDate, date),
            _ => throw new DomainException("invalid_budget_period", "Período de presupuesto no válido."),
        };

        if (period != BudgetPeriod.Custom && !isRecurring && window.Start != startDate)
        {
            // A one-off weekly/monthly budget only ever has its first window.
            return null;
        }

        return window;
    }

    /// <summary>The first window of the budget -- used for overlap checks and one-off budgets.</summary>
    public static BudgetWindow FirstWindow(BudgetPeriod period, DateOnly startDate, DateOnly? endDate) =>
        WindowContaining(period, isRecurring: false, startDate, endDate, startDate)
        ?? new BudgetWindow(startDate, startDate);

    /// <summary>The window right before <paramref name="window"/>, or null if the budget had not started yet.</summary>
    public static BudgetWindow? Previous(
        BudgetPeriod period,
        bool isRecurring,
        DateOnly startDate,
        DateOnly? endDate,
        BudgetWindow window) =>
        window.Start <= startDate
            ? null
            : WindowContaining(period, isRecurring, startDate, endDate, window.Start.AddDays(-1));

    /// <summary>
    /// The full date range over which a budget can ever apply. Two active budgets
    /// for the same category conflict when these ranges intersect -- regardless of
    /// period, so a weekly and a monthly "Comida" can never both consume the same
    /// movement.
    /// </summary>
    public static (DateOnly Start, DateOnly? End) ActiveRange(
        BudgetPeriod period,
        bool isRecurring,
        DateOnly startDate,
        DateOnly? endDate)
    {
        if (period == BudgetPeriod.Custom || !isRecurring)
        {
            return (startDate, FirstWindow(period, startDate, endDate).End);
        }

        return (startDate, endDate);
    }

    public static bool RangesOverlap((DateOnly Start, DateOnly? End) a, (DateOnly Start, DateOnly? End) b)
    {
        var aEnd = a.End ?? DateOnly.MaxValue;
        var bEnd = b.End ?? DateOnly.MaxValue;
        return a.Start <= bEnd && b.Start <= aEnd;
    }

    private static BudgetWindow FixedLength(DateOnly anchor, DateOnly date, int length)
    {
        var elapsed = date.DayNumber - anchor.DayNumber;
        var index = elapsed / length;
        var start = anchor.AddDays(index * length);
        return new BudgetWindow(start, start.AddDays(length - 1));
    }

    /// <summary>
    /// Monthly windows keep the start date's day of month as their anchor, clamped
    /// to short months: a budget that starts on the 31st runs 31 ene → 27 feb,
    /// 28 feb → 30 mar, 31 mar → 29 abr... A budget that starts on the 1st is
    /// simply the calendar month.
    /// </summary>
    private static BudgetWindow MonthlyContaining(DateOnly anchor, DateOnly date)
    {
        var anchorDay = anchor.Day;
        var candidate = AnchorIn(date.Year, date.Month, anchorDay);
        if (candidate > date)
        {
            var previousMonth = new DateOnly(date.Year, date.Month, 1).AddMonths(-1);
            candidate = AnchorIn(previousMonth.Year, previousMonth.Month, anchorDay);
        }

        var nextMonth = new DateOnly(candidate.Year, candidate.Month, 1).AddMonths(1);
        var nextStart = AnchorIn(nextMonth.Year, nextMonth.Month, anchorDay);
        return new BudgetWindow(candidate, nextStart.AddDays(-1));
    }

    private static DateOnly AnchorIn(int year, int month, int anchorDay) =>
        new(year, month, Math.Min(anchorDay, DateTime.DaysInMonth(year, month)));
}
