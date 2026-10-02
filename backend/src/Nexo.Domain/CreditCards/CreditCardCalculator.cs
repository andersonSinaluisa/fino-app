using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Domain.CreditCards;

/// <summary>
/// One movement of the card as the calculator needs it: its LOCAL date (the
/// person's calendar day decides the cycle), direction, type and magnitude.
/// Only movements that count (Posted/Pending) are passed in.
/// </summary>
public sealed record CardMovement(
    Guid Id,
    DateOnly Date,
    TransactionDirection Direction,
    CreditCardMovementType Type,
    decimal Amount)
{
    /// <summary>+ when the movement raises the debt (a charge), − when it lowers it (a credit).</summary>
    public decimal DebtDelta => Direction == TransactionDirection.Expense ? Amount : -Amount;
}

/// <summary>What a card's movements add up to between two dates, by type.</summary>
public sealed record CardActivity(
    decimal Purchases,
    decimal Refunds,
    decimal Payments,
    decimal Interest,
    decimal Fees,
    decimal CashAdvances,
    decimal Adjustments)
{
    public static readonly CardActivity Empty = new(0m, 0m, 0m, 0m, 0m, 0m, 0m);
}

/// <summary>
/// One statement (closed cycle) or the open cycle, with everything derived.
/// </summary>
/// <param name="StatementBalance">Total a pagar: the bank's figure when declared, else computed.</param>
/// <param name="AmountPaid">Payments made after the closing (capped at the balance).</param>
/// <param name="Pending">What is still owed of THIS statement.</param>
/// <param name="Charges">Everything that raised the debt inside the cycle.</param>
/// <param name="Credits">Everything that lowered it inside the cycle.</param>
public sealed record StatementView(
    DateOnly PeriodStart,
    DateOnly ClosingDate,
    DateOnly DueDate,
    decimal StatementBalance,
    decimal? MinimumPayment,
    decimal AmountPaid,
    decimal Pending,
    StatementStatus Status,
    bool IsDeclared,
    decimal Charges,
    decimal Credits)
{
    public BillingCycle Cycle => new(PeriodStart, ClosingDate, DueDate);
}

/// <summary>
/// "Próximo pago": the money the person has to pay next, and by when.
/// </summary>
/// <param name="FromClosedStatement">True: the pending part of a closed statement. False: the projected balance of the open cycle.</param>
/// <param name="MinimumPayment">Pending part of the bank's minimum, when the statement declared one.</param>
public sealed record NextPayment(
    decimal Amount,
    DateOnly DueDate,
    decimal? MinimumPayment,
    bool FromClosedStatement,
    bool IsOverdue);

/// <summary>Everything Fino knows about a card on one date. Every figure the UI shows comes from here.</summary>
/// <param name="CurrentDebt">Deuda actual: everything owed today, deferred installments included.</param>
/// <param name="CreditBalance">Saldo a favor (overpaid card). Never money available to spend.</param>
/// <param name="AvailableCredit">Cupo disponible. Credit, NEVER money: it is never added to Tu dinero or Disponible.</param>
/// <param name="DeferredDebt">Part of the debt in installments not billed yet (deuda futura).</param>
/// <param name="ProjectedStatementBalance">What the open cycle's statement would be if it closed today.</param>
/// <param name="CommittedContribution">What this card adds to Comprometido: the next payment when auto-reserve is on, else 0.</param>
/// <param name="Statements">Newest first; the first one is the open cycle.</param>
public sealed record CreditCardSnapshot(
    decimal CurrentDebt,
    decimal CreditBalance,
    decimal CreditLimit,
    decimal AvailableCredit,
    decimal? UtilizationPercent,
    bool IsOverLimit,
    decimal DeferredDebt,
    BillingCycle CurrentCycle,
    decimal ProjectedStatementBalance,
    StatementView? LastStatement,
    NextPayment? NextPayment,
    decimal CommittedContribution,
    IReadOnlyList<StatementView> Statements);

