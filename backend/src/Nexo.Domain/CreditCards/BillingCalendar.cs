using Nexo.Domain.Common;

namespace Nexo.Domain.CreditCards;

/// <summary>
/// One billing cycle of a card: every movement dated from <see cref="Start"/> to
/// <see cref="Closing"/> (both inclusive, in the person's local calendar) belongs
/// to the statement that closes on <see cref="Closing"/> and must be paid by
/// <see cref="Due"/>.
/// </summary>
public readonly record struct BillingCycle(DateOnly Start, DateOnly Closing, DateOnly Due)
{
    public bool Contains(DateOnly date) => date >= Start && date <= Closing;
}

/// <summary>
/// Fechas de corte y de pago. Never "the calendar month": a purchase on 10 sept
/// with corte 15 belongs to the statement closing 15 sept; one on 16 sept, to the
/// next one. Deterministic policy for days a month does not have:
/// <list type="bullet">
/// <item>A configured day the month lacks (31 in April, 30 in February) becomes the
/// LAST day of that month. Corte 31 closes on 28/29 Feb, 30 Apr, 31 May.</item>
/// <item>A purchase ON the closing date belongs to that closing's statement (the
/// cycle closes at the end of that day).</item>
/// <item>The payment date is the first matching day AFTER the closing: in the same
/// month when the payment day is later than the closing day, otherwise in the next
/// month. If clamping a short month makes the payment day land on (or before) the
/// clamped closing, it moves to the day after the closing -- the bank can never ask
/// for payment before the statement exists.</item>
/// </list>
/// </summary>
public static class BillingCalendar
{
    public const int MinimumDay = 1;
    public const int MaximumDay = 31;

    public static void EnsureValidDays(int closingDay, int dueDay)
    {
        if (closingDay is < MinimumDay or > MaximumDay)
        {
            throw new DomainException("card_invalid_closing_day", "El día de corte debe estar entre 1 y 31.");
        }

        if (dueDay is < MinimumDay or > MaximumDay)
        {
            throw new DomainException("card_invalid_due_day", "El día máximo de pago debe estar entre 1 y 31.");
        }

        if (closingDay == dueDay)
        {
            throw new DomainException("card_same_days", "El día máximo de pago no puede ser el mismo día del corte.");
        }
    }

    /// <summary>The configured day in that month, clamped to the month's last day.</summary>
    public static DateOnly DayIn(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    /// <summary>The first closing date on or after <paramref name="date"/>.</summary>
    public static DateOnly ClosingOnOrAfter(DateOnly date, int closingDay)
    {
        var closing = DayIn(date.Year, date.Month, closingDay);
        if (date <= closing)
        {
            return closing;
        }

        var next = FirstOfMonth(date).AddMonths(1);
        return DayIn(next.Year, next.Month, closingDay);
    }

    public static DateOnly PreviousClosing(DateOnly closing, int closingDay)
    {
        var previous = FirstOfMonth(closing).AddMonths(-1);
        return DayIn(previous.Year, previous.Month, closingDay);
    }

    public static DateOnly NextClosing(DateOnly closing, int closingDay)
    {
        var next = FirstOfMonth(closing).AddMonths(1);
        return DayIn(next.Year, next.Month, closingDay);
    }

    public static DateOnly DueDateFor(DateOnly closing, int closingDay, int dueDay)
    {
        if (dueDay > closingDay)
        {
            var sameMonth = DayIn(closing.Year, closing.Month, dueDay);
            return sameMonth > closing ? sameMonth : closing.AddDays(1);
        }

        var next = FirstOfMonth(closing).AddMonths(1);
        return DayIn(next.Year, next.Month, dueDay);
    }

    public static BillingCycle CycleContaining(DateOnly date, int closingDay, int dueDay) =>
        ForClosing(ClosingOnOrAfter(date, closingDay), closingDay, dueDay);

    public static BillingCycle ForClosing(DateOnly closing, int closingDay, int dueDay) =>
        new(PreviousClosing(closing, closingDay).AddDays(1), closing, DueDateFor(closing, closingDay, dueDay));

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
