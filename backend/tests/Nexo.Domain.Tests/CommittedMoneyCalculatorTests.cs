using Nexo.Domain.Budgets;
using Xunit;

namespace Nexo.Domain.Tests;

/// <summary>
/// Presupuestos / Comprometido: the single rule Disponible = Tu dinero − Comprometido,
/// floored at zero, and the de-duplication that keeps the same money from being
/// counted twice when two sources describe it.
/// </summary>
public class CommittedMoneyCalculatorTests
{
    private static readonly Guid Internet = Guid.CreateVersion7();
    private static readonly Guid Rent = Guid.CreateVersion7();

    private static CommitmentCandidate Upcoming(string key, decimal amount, Guid? category = null) =>
        new(CommitmentSource.UpcomingPayment, key, key, category, amount);

    private static CommitmentCandidate Reserved(string key, decimal amount, Guid? category = null) =>
        new(CommitmentSource.ReservedBudget, key, key, category, amount, Guid.CreateVersion7());

    [Fact]
    public void Available_is_current_money_minus_committed_with_both_sources()
    {
        var result = CommittedMoneyCalculator.Calculate(668.89m,
        [
            Upcoming("merchant:NETFLIX", 52.59m),
            Reserved("alquiler", 45.00m, Rent),
        ]);

        Assert.Equal(668.89m, result.CurrentMoney);
        Assert.Equal(97.59m, result.Committed);
        Assert.Equal(571.30m, result.Available);
        Assert.Equal(0m, result.Overcommitted);
        Assert.False(result.IsOvercommitted);
        Assert.Equal(52.59m, result.TotalFor(CommitmentSource.UpcomingPayment));
        Assert.Equal(45.00m, result.TotalFor(CommitmentSource.ReservedBudget));
    }

    [Fact]
    public void Committed_above_the_balance_keeps_its_real_value_and_available_never_goes_negative()
    {
        var result = CommittedMoneyCalculator.Calculate(100m, [Reserved("alquiler", 150m, Rent)]);

        Assert.Equal(150m, result.Committed);
        Assert.Equal(0m, result.Available);
        Assert.Equal(50m, result.Overcommitted);
        Assert.True(result.IsOvercommitted);
    }

    [Fact]
    public void A_negative_balance_is_never_reported_as_negative_available()
    {
        var result = CommittedMoneyCalculator.Calculate(-20m, [Reserved("alquiler", 10m, Rent)]);

        Assert.Equal(0m, result.Available);
        Assert.Equal(10m, result.Overcommitted);
    }

    [Fact]
    public void Upcoming_payment_and_reserved_budget_for_the_same_obligation_count_once()
    {
        var result = CommittedMoneyCalculator.Calculate(500m,
        [
            Upcoming("merchant:CNT", 35m, Internet),
            Reserved("internet", 35m, Internet),
        ]);

        Assert.Equal(35m, result.Committed);
        Assert.Equal(465m, result.Available);

        var payment = Assert.Single(result.Lines, l => l.Source == CommitmentSource.UpcomingPayment);
        Assert.Equal(35m, payment.GrossAmount);
        Assert.Equal(0m, payment.CountedAmount);
        Assert.Equal("internet", payment.CoveredByReferenceKey);
    }

    [Fact]
    public void Only_the_part_of_a_payment_that_exceeds_the_reserve_is_added()
    {
        // Reserve $35 for Internet but the bill Fino keeps seeing is $50: $50 will
        // leave, not $85 and not $35.
        var result = CommittedMoneyCalculator.Calculate(500m,
        [
            Upcoming("merchant:CNT", 50m, Internet),
            Reserved("internet", 35m, Internet),
        ]);

        Assert.Equal(50m, result.Committed);
        Assert.Equal(15m, Assert.Single(result.Lines, l => l.Source == CommitmentSource.UpcomingPayment).CountedAmount);
    }

    [Fact]
    public void A_reserve_absorbs_several_payments_of_its_category_until_it_runs_out()
    {
        var result = CommittedMoneyCalculator.Calculate(500m,
        [
            Reserved("servicios", 40m, Internet),
            Upcoming("merchant:A", 30m, Internet),
            Upcoming("merchant:B", 30m, Internet),
        ]);

        // max(40, 60) = 60.
        Assert.Equal(60m, result.Committed);
    }

    [Fact]
    public void Payments_of_other_categories_or_without_category_are_never_absorbed()
    {
        var result = CommittedMoneyCalculator.Calculate(500m,
        [
            Reserved("internet", 35m, Internet),
            Upcoming("merchant:NETFLIX", 10m, Rent),
            Upcoming("merchant:GYM", 20m),
        ]);

        Assert.Equal(65m, result.Committed);
    }

    [Fact]
    public void The_same_candidate_reported_twice_is_counted_once()
    {
        var result = CommittedMoneyCalculator.Calculate(500m,
        [
            Upcoming("merchant:CNT", 35m),
            Upcoming("merchant:CNT", 35m),
        ]);

        Assert.Equal(35m, result.Committed);
    }

    [Fact]
    public void Zero_or_negative_candidates_are_ignored()
    {
        var result = CommittedMoneyCalculator.Calculate(500m,
        [
            Reserved("comida", 0m),
            Upcoming("merchant:X", -3m),
        ]);

        Assert.Equal(0m, result.Committed);
        Assert.Equal(500m, result.Available);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Cents_add_up_exactly()
    {
        var result = CommittedMoneyCalculator.Calculate(0.3m,
        [
            Reserved("a", 0.1m),
            Reserved("b", 0.2m),
        ]);

        Assert.Equal(0.3m, result.Committed);
        Assert.Equal(0m, result.Available);
        Assert.Equal(0m, result.Overcommitted);
    }
}
