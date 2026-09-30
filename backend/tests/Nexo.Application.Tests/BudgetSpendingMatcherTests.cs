using Nexo.Application.Budgets;
using Nexo.Domain.Budgets;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Application.Tests;

/// <summary>
/// Presupuestos: which movement belongs to which budget. The general budget must
/// never consume a movement a category budget already tracks.
/// </summary>
public class BudgetSpendingMatcherTests
{
    private static readonly Guid Food = Guid.CreateVersion7();
    private static readonly Guid Transport = Guid.CreateVersion7();
    private static readonly Guid Salary = Guid.CreateVersion7();
    private static readonly BudgetWindow September = new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
    private static readonly IReadOnlySet<Guid> IncomeCategories = new HashSet<Guid> { Salary };

    private static SpendingRow Row(Guid? category, TransactionDirection direction, decimal amount, int day = 10, int month = 9) =>
        new(Guid.CreateVersion7(), category, direction, amount, new DateOnly(2026, month, day));

    private static readonly SpendingRow[] Rows =
    [
        Row(Food, TransactionDirection.Expense, 42m),
        Row(Food, TransactionDirection.Expense, 18m),
        Row(Food, TransactionDirection.Income, 5m),         // refund
        Row(Food, TransactionDirection.Expense, 99m, 31, 8), // outside the window
        Row(Transport, TransactionDirection.Expense, 20m),
        Row(null, TransactionDirection.Expense, 7m),
        Row(null, TransactionDirection.Income, 1000m),       // uncategorized income: never a refund
        Row(Salary, TransactionDirection.Income, 900m),
    ];

    [Fact]
    public void A_category_budget_sums_its_expenses_and_subtracts_its_refunds()
    {
        var match = BudgetSpendingMatcher.Match(Food, September, Rows, IncomeCategories, (_, _) => false);

        Assert.Equal(60m, match.Expenses);
        Assert.Equal(5m, match.Refunds);
        Assert.Equal(3, match.TransactionIds.Count);
    }

    [Fact]
    public void The_general_budget_skips_categories_tracked_by_their_own_budget()
    {
        var match = BudgetSpendingMatcher.Match(null, September, Rows, IncomeCategories, (category, _) => category == Food);

        // Transport 20 + uncategorized 7. Food belongs to its own budget; income
        // (salary, uncategorized) is never spending nor a refund here.
        Assert.Equal(27m, match.Expenses);
        Assert.Equal(0m, match.Refunds);
    }

    [Fact]
    public void The_general_budget_never_treats_income_as_a_refund()
    {
        var match = BudgetSpendingMatcher.Match(null, September, Rows, IncomeCategories, (_, _) => false);

        Assert.Equal(87m, match.Expenses); // 42 + 18 + 20 + 7
        Assert.Equal(0m, match.Refunds);
    }

    [Fact]
    public void Window_boundaries_are_inclusive_local_dates()
    {
        var rows = new[]
        {
            Row(Food, TransactionDirection.Expense, 1m, 1, 9),
            Row(Food, TransactionDirection.Expense, 2m, 30, 9),
            Row(Food, TransactionDirection.Expense, 4m, 1, 10),
        };

        var match = BudgetSpendingMatcher.Match(Food, September, rows, IncomeCategories, (_, _) => false);
        Assert.Equal(3m, match.Expenses);
    }
}
