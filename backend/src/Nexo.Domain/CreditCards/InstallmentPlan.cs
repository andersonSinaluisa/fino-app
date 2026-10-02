using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Domain.CreditCards;

/// <summary>Persisted lifecycle of a plan. "Completed" is derived (every installment billed).</summary>
public enum InstallmentPlanStatus
{
    Active = 0,

    /// <summary>Precancelado: the remaining balance became payable at once on <see cref="InstallmentPlan.CancelledOn"/>.</summary>
    Cancelled = 1,
}

/// <summary>Derived state of one installment on a given date.</summary>
public enum InstallmentStatus
{
    /// <summary>Its statement has not closed yet: future debt, not payable now.</summary>
    Upcoming = 0,

    /// <summary>Included in a closed statement (or the plan was cancelled).</summary>
    Billed = 1,
}

/// <summary>
/// Compras diferidas / a cuotas. The purchase itself is ONE normal card movement
/// (Laptop $1,200, Compras): it is a $1,200 expense on the purchase date and the
/// card debt grows by $1,200 that day. The plan only says how that debt becomes
/// payable: $100 per statement over 12 statements.
///
/// <para>So it changes WHEN money must be reserved (Comprometido takes only the
/// installment of the next statement), never HOW MUCH was spent -- the plan can
/// never add a second $1,200 (or twelve $100 expenses) on top of the purchase.</para>
/// </summary>
public sealed class InstallmentPlan : Entity, IUserOwned
{
    public const int MinimumInstallments = 2;

    private readonly List<Installment> _installments = [];

    private InstallmentPlan()
    {
    }

    public Guid UserId { get; private set; }

    public Guid CreditCardId { get; private set; }

    /// <summary>The card purchase being deferred.</summary>
    public Guid TransactionId { get; private set; }

    public decimal OriginalAmount { get; private set; }

    public int NumberOfInstallments { get; private set; }

    /// <summary>Every installment but the last; the last absorbs the rounding cents.</summary>
    public decimal InstallmentAmount { get; private set; }

    /// <summary>
    /// Informative annual rate as printed by the bank. Not applied to any figure:
    /// the interest a bank actually charges reaches Fino as Interest movements on the
    /// statement, so applying a rate here too would count it twice.
    /// </summary>
    public decimal? InterestRate { get; private set; }

    /// <summary>The local purchase date, copied so the plan never needs the movement to know when it started.</summary>
    public DateOnly StartDate { get; private set; }

    public InstallmentPlanStatus Status { get; private set; }

    public DateOnly? CancelledOn { get; private set; }

    public IReadOnlyCollection<Installment> Installments => _installments;

    /// <summary>
    /// Creates the schedule. Installment 1 is billed on the statement that closes on
    /// <paramref name="firstClosingDate"/> (by default the one containing the purchase),
    /// each following one on the next closing.
    /// </summary>
    public static InstallmentPlan Create(
        CreditCard card,
        Transaction purchase,
        DateOnly purchaseDate,
        int numberOfInstallments,
        DateOnly firstClosingDate,
        decimal? interestRate,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(purchase);

        if (purchase.FinancialAccountId != card.FinancialAccountId)
        {
            throw new DomainException("installment_wrong_card", "Esa compra no pertenece a esta tarjeta.");
        }

        if (purchase.CardMovementType != CreditCardMovementType.Purchase || purchase.Direction != TransactionDirection.Expense)
        {
            throw new DomainException("installment_not_purchase", "Solo una compra con tarjeta se puede diferir en cuotas.");
        }

        if (!purchase.CountsTowardsBalance)
        {
            throw new DomainException("installment_not_posted", "Esa compra no está registrada en tu tarjeta.");
        }

        if (numberOfInstallments < MinimumInstallments || numberOfInstallments > CreditCardMovementRules.MaximumInstallments)
        {
            throw new DomainException(
                "installment_invalid_count",
                $"Una compra diferida tiene entre {MinimumInstallments} y {CreditCardMovementRules.MaximumInstallments} cuotas.");
        }

        if (firstClosingDate < purchaseDate)
        {
            throw new DomainException("installment_invalid_start", "La primera cuota no puede cobrarse antes de la compra.");
        }

        if (interestRate is < 0m or > 100m)
        {
            throw new DomainException("installment_invalid_rate", "La tasa de interés debe estar entre 0% y 100%.");
        }

        var plan = new InstallmentPlan
        {
            UserId = card.UserId,
            CreditCardId = card.Id,
            TransactionId = purchase.Id,
            OriginalAmount = purchase.Amount,
            NumberOfInstallments = numberOfInstallments,
            InterestRate = interestRate is { } rate ? Math.Round(rate, 2) : null,
            StartDate = purchaseDate,
            Status = InstallmentPlanStatus.Active,
        };

        plan.InstallmentAmount = SplitEvenly(plan.OriginalAmount, numberOfInstallments)[0];
        plan.BuildSchedule(firstClosingDate, card.ClosingDay, card.PaymentDueDay, now);
        plan.Stamp(now);
        return plan;
    }