/// <summary>
/// THE single source of truth for every credit-card figure: deuda actual, cupo
/// disponible, utilización, total de cada estado, pagado, pendiente, estado,
/// próximo pago, y lo que la tarjeta aporta a Comprometido. Pure: no database, no
/// clock -- the caller passes "today" in the person's time zone.
///
/// <para><b>Debt.</b> The card account's balance is negative when money is owed
/// (purchases are expenses on the card). Debt on any past date is reconstructed by
/// walking back from today's balance, exactly like the balance chart does.</para>
///
/// <para><b>Statement balance</b> (when the bank's figure was not declared) = debt at
/// the closing − installments not billed by then. So a $1,200 laptop in 12 cuotas
/// adds $100 per statement, never $1,200 at once; an unpaid previous balance
/// carries over automatically because it is still part of the debt.</para>
///
/// <para><b>Próximo pago.</b> The pending part of the last closed statement; once that
/// is paid, the projected balance of the open cycle (its purchases plus the
/// installment it will bill). Never the whole future debt: deferred installments of
/// later statements are not money to reserve today.</para>
/// </summary>
public static class CreditCardCalculator
{
    /// <summary>A declared closing within this many days of a configured one replaces it (banks shift cortes around weekends).</summary>
    public const int DeclaredClosingToleranceDays = 7;

    /// <summary>How many closed statements are reconstructed at most.</summary>
    public const int MaximumHistory = 24;

    public static CreditCardSnapshot Calculate(
        CreditCardTerms terms,
        decimal accountBalance,
        DateOnly today,
        IReadOnlyCollection<CardMovement> movements,
        IReadOnlyCollection<InstallmentPlanSchedule> plans,
        IReadOnlyCollection<DeclaredStatement> declared)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(movements);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(declared);

        var signedDebtNow = MoneyMath.Round(-accountBalance);
        var currentDebt = Math.Max(signedDebtNow, 0m);
        var creditBalance = Math.Max(-signedDebtNow, 0m);
        var limit = MoneyMath.Round(terms.CreditLimit);

        decimal SignedDebtAt(DateOnly date) =>
            signedDebtNow - movements.Where(m => m.Date > date).Sum(m => m.DebtDelta);

        decimal DeferredAt(DateOnly date) => plans.Sum(p => p.DeferredAt(date));

        var timeline = BuildTimeline(terms, today, movements, plans, declared);
        var currentIndex = timeline.FindIndex(c => !c.IsClosedOn(today));
        var current = timeline[currentIndex];

        var exigibleNow = Math.Max(signedDebtNow - DeferredAt(today), 0m);
        var projected = MoneyMath.Round(Math.Max(signedDebtNow - DeferredAt(current.Cycle.Closing), 0m));

        var closedViews = new List<StatementView>();
        var firstClosed = Math.Max(0, currentIndex - MaximumHistory);
        for (var k = firstClosed; k < currentIndex; k++)
        {
            var entry = timeline[k];
            var cycle = entry.Cycle;
            var isLatest = k == currentIndex - 1;

            var balance = entry.Declared is { } d
                ? d.Balance
                : MoneyMath.Round(Math.Max(SignedDebtAt(cycle.Closing) - DeferredAt(cycle.Closing), 0m));

            var windowEnd = isLatest ? today : Min(timeline[k + 1].Cycle.Closing, today);
            var paid = MoneyMath.Round(movements
                .Where(m => m.Type == CreditCardMovementType.Payment && m.Date > cycle.Closing && m.Date <= windowEnd)
                .Sum(m => m.Amount));
            var paidCapped = Math.Min(paid, balance);

            // A computed statement can never ask for more than is still owed today
            // (a refund after the closing lowers it). A declared one is the bank's own
            // figure: it may include charges Fino has not seen yet, so it stands as printed.
            var pending = isLatest && entry.Declared is null
                ? Math.Max(Math.Min(balance - paidCapped, exigibleNow), 0m)
                : Math.Max(balance - paidCapped, 0m);
            pending = MoneyMath.Round(pending);

            var status = balance <= 0m || pending <= 0m
                ? StatementStatus.Paid
                : cycle.Due < today
                    ? StatementStatus.Overdue
                    : paidCapped > 0m ? StatementStatus.PartiallyPaid : StatementStatus.Closed;

            var (charges, credits) = ChargesAndCredits(movements, cycle);
            closedViews.Add(new StatementView(
                cycle.Start,
                cycle.Closing,
                cycle.Due,
                balance,
                entry.Declared?.MinimumPayment,
                paidCapped,
                pending,
                status,
                entry.Declared is not null,
                charges,
                credits));
        }

