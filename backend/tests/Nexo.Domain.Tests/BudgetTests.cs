using Nexo.Domain.Budgets;
using Nexo.Domain.Common;
using Xunit;

namespace Nexo.Domain.Tests;

/// <summary>
/// Presupuestos: period arithmetic, the history-preserving amount, progress and
/// levels, the reserved contribution and the deterministic insights.
/// </summary>
public class BudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 17, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static Budget Monthly(decimal amount = 300m, bool reserve = false, DateOnly? start = null) =>
        Budget.Create(User, "Comida", Guid.CreateVersion7(), amount, BudgetPeriod.Monthly, start ?? D(2026, 9, 1), null, true, reserve, BudgetPriority.Important, Now);

    // ---- Periods ---------------------------------------------------------

    [Fact]
    public void A_monthly_budget_starting_on_the_first_is_the_calendar_month()
    {
        var budget = Monthly();

        Assert.Equal(new BudgetWindow(D(2026, 9, 1), D(2026, 9, 30)), budget.WindowContaining(D(2026, 9, 29)));
        Assert.Equal(new BudgetWindow(D(2026, 10, 1), D(2026, 10, 31)), budget.WindowContaining(D(2026, 10, 1)));
        Assert.Equal(new BudgetWindow(D(2027, 2, 1), D(2027, 2, 28)), budget.WindowContaining(D(2027, 2, 14)));
        Assert.Null(budget.WindowContaining(D(2026, 8, 31)));
    }

    [Fact]
    public void A_monthly_budget_anchored_on_the_31st_clamps_to_short_months()
    {
        var budget = Monthly(start: D(2026, 1, 31));

        Assert.Equal(new BudgetWindow(D(2026, 1, 31), D(2026, 2, 27)), budget.WindowContaining(D(2026, 2, 10)));
        Assert.Equal(new BudgetWindow(D(2026, 2, 28), D(2026, 3, 30)), budget.WindowContaining(D(2026, 3, 1)));
        Assert.Equal(new BudgetWindow(D(2026, 3, 31), D(2026, 4, 29)), budget.WindowContaining(D(2026, 3, 31)));
    }

    [Fact]
    public void Weekly_and_biweekly_windows_repeat_from_the_start_date()
    {
        var weekly = BudgetSchedule.WindowContaining(BudgetPeriod.Weekly, true, D(2026, 9, 7), null, D(2026, 9, 29));
        Assert.Equal(new BudgetWindow(D(2026, 9, 28), D(2026, 10, 4)), weekly);

        var biweekly = BudgetSchedule.WindowContaining(BudgetPeriod.Biweekly, true, D(2026, 9, 7), null, D(2026, 9, 29));
        Assert.Equal(new BudgetWindow(D(2026, 9, 21), D(2026, 10, 4)), biweekly);
    }

    [Fact]
    public void A_non_recurring_budget_only_has_its_first_window()
    {
        Assert.NotNull(BudgetSchedule.WindowContaining(BudgetPeriod.Monthly, false, D(2026, 9, 1), null, D(2026, 9, 30)));
        Assert.Null(BudgetSchedule.WindowContaining(BudgetPeriod.Monthly, false, D(2026, 9, 1), null, D(2026, 10, 1)));
    }

    [Fact]
    public void A_custom_budget_is_its_explicit_range()
    {
        var budget = Budget.Create(User, "Viaje", null, 800m, BudgetPeriod.Custom, D(2026, 12, 20), D(2027, 1, 5), true, true, BudgetPriority.Flexible, Now);

        Assert.False(budget.IsRecurring);
        Assert.Equal(new BudgetWindow(D(2026, 12, 20), D(2027, 1, 5)), budget.WindowContaining(D(2027, 1, 1)));
        Assert.Null(budget.WindowContaining(D(2027, 1, 6)));
    }

    [Fact]
    public void A_custom_budget_needs_an_end_date_and_a_positive_amount()
    {
        Assert.Throws<DomainException>(() =>
            Budget.Create(User, "Viaje", null, 800m, BudgetPeriod.Custom, D(2026, 12, 20), null, false, false, BudgetPriority.Flexible, Now));
        Assert.Throws<DomainException>(() =>
            Budget.Create(User, "Comida", null, 0m, BudgetPeriod.Monthly, D(2026, 9, 1), null, true, false, BudgetPriority.Flexible, Now));
        Assert.Throws<DomainException>(() =>
            Budget.Create(User, "Comida", null, 10m, BudgetPeriod.Monthly, D(2026, 9, 1), D(2026, 8, 1), true, false, BudgetPriority.Flexible, Now));
    }

    [Fact]
    public void Recurring_ranges_overlap_whatever_their_period()
    {
        var monthly = BudgetSchedule.ActiveRange(BudgetPeriod.Monthly, true, D(2026, 9, 1), null);
        var weekly = BudgetSchedule.ActiveRange(BudgetPeriod.Weekly, true, D(2026, 12, 1), null);
        var oneOffBefore = BudgetSchedule.ActiveRange(BudgetPeriod.Monthly, false, D(2026, 8, 1), null);

        Assert.True(BudgetSchedule.RangesOverlap(monthly, weekly));
        Assert.False(BudgetSchedule.RangesOverlap(monthly, oneOffBefore));
    }

    [Fact]
    public void Editing_the_amount_in_october_keeps_septembers_amount()
    {
        var budget = Monthly(300m);
        var september = budget.WindowContaining(D(2026, 9, 15))!.Value;
        var october = budget.WindowContaining(D(2026, 10, 15))!.Value;

        budget.Update("Comida", budget.CategoryId, 350m, null, false, BudgetPriority.Important, october.Start, Now.AddDays(10));

        Assert.Equal(300m, budget.AmountFor(september));
        Assert.Equal(350m, budget.AmountFor(october));
        Assert.Equal(350m, budget.Amount);
    }

    [Fact]
    public void Editing_twice_in_the_same_window_keeps_one_revision()
    {
        var budget = Monthly(300m);
        var october = budget.WindowContaining(D(2026, 10, 15))!.Value;

        budget.Update("Comida", budget.CategoryId, 350m, null, false, BudgetPriority.Important, october.Start, Now);
        budget.Update("Comida", budget.CategoryId, 320m, null, false, BudgetPriority.Important, october.Start, Now);

        Assert.Equal(2, budget.Revisions.Count);
        Assert.Equal(320m, budget.AmountFor(october));
    }

    // ---- Progress --------------------------------------------------------

    private static readonly BudgetWindow September = new(D(2026, 9, 1), D(2026, 9, 30));

    [Fact]
    public void Spent_and_remaining_are_derived_from_the_movements()
    {
        var progress = BudgetCalculator.Evaluate(300m, 120m, 0m, September, D(2026, 9, 19), false, true);

        Assert.Equal(120m, progress.Spent);
        Assert.Equal(180m, progress.Remaining);
        Assert.Equal(40m, progress.PercentUsed);
        Assert.Equal(BudgetUsageLevel.Normal, progress.Level);
        Assert.Equal(0m, progress.Reserved);
        Assert.Equal(12, progress.DaysRemaining);
        Assert.Equal(15m, progress.DailyAllowance);
    }

    [Theory]
    [InlineData(69.9, BudgetUsageLevel.Normal)]
    [InlineData(70, BudgetUsageLevel.Attention)]
    [InlineData(89.9, BudgetUsageLevel.Attention)]
    [InlineData(90, BudgetUsageLevel.NearLimit)]
    [InlineData(99.9, BudgetUsageLevel.NearLimit)]
    [InlineData(100, BudgetUsageLevel.Exceeded)]
    [InlineData(250, BudgetUsageLevel.Exceeded)]
    public void Levels_follow_the_thresholds(double percent, BudgetUsageLevel expected) =>
        Assert.Equal(expected, BudgetCalculator.LevelFor((decimal)percent));

    [Fact]
    public void An_exceeded_budget_reports_by_how_much_and_has_nothing_left()
    {
        var progress = BudgetCalculator.Evaluate(300m, 324m, 0m, September, D(2026, 9, 19), true, true);

        Assert.Equal(BudgetUsageLevel.Exceeded, progress.Level);
        Assert.Equal(24m, progress.Overspent);
        Assert.Equal(0m, progress.Remaining);
        Assert.Equal(0m, progress.Reserved);
        Assert.Null(progress.DailyAllowance);
    }

    [Fact]
    public void Refunds_give_back_budget_but_never_below_zero_spent()
    {
        var partial = BudgetCalculator.Evaluate(300m, 120m, 20m, September, D(2026, 9, 19), false, true);
        Assert.Equal(100m, partial.Spent);

        var moreRefundedThanSpent = BudgetCalculator.Evaluate(300m, 10m, 25m, September, D(2026, 9, 19), false, true);
        Assert.Equal(0m, moreRefundedThanSpent.Spent);
        Assert.Equal(300m, moreRefundedThanSpent.Remaining);
    }

    [Fact]
    public void Only_an_active_reserving_budget_in_its_current_window_reserves()
    {
        Assert.Equal(250m, BudgetCalculator.Evaluate(250m, 0m, 0m, September, D(2026, 9, 19), true, true).Reserved);
        Assert.Equal(0m, BudgetCalculator.Evaluate(250m, 0m, 0m, September, D(2026, 9, 19), false, true).Reserved);
        Assert.Equal(0m, BudgetCalculator.Evaluate(250m, 0m, 0m, September, D(2026, 9, 19), true, false).Reserved);
        Assert.Equal(0m, BudgetCalculator.Evaluate(250m, 0m, 0m, September, D(2026, 10, 2), true, true).Reserved);
    }

    [Fact]
    public void Paying_a_reserved_bill_releases_the_reserve()
    {
        Assert.Equal(35m, BudgetCalculator.ReservedContribution(35m, 0m, 0m));
        Assert.Equal(0m, BudgetCalculator.ReservedContribution(35m, 35m, 0m));
        Assert.Equal(0m, BudgetCalculator.ReservedContribution(35m, 40m, 0m));
        Assert.Equal(15m, BudgetCalculator.ReservedContribution(35m, 20m, 0m));
    }

    [Fact]
    public void Pace_projection_waits_for_enough_days()
    {
        Assert.Null(BudgetCalculator.Evaluate(300m, 100m, 0m, September, D(2026, 9, 2), false, true).ProjectedSpend);
        Assert.Equal(600m, BudgetCalculator.Evaluate(300m, 100m, 0m, September, D(2026, 9, 5), false, true).ProjectedSpend);
    }

    // ---- Insights --------------------------------------------------------

    [Fact]
    public void High_usage_insight_names_the_budget_and_the_days_left()
    {
        var progress = BudgetCalculator.Evaluate(300m, 240m, 0m, September, D(2026, 9, 19), false, true);
        var insights = BudgetInsightRules.For(Guid.CreateVersion7(), "Comida", BudgetPriority.Important, progress, null, "mes");

        var high = Assert.Single(insights, i => i.Kind == BudgetInsightKind.HighUsage);
        Assert.Equal("Comida está al 80% y quedan 12 días.", high.Message);
        Assert.True(high.IsHomeWorthy);
    }

    [Fact]
    public void Exceeded_insight_hides_the_amount_when_asked()
    {
        var progress = BudgetCalculator.Evaluate(300m, 324m, 0m, September, D(2026, 9, 19), false, true);
        var insight = Assert.Single(BudgetInsightRules.For(Guid.CreateVersion7(), "Comida", BudgetPriority.Important, progress, null, "mes"));

        Assert.Equal("Excediste Comida por $24.00.", insight.Message);
        Assert.DoesNotContain("$", insight.MessageWithoutAmounts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_calm_budget_gets_no_alarm()
    {
        var progress = BudgetCalculator.Evaluate(300m, 60m, 0m, September, D(2026, 9, 19), false, true);
        var insights = BudgetInsightRules.For(Guid.CreateVersion7(), "Comida", BudgetPriority.Important, progress, null, "mes");

        Assert.DoesNotContain(insights, i => i.IsHomeWorthy);
    }

    [Fact]
    public void Comparison_with_the_previous_period_uses_the_same_point_in_time()
    {
        var progress = BudgetCalculator.Evaluate(300m, 80m, 0m, September, D(2026, 9, 19), false, true);
        var insights = BudgetInsightRules.For(Guid.CreateVersion7(), "Comida", BudgetPriority.Important, progress, 122m, "mes");

        var comparison = Assert.Single(insights, i => i.Kind == BudgetInsightKind.ComparedToPrevious);
        Assert.Equal("A esta altura gastaste $42.00 menos en Comida que el mes anterior.", comparison.Message);
    }

    [Fact]
    public void Past_windows_produce_no_insights()
    {
        var progress = BudgetCalculator.Evaluate(300m, 400m, 0m, September, D(2026, 10, 5), false, true);
        Assert.Empty(BudgetInsightRules.For(Guid.CreateVersion7(), "Comida", BudgetPriority.Important, progress, null, "mes"));
    }
}
