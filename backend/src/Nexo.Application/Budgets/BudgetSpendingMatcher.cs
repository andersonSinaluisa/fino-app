using Nexo.Domain.Budgets;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Budgets;

/// <summary>
/// A movement reduced to what budget matching needs. <see cref="LocalDate"/> is the
/// date in the USER's time zone: a $20 dinner at 22:30 on 30 sept in Guayaquil is a
/// September expense, even though it is already 1 oct in UTC.
/// </summary>
public sealed record SpendingRow(
    Guid TransactionId,
    Guid? CategoryId,
    TransactionDirection Direction,
    decimal Amount,
    DateOnly LocalDate,
    bool IsRefund = false);

/// <summary>What one budget window consumed.</summary>
public sealed record SpendingMatch(decimal Expenses, decimal Refunds, IReadOnlyList<Guid> TransactionIds);

/// <summary>
/// THE rule for "which movements belong to which budget". Spent is always derived
/// from here, never stored, and the "Movimientos de este presupuesto" list shows
/// exactly the ids returned here -- so the list and the total cannot disagree.
///
/// Callers must pass only movements that count as real money flow: Posted or
/// Pending, and NOT internal transfers (moving money between your own accounts is
/// not spending). Beyond that:
/// <list type="bullet">
/// <item><b>Category budget</b>: expenses in the category add; income in the same
/// (spending) category is a refund/reversal and subtracts.</item>
/// <item><b>General budget</b> (no category): every expense whose category is NOT
/// tracked by an active category budget on that date, uncategorized included.
/// It never takes ordinary income as a refund: without a category nothing proves an
/// incoming movement (a salary, a transfer from a relative) gives back money that
/// was spent. The one exception is a credit-card refund (<see cref="SpendingRow.IsRefund"/>):
/// the card itself says it is money given back for a purchase.</item>
/// </list>
/// Combined with the backend's overlap rule (one active budget per category per
/// date range, one active general budget per date range), every movement is
/// consumed by at most one active budget.
/// </summary>
public static class BudgetSpendingMatcher
{
    public static SpendingMatch Match(
        Guid? categoryId,
        BudgetWindow window,
        IEnumerable<SpendingRow> rows,
        IReadOnlySet<Guid> incomeCategoryIds,
        Func<Guid, DateOnly, bool> isTrackedByCategoryBudget)
    {
        ArgumentNullException.ThrowIfNull(rows);

        decimal expenses = 0m;
        decimal refunds = 0m;
        var ids = new List<Guid>();

        foreach (var row in rows)
        {
            if (!window.Contains(row.LocalDate) || row.Amount <= 0m)
            {
                continue;
            }

            if (!Belongs(categoryId, row, incomeCategoryIds, isTrackedByCategoryBudget))
            {
                continue;
            }

            if (row.Direction == TransactionDirection.Expense)
            {
                expenses += row.Amount;
            }
            else
            {
                refunds += row.Amount;
            }

            ids.Add(row.TransactionId);
        }

        // A divided movement can contribute more than one row; it is still one movement.
        return new SpendingMatch(expenses, refunds, ids.Distinct().ToList());
    }

    private static bool Belongs(
        Guid? categoryId,
        SpendingRow row,
        IReadOnlySet<Guid> incomeCategoryIds,
        Func<Guid, DateOnly, bool> isTrackedByCategoryBudget)
    {
        if (categoryId is { } budgetCategory)
        {
            return row.CategoryId == budgetCategory;
        }

        if (row.Direction != TransactionDirection.Expense && !row.IsRefund)
        {
            return false;
        }

        if (row.CategoryId is not { } rowCategory)
        {
            return true;
        }

        if (incomeCategoryIds.Contains(rowCategory))
        {
            return false;
        }

        return !isTrackedByCategoryBudget(rowCategory, row.LocalDate);
    }
}