    /// <summary>
    /// $1,200 / 12 = $100 each; $100 / 3 = $33.33 + $33.33 + $33.34. Integer cents,
    /// never floating point, and the parts always add up to the original exactly.
    /// </summary>
    public static IReadOnlyList<decimal> SplitEvenly(decimal total, int parts)
    {
        var cents = (long)decimal.Round(total * 100m, 0, MidpointRounding.AwayFromZero);
        var baseCents = cents / parts;
        var result = new decimal[parts];
        for (var i = 0; i < parts; i++)
        {
            result[i] = baseCents / 100m;
        }

        result[parts - 1] = (cents - (baseCents * (parts - 1))) / 100m;
        return result;
    }

    /// <summary>
    /// The card's corte/pago days changed: installments not billed yet move to the new
    /// calendar; billed ones are history and stay where they were.
    /// </summary>
    public void Reschedule(int closingDay, int dueDay, DateOnly today, DateTimeOffset now)
    {
        var pending = _installments.Where(i => i.ClosingDate >= today).OrderBy(i => i.Number).ToList();
        if (pending.Count == 0 || Status == InstallmentPlanStatus.Cancelled)
        {
            return;
        }

        var closing = BillingCalendar.ClosingOnOrAfter(today, closingDay);
        foreach (var installment in pending)
        {
            installment.MoveTo(closing, BillingCalendar.DueDateFor(closing, closingDay, dueDay), now);
            closing = BillingCalendar.NextClosing(closing, closingDay);
        }

        Stamp(now);
    }

    /// <summary>Precancelación: everything not billed yet becomes payable from <paramref name="on"/>.</summary>
    public void Cancel(DateOnly on, DateTimeOffset now)
    {
        if (Status == InstallmentPlanStatus.Cancelled)
        {
            throw new DomainException("installment_already_cancelled", "Este diferido ya fue precancelado.");
        }

        if (on < StartDate)
        {
            throw new DomainException("installment_invalid_cancel", "No se puede precancelar antes de la compra.");
        }

        Status = InstallmentPlanStatus.Cancelled;
        CancelledOn = on;
        Stamp(now);
    }

    public InstallmentPlanSchedule ToSchedule(bool purchaseCounts) =>
        new(Id, StartDate, _installments.OrderBy(i => i.Number).Select(i => new ScheduledInstallment(i.Number, i.Amount, i.ClosingDate, i.DueDate)).ToList(), CancelledOn, purchaseCounts);

    private void BuildSchedule(DateOnly firstClosingDate, int closingDay, int dueDay, DateTimeOffset now)
    {
        _installments.Clear();
        var amounts = SplitEvenly(OriginalAmount, NumberOfInstallments);
        var closing = firstClosingDate;
        for (var i = 0; i < NumberOfInstallments; i++)
        {
            _installments.Add(Installment.Create(this, i + 1, amounts[i], closing, BillingCalendar.DueDateFor(closing, closingDay, dueDay), now));
            closing = BillingCalendar.NextClosing(closing, closingDay);
        }
    }
}

/// <summary>One installment of a plan: how much, and on which statement it is billed.</summary>
public sealed class Installment : Entity, IUserOwned
{
    private Installment()
    {
    }

    public Guid UserId { get; private set; }

    public Guid InstallmentPlanId { get; private set; }

    public int Number { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>The closing date of the statement that bills it (its billing period).</summary>
    public DateOnly ClosingDate { get; private set; }

    public DateOnly DueDate { get; private set; }

    public InstallmentStatus StatusOn(DateOnly today, DateOnly? cancelledOn) =>
        ClosingDate < today || (cancelledOn is { } c && c <= today) ? InstallmentStatus.Billed : InstallmentStatus.Upcoming;

    internal static Installment Create(InstallmentPlan plan, int number, decimal amount, DateOnly closingDate, DateOnly dueDate, DateTimeOffset now)
    {
        var installment = new Installment
        {
            UserId = plan.UserId,
            InstallmentPlanId = plan.Id,
            Number = number,
            Amount = amount,
            ClosingDate = closingDate,
            DueDate = dueDate,
        };
        installment.Stamp(now);
        return installment;
    }

    internal void MoveTo(DateOnly closingDate, DateOnly dueDate, DateTimeOffset now)
    {
        ClosingDate = closingDate;
        DueDate = dueDate;
        Stamp(now);
    }
}

public sealed record ScheduledInstallment(int Number, decimal Amount, DateOnly ClosingDate, DateOnly DueDate);

/// <summary>A plan as the calculator consumes it.</summary>
/// <param name="PurchaseCounts">False when the purchase was ignored/deleted: then the plan defers nothing.</param>
public sealed record InstallmentPlanSchedule(
    Guid PlanId,
    DateOnly StartDate,
    IReadOnlyList<ScheduledInstallment> Installments,
    DateOnly? CancelledOn,
    bool PurchaseCounts)
{
    /// <summary>
    /// Debt that exists on <paramref name="date"/> but is not payable yet: installments
    /// billed on a closing after that date. Zero before the purchase (no debt yet) and
    /// from the cancellation on (everything became payable).
    /// </summary>
    public decimal DeferredAt(DateOnly date)
    {
        if (!PurchaseCounts || date < StartDate || (CancelledOn is { } cancelled && cancelled <= date))
        {
            return 0m;
        }

        return Installments.Where(i => i.ClosingDate > date).Sum(i => i.Amount);
    }
}