        var (openCharges, openCredits) = ChargesAndCredits(movements, current.Cycle);
        var openView = new StatementView(
            current.Cycle.Start,
            current.Cycle.Closing,
            current.Cycle.Due,
            projected,
            null,
            0m,
            projected,
            StatementStatus.Open,
            false,
            openCharges,
            openCredits);

        var last = closedViews.Count > 0 ? closedViews[^1] : null;

        NextPayment? next = null;
        if (last is { Pending: > 0m })
        {
            decimal? minimumPending = last.MinimumPayment is { } minimum
                ? MoneyMath.Round(Math.Max(Math.Min(minimum - last.AmountPaid, last.Pending), 0m))
                : null;
            next = new NextPayment(last.Pending, last.DueDate, minimumPending, true, last.DueDate < today);
        }
        else if (projected > 0m)
        {
            next = new NextPayment(projected, current.Cycle.Due, null, false, false);
        }

        var statements = new List<StatementView> { openView };
        statements.AddRange(Enumerable.Reverse(closedViews));

        return new CreditCardSnapshot(
            MoneyMath.Round(currentDebt),
            MoneyMath.Round(creditBalance),
            limit,
            MoneyMath.Round(Math.Max(limit - signedDebtNow, 0m)),
            limit > 0m ? Math.Round(currentDebt / limit * 100m, 1, MidpointRounding.AwayFromZero) : null,
            currentDebt > limit,
            MoneyMath.Round(DeferredAt(today)),
            current.Cycle,
            projected,
            last,
            next,
            terms.AutoReserve && next is not null ? next.Amount : 0m,
            statements);
    }

    /// <summary>Movements between two local dates (inclusive), added up by type.</summary>
    public static CardActivity Summarize(IEnumerable<CardMovement> movements, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(movements);

        var inRange = movements.Where(m => m.Date >= from && m.Date <= to).ToList();
        decimal Sum(CreditCardMovementType type) => MoneyMath.Round(inRange.Where(m => m.Type == type).Sum(m => m.Amount));
        decimal Adjustments() => MoneyMath.Round(inRange.Where(m => m.Type == CreditCardMovementType.Adjustment).Sum(m => m.DebtDelta));

        return new CardActivity(
            Sum(CreditCardMovementType.Purchase),
            Sum(CreditCardMovementType.Refund),
            Sum(CreditCardMovementType.Payment),
            Sum(CreditCardMovementType.Interest),
            Sum(CreditCardMovementType.Fee),
            Sum(CreditCardMovementType.CashAdvance),
            Adjustments());
    }

    private sealed record TimelineEntry(BillingCycle Cycle, DeclaredStatement? Declared)
    {
        /// <summary>
        /// A cycle closes at the end of its closing day, so on that day it is still
        /// open -- unless the bank already issued (and the person declared) its statement.
        /// </summary>
        public bool IsClosedOn(DateOnly today) => Cycle.Closing < today || (Cycle.Closing == today && Declared is not null);
    }

    /// <summary>
    /// Closings from the first activity up to (and past) today: the configured
    /// calendar, with every declared statement replacing the configured closing it
    /// corresponds to (or added when none is near).
    /// </summary>
    private static List<TimelineEntry> BuildTimeline(
        CreditCardTerms terms,
        DateOnly today,
        IReadOnlyCollection<CardMovement> movements,
        IReadOnlyCollection<InstallmentPlanSchedule> plans,
        IReadOnlyCollection<DeclaredStatement> declared)
    {
        var closingDay = terms.ClosingDay;
        var dueDay = terms.PaymentDueDay;

        var activityDates = movements.Select(m => m.Date)
            .Concat(plans.Select(p => p.StartDate))
            .Concat(declared.Select(d => d.ClosingDate))
            .Where(d => d <= today)
            .ToList();
        var firstActivity = activityDates.Count > 0 ? activityDates.Min() : today;

        var currentClosing = BillingCalendar.ClosingOnOrAfter(today, closingDay);
        var configured = new List<DateOnly>();
        for (var closing = BillingCalendar.ClosingOnOrAfter(firstActivity, closingDay);
             closing <= BillingCalendar.NextClosing(currentClosing, closingDay);
             closing = BillingCalendar.NextClosing(closing, closingDay))
        {
            configured.Add(closing);
        }

        // Declared statements win over the configured calendar. Only ones that
        // already closed can be declared, so they never invent a future closing.
        var replaced = new Dictionary<DateOnly, DeclaredStatement>();
        var extra = new List<DeclaredStatement>();
        foreach (var statement in declared.Where(d => d.ClosingDate <= today).OrderBy(d => d.ClosingDate))
        {
            var nearest = configured
                .Where(c => !replaced.ContainsKey(c)
                            && Math.Abs(c.DayNumber - statement.ClosingDate.DayNumber) <= DeclaredClosingToleranceDays)
                .OrderBy(c => Math.Abs(c.DayNumber - statement.ClosingDate.DayNumber))
                .Cast<DateOnly?>()
                .FirstOrDefault();

            if (nearest is { } match)
            {
                replaced[match] = statement;
            }
            else
            {
                extra.Add(statement);
            }
        }

        var points = configured
            .Select(c => replaced.TryGetValue(c, out var d)
                ? (Closing: d.ClosingDate, Due: d.DueDate, Declared: (DeclaredStatement?)d)
                : (Closing: c, Due: BillingCalendar.DueDateFor(c, closingDay, dueDay), Declared: null))
            .Concat(extra.Select(d => (Closing: d.ClosingDate, Due: d.DueDate, Declared: (DeclaredStatement?)d)))
            .GroupBy(p => p.Closing)
            .Select(g => g.OrderByDescending(p => p.Declared is not null).First())
            .OrderBy(p => p.Closing)
            .ToList();

        // There is always an open cycle after today (a declared closing ON today
        // closes that cycle, so the next one becomes the open one).
        if (!points.Any(p => p.Closing > today || (p.Closing == today && p.Declared is null)))
        {
            var next = BillingCalendar.NextClosing(points[^1].Closing, closingDay);
            points.Add((next, BillingCalendar.DueDateFor(next, closingDay, dueDay), null));
        }

        var result = new List<TimelineEntry>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var start = i == 0
                ? BillingCalendar.PreviousClosing(points[i].Closing, closingDay).AddDays(1)
                : points[i - 1].Closing.AddDays(1);
            if (start > points[i].Closing)
            {
                start = points[i].Closing;
            }

            result.Add(new TimelineEntry(new BillingCycle(start, points[i].Closing, points[i].Due), points[i].Declared));
        }

        return result;
    }

    private static (decimal Charges, decimal Credits) ChargesAndCredits(IEnumerable<CardMovement> movements, BillingCycle cycle)
    {
        var inCycle = movements.Where(m => cycle.Contains(m.Date)).ToList();
        return (
            MoneyMath.Round(inCycle.Where(m => m.Direction == TransactionDirection.Expense).Sum(m => m.Amount)),
            MoneyMath.Round(inCycle.Where(m => m.Direction == TransactionDirection.Income).Sum(m => m.Amount)));
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;
}
