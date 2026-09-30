using Nexo.Domain.Common;

namespace Nexo.Domain.Budgets;

/// <summary>
/// A plan for part of the person's money.
///
/// Two distinct ideas share this aggregate, switched by <see cref="ReserveFunds"/>:
/// <list type="bullet">
/// <item><b>Límite</b> (ReserveFunds = false): "quiero gastar como mucho $300 en
/// Comida". It only measures spending; it never touches Disponible.</item>
/// <item><b>Reserva</b> (ReserveFunds = true): "estos $250 ya son del alquiler".
/// Whatever is still unspent in the current window counts as Comprometido.</item>
/// </list>
///
/// Nothing derived is stored here: spent, remaining, reserved, committed and
/// available are always recomputed from the movements (see
/// <see cref="BudgetCalculator"/> and <see cref="CommittedMoneyCalculator"/>), so
/// editing or deleting a movement can never leave a stale "Spent" behind.
/// </summary>
public sealed class Budget : Entity, IUserOwned
{
    public const int NameMaxLength = 60;

    private readonly List<BudgetAmountRevision> _revisions = [];

    private Budget()
    {
    }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>
    /// Null = presupuesto general: it tracks every expense that no category budget
    /// active in the same window already tracks (see BudgetService), so a movement
    /// is never consumed by two budgets.
    /// </summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>The CURRENT amount. Past windows read theirs from <see cref="Revisions"/>.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = Common.Currency.Usd;

    public BudgetPeriod Period { get; private set; }

    /// <summary>Local calendar date (user's time zone) on which the first window starts.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Last local date the budget applies. Required for <see cref="BudgetPeriod.Custom"/>.</summary>
    public DateOnly? EndDate { get; private set; }

    public bool IsRecurring { get; private set; }

    public bool ReserveFunds { get; private set; }

    public BudgetPriority Priority { get; private set; }

    /// <summary>False = pausado: kept with its history, but excluded from Comprometido and from the overlap rule.</summary>
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<BudgetAmountRevision> Revisions => _revisions;

    public static Budget Create(
        Guid userId,
        string name,
        Guid? categoryId,
        decimal amount,
        BudgetPeriod period,
        DateOnly startDate,
        DateOnly? endDate,
        bool isRecurring,
        bool reserveFunds,
        BudgetPriority priority,
        DateTimeOffset now,
        string? currency = null)
    {
        DomainException.Require(userId != Guid.Empty, "Un presupuesto necesita un dueño.");
        ValidateSchedule(period, startDate, endDate);

        var budget = new Budget
        {
            UserId = userId,
            Name = DomainException.RequireText(name, nameof(name), NameMaxLength),
            CategoryId = categoryId,
            Amount = RequireAmount(amount),
            Currency = Common.Currency.Normalize(currency),
            Period = period,
            StartDate = startDate,
            EndDate = endDate,
            IsRecurring = period != BudgetPeriod.Custom && isRecurring,
            ReserveFunds = reserveFunds,
            Priority = priority,
            IsActive = true,
        };

        budget._revisions.Add(BudgetAmountRevision.Create(budget.Id, startDate, budget.Amount, now));
        budget.Stamp(now);
        return budget;
    }

    /// <summary>
    /// Changes the definition. <paramref name="effectiveFrom"/> is the start of the
    /// window the edit is made in: a new amount applies to the current window and
    /// every later one, while earlier windows keep the amount they had -- editing
    /// October never rewrites September.
    /// </summary>
    public void Update(
        string name,
        Guid? categoryId,
        decimal amount,
        DateOnly? endDate,
        bool reserveFunds,
        BudgetPriority priority,
        DateOnly effectiveFrom,
        DateTimeOffset now)
    {
        ValidateSchedule(Period, StartDate, endDate);

        Name = DomainException.RequireText(name, nameof(name), NameMaxLength);
        CategoryId = categoryId;
        EndDate = endDate;
        ReserveFunds = reserveFunds;
        Priority = priority;

        var newAmount = RequireAmount(amount);
        if (newAmount != Amount)
        {
            var from = effectiveFrom < StartDate ? StartDate : effectiveFrom;
            var existing = _revisions.FirstOrDefault(r => r.EffectiveFrom == from);
            if (existing is not null)
            {
                existing.Change(newAmount, now);
            }
            else
            {
                _revisions.Add(BudgetAmountRevision.Create(Id, from, newAmount, now));
            }

            // A later revision would silently override the edit the person just made.
            _revisions.RemoveAll(r => r.EffectiveFrom > from);
            Amount = newAmount;
        }

        Stamp(now);
    }

