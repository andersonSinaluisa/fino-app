using Nexo.Domain.Common;

namespace Nexo.Domain.Budgets;

/// <summary>
/// Everything the app shows about one budget window. Computed, never stored.
/// </summary>
public sealed record BudgetProgress(
    decimal Amount,
    /// <summary>Net spending in the window: expenses minus refunds, never below zero.</summary>
    decimal Spent,
    /// <summary>What is left to spend; zero once exceeded.</summary>
    decimal Remaining,
    /// <summary>How far past the amount the person went; zero until exceeded.</summary>
    decimal Overspent,
    /// <summary>Spent / Amount × 100, one decimal.</summary>
    decimal PercentUsed,
    BudgetUsageLevel Level,
    /// <summary>
    /// The part of <see cref="Remaining"/> that feeds Comprometido: only when the
    /// budget reserves funds, is active and the window is the CURRENT one. A past
    /// or future window never reserves today's money.
    /// </summary>
    decimal Reserved,
    int DaysInWindow,
    /// <summary>Days of the window already started, today included. 0 for a future window.</summary>
    int DaysElapsed,
    /// <summary>Days left, today included. 0 for a past window.</summary>
    int DaysRemaining,
    /// <summary>Remaining / DaysRemaining, when there is something left and days to spend it in.</summary>
    decimal? DailyAllowance,
    /// <summary>Linear projection of Spent to the end of the window; null until enough of it has passed.</summary>
    decimal? ProjectedSpend,
    bool IsCurrentWindow);

public static class BudgetCalculator
{
    public const decimal AttentionPercent = 70m;
    public const decimal NearLimitPercent = 90m;
    public const decimal ExceededPercent = 100m;

    /// <summary>
    /// Below this many elapsed days a pace projection is noise ("gastaste $40 el
    /// día 1 → vas a gastar $1,200") -- the rule is "mostrar insights solo cuando
    /// los datos permitan calcularlos correctamente".
    /// </summary>
    public const int MinimumDaysForProjection = 5;

    /// <param name="amount">The amount that applied to this window (see <see cref="Budget.AmountFor"/>).</param>
    /// <param name="expenses">Sum of matching expenses in the window (positive).</param>
    /// <param name="refunds">Sum of matching refunds (income in the same spending category) in the window (positive).</param>
    /// <param name="window">The window being evaluated.</param>
    /// <param name="today">The user's LOCAL date.</param>
    /// <param name="reserveFunds">Whether the budget reserves money.</param>
    /// <param name="isActive">A paused budget never reserves.</param>
    public static BudgetProgress Evaluate(
        decimal amount,
        decimal expenses,
        decimal refunds,
        BudgetWindow window,
        DateOnly today,
        bool reserveFunds,
        bool isActive)
    {
        amount = MoneyMath.Round(Math.Max(amount, 0m));
        var spent = MoneyMath.Round(Math.Max(expenses - refunds, 0m));
        var remaining = MoneyMath.Round(Math.Max(amount - spent, 0m));
        var overspent = MoneyMath.Round(Math.Max(spent - amount, 0m));
        var percent = amount == 0m ? 0m : Math.Round(spent / amount * 100m, 1, MidpointRounding.AwayFromZero);

        var isCurrent = window.Contains(today);
        var daysElapsed = today < window.Start
            ? 0
            : Math.Min(today.DayNumber - window.Start.DayNumber + 1, window.Days);
        var daysRemaining = today > window.End
            ? 0
            : Math.Min(window.End.DayNumber - today.DayNumber + 1, window.Days);

        var reserved = reserveFunds && isActive && isCurrent ? remaining : 0m;

        decimal? daily = isCurrent && remaining > 0m && daysRemaining > 0
            ? MoneyMath.Round(remaining / daysRemaining)
            : null;

        decimal? projected = isCurrent && daysElapsed >= Math.Min(MinimumDaysForProjection, window.Days) && daysElapsed > 0
            ? MoneyMath.Round(spent / daysElapsed * window.Days)
            : null;

        return new BudgetProgress(
            amount,
            spent,
            remaining,
            overspent,
            percent,
            LevelFor(percent),
            reserved,
            window.Days,
            daysElapsed,
            daysRemaining,
            daily,
            projected,
            isCurrent);
    }

    public static BudgetUsageLevel LevelFor(decimal percentUsed) => percentUsed switch
    {
        >= ExceededPercent => BudgetUsageLevel.Exceeded,
        >= NearLimitPercent => BudgetUsageLevel.NearLimit,
        >= AttentionPercent => BudgetUsageLevel.Attention,
        _ => BudgetUsageLevel.Normal,
    };

    /// <summary>
    /// What a reserving budget would add to Comprometido right now: the amount
    /// still unspent in its current window. Used by the create/edit preview so it
    /// runs through exactly the same rule as the real calculation.
    /// </summary>
    public static decimal ReservedContribution(decimal amount, decimal expenses, decimal refunds) =>
        MoneyMath.Round(Math.Max(MoneyMath.Round(amount) - MoneyMath.Round(Math.Max(expenses - refunds, 0m)), 0m));
}
