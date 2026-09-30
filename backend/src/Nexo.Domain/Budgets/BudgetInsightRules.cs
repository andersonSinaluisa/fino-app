using System.Globalization;
using Nexo.Domain.Common;

namespace Nexo.Domain.Budgets;

public enum BudgetInsightKind
{
    /// <summary>"Excediste tu presupuesto de Comida por $24."</summary>
    Exceeded,

    /// <summary>"Si mantienes este ritmo podrías superar Comida."</summary>
    PaceWillExceed,

    /// <summary>"Ya utilizaste el 80% de Comida y quedan 12 días."</summary>
    HighUsage,

    /// <summary>"Te quedan $23 diarios hasta terminar el período."</summary>
    DailyAllowance,

    /// <summary>"Este mes gastaste $42 menos en Comida que el anterior."</summary>
    ComparedToPrevious,
}

/// <summary>
/// A deterministic observation about one budget. <see cref="Relevance"/> orders
/// them; only kinds with <see cref="IsHomeWorthy"/> may reach Home, and only the
/// single most relevant one.
/// </summary>
public sealed record BudgetInsight(
    BudgetInsightKind Kind,
    Guid BudgetId,
    string Message,
    /// <summary>
    /// A version without money figures, for when the person has amounts hidden:
    /// "ocultar montos" must hide them in insights too.
    /// </summary>
    string MessageWithoutAmounts,
    int Relevance,
    bool IsHomeWorthy);

/// <summary>
/// Plain rules, no AI: each insight appears only when the numbers behind it are
/// sound (a full comparison window, enough elapsed days for a pace), and wording
/// stays factual -- never alarmist.
/// </summary>
public static class BudgetInsightRules
{
    private static readonly CultureInfo Money = CultureInfo.GetCultureInfo("en-US");

    /// <param name="budgetId">The budget being described.</param>
    /// <param name="name">Its display name.</param>
    /// <param name="priority">Essential budgets win ties for the Home slot.</param>
    /// <param name="progress">Current-window progress.</param>
    /// <param name="previousSpentToSameDay">
    /// Spending in the PREVIOUS window up to the same day offset as today, or null
    /// when there was no previous window. Comparing month-to-date against a whole
    /// previous month would always say "gastaste menos" early in the month.
    /// </param>
    /// <param name="periodNoun">"mes", "semana", "quincena" or "período".</param>
    public static IReadOnlyList<BudgetInsight> For(
        Guid budgetId,
        string name,
        BudgetPriority priority,
        BudgetProgress progress,
        decimal? previousSpentToSameDay,
        string periodNoun)
    {
        var insights = new List<BudgetInsight>();
        if (!progress.IsCurrentWindow)
        {
            return insights;
        }

        var tie = priority switch
        {
            BudgetPriority.Essential => 2,
            BudgetPriority.Important => 1,
            _ => 0,
        };

        var days = DaysPhrase(progress.DaysRemaining);

        if (progress.Level == BudgetUsageLevel.Exceeded)
        {
            insights.Add(new BudgetInsight(
                BudgetInsightKind.Exceeded,
                budgetId,
                progress.Overspent > 0m
                    ? $"Excediste {name} por {Format(progress.Overspent)}."
                    : $"Llegaste al límite de {name}.",
                $"Llegaste al límite de {name}.",
                400 + tie,
                IsHomeWorthy: true));
            return insights;
        }

        if (progress.ProjectedSpend is { } projected
            && projected > progress.Amount
            && progress.DaysRemaining > 1)
        {
            insights.Add(new BudgetInsight(
                BudgetInsightKind.PaceWillExceed,
                budgetId,
                $"Si mantienes este ritmo podrías superar {name} antes de que termine el {periodNoun}.",
                $"Si mantienes este ritmo podrías superar {name} antes de que termine el {periodNoun}.",
                300 + tie,
                IsHomeWorthy: true));
        }

        if (progress.PercentUsed >= BudgetCalculator.AttentionPercent && progress.DaysRemaining > 0)
        {
            var percent = Math.Floor(progress.PercentUsed).ToString("0", CultureInfo.InvariantCulture);
            insights.Add(new BudgetInsight(
                BudgetInsightKind.HighUsage,
                budgetId,
                $"{name} está al {percent}% y {days}.",
                $"{name} está al {percent}% y {days}.",
                200 + (int)Math.Floor(progress.PercentUsed / 10m) + tie,
                IsHomeWorthy: true));
        }

        if (progress.DailyAllowance is { } daily && progress.DaysRemaining > 1)
        {
            insights.Add(new BudgetInsight(
                BudgetInsightKind.DailyAllowance,
                budgetId,
                $"Te quedan {Format(daily)} diarios en {name} hasta terminar el {periodNoun}.",
                $"Revisa cuánto te queda por día en {name}.",
                100 + tie,
                IsHomeWorthy: false));
        }

        if (previousSpentToSameDay is { } previous && progress.DaysElapsed >= BudgetCalculator.MinimumDaysForProjection)
        {
            var difference = MoneyMath.Round(previous - progress.Spent);
            if (Math.Abs(difference) >= 1m)
            {
                var direction = difference > 0m ? "menos" : "más";
                insights.Add(new BudgetInsight(
                    BudgetInsightKind.ComparedToPrevious,
                    budgetId,
                    $"A esta altura gastaste {Format(Math.Abs(difference))} {direction} en {name} que el {periodNoun} anterior.",
                    $"A esta altura gastaste {direction} en {name} que el {periodNoun} anterior.",
                    50 + tie,
                    IsHomeWorthy: false));
            }
        }

        return insights;
    }

    public static string PeriodNoun(BudgetPeriod period) => period switch
    {
        BudgetPeriod.Weekly => "semana",
        BudgetPeriod.Biweekly => "quincena",
        BudgetPeriod.Monthly => "mes",
        _ => "período",
    };

    private static string DaysPhrase(int daysRemaining) => daysRemaining switch
    {
        <= 1 => "el período termina hoy",
        _ => $"quedan {daysRemaining} días",
    };

    private static string Format(decimal amount) => "$" + MoneyMath.Round(amount).ToString("#,##0.00", Money);
}