    public void Pause(DateTimeOffset now)
    {
        IsActive = false;
        Stamp(now);
    }

    public void Resume(DateTimeOffset now)
    {
        IsActive = true;
        Stamp(now);
    }

    /// <summary>The window of this budget containing <paramref name="date"/>, or null if it does not apply that day.</summary>
    public BudgetWindow? WindowContaining(DateOnly date) =>
        BudgetSchedule.WindowContaining(Period, IsRecurring, StartDate, EndDate, date);

    public BudgetWindow? PreviousWindow(BudgetWindow window) =>
        BudgetSchedule.Previous(Period, IsRecurring, StartDate, EndDate, window);

    public (DateOnly Start, DateOnly? End) ActiveRange() =>
        BudgetSchedule.ActiveRange(Period, IsRecurring, StartDate, EndDate);

    /// <summary>The amount that applied to a given window (history-preserving).</summary>
    public decimal AmountFor(BudgetWindow window)
    {
        var revision = _revisions
            .Where(r => r.EffectiveFrom <= window.Start)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();

        return revision?.Amount
               ?? _revisions.OrderBy(r => r.EffectiveFrom).FirstOrDefault()?.Amount
               ?? Amount;
    }

    private static decimal RequireAmount(decimal amount)
    {
        var rounded = MoneyMath.Round(amount);
        if (rounded <= 0m)
        {
            throw new DomainException("invalid_budget_amount", "El monto del presupuesto debe ser mayor que cero.");
        }

        if (rounded > 99_999_999m)
        {
            throw new DomainException("invalid_budget_amount", "El monto del presupuesto es demasiado grande.");
        }

        return rounded;
    }

    private static void ValidateSchedule(BudgetPeriod period, DateOnly startDate, DateOnly? endDate)
    {
        if (!Enum.IsDefined(period))
        {
            throw new DomainException("invalid_budget_period", "Período de presupuesto no válido.");
        }

        if (period == BudgetPeriod.Custom && endDate is null)
        {
            throw new DomainException("invalid_budget_period", "Un presupuesto personalizado necesita fecha de fin.");
        }

        if (endDate is { } end && end < startDate)
        {
            throw new DomainException("invalid_budget_period", "La fecha de fin no puede ser anterior a la de inicio.");
        }

        if (period == BudgetPeriod.Custom && endDate is { } customEnd && customEnd.DayNumber - startDate.DayNumber > 366)
        {
            throw new DomainException("invalid_budget_period", "Un presupuesto personalizado puede durar como máximo un año.");
        }
    }
}

/// <summary>
/// "Desde esta fecha, el presupuesto vale X". Kept so that editing the amount in
/// October leaves September's historical amount intact.
/// </summary>
public sealed class BudgetAmountRevision : Entity
{
    private BudgetAmountRevision()
    {
    }

    public Guid BudgetId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public decimal Amount { get; private set; }

    internal static BudgetAmountRevision Create(Guid budgetId, DateOnly effectiveFrom, decimal amount, DateTimeOffset now)
    {
        var revision = new BudgetAmountRevision
        {
            BudgetId = budgetId,
            EffectiveFrom = effectiveFrom,
            Amount = amount,
        };
        revision.Stamp(now);
        return revision;
    }

    internal void Change(decimal amount, DateTimeOffset now)
    {
        Amount = amount;
        Stamp(now);
    }
}
