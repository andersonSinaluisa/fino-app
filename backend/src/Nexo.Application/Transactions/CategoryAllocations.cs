using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Transactions;

/// <summary>
/// One "category line" of money: a whole movement when it is not divided, or one
/// part of a divided movement. Amount is always a positive magnitude, as on
/// <see cref="Transaction.Amount"/>.
/// </summary>
public sealed class CategoryAllocation
{
    public Guid TransactionId { get; init; }

    public Guid? CategoryId { get; init; }

    public decimal Amount { get; init; }

    public TransactionDirection Direction { get; init; }

    public DateTimeOffset TransactionDate { get; init; }

    /// <summary>
    /// Tarjetas de crédito: a credit that gives back money spent in this category
    /// (devolución de una compra con tarjeta). Subtracts from the category's spending
    /// and is never income.
    /// </summary>
    public bool IsRefund { get; init; }

    /// <summary>What this line adds to its category's spending: + for an expense, − for a refund.</summary>
    public decimal SpendingAmount => Direction == TransactionDirection.Expense ? Amount : -Amount;
}

/// <summary>
/// Tarjetas de crédito: THE rule for "is this movement income, spending, or neither".
/// Every income/expense total goes through here instead of summing Amount by
/// Direction directly:
/// <list type="bullet">
/// <item><b>Neither</b>: confirmed transfers between own accounts AND card payments,
/// cash advances and adjustments (all flagged <see cref="Transaction.IsInternalTransfer"/>).
/// Paying the card is moving your own money -- counting it would add the same
/// purchase twice ($100 purchase + $100 payment = $200).</item>
/// <item><b>Spending</b>: every other expense (card purchases, interest and fees included)
/// MINUS card refunds.</item>
/// <item><b>Income</b>: every other income, card refunds excluded.</item>
/// </list>
/// </summary>
public static class MoneyFlows
{
    /// <summary>Movements that are income or spending (status filters stay with the caller).</summary>
    public static IQueryable<Transaction> Countable(IQueryable<Transaction> movements) =>
        movements.Where(t => !t.IsInternalTransfer);

    /// <summary>Movements that add to (expenses) or give back (card refunds) spending.</summary>
    public static IQueryable<Transaction> Spending(IQueryable<Transaction> movements) =>
        movements.Where(t => !t.IsInternalTransfer
                             && (t.Direction == TransactionDirection.Expense
                                 || t.CardMovementType == CreditCardMovementType.Refund));

    /// <summary>Income: never a transfer, never a card refund.</summary>
    public static IQueryable<Transaction> Income(IQueryable<Transaction> movements) =>
        movements.Where(t => !t.IsInternalTransfer
                             && t.Direction == TransactionDirection.Income
                             && t.CardMovementType != CreditCardMovementType.Refund);

    /// <summary>Income and net spending of an already scoped query (user, dates, status, accounts).</summary>
    public static async Task<(decimal Income, decimal Expense)> SumAsync(
        IQueryable<Transaction> scoped,
        CancellationToken cancellationToken)
    {
        var income = await Income(scoped)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var expenses = await Countable(scoped)
            .Where(t => t.Direction == TransactionDirection.Expense)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var refunds = await Countable(scoped)
            .Where(t => t.Direction == TransactionDirection.Income && t.CardMovementType == CreditCardMovementType.Refund)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        // A refund larger than the period's purchases (the purchase was last month)
        // does not make spending negative.
        return (MoneyMath.Round(income), MoneyMath.Round(Math.Max(expenses - refunds, 0m)));
    }
}

/// <summary>
/// Movimientos divididos: THE rule for "how much of which category". Every
/// per-category figure (Gastos por categoría, Estadísticas, insights, pulsos,
/// presupuestos) must go through here instead of grouping
/// <see cref="Transaction.CategoryId"/> directly:
/// <list type="bullet">
/// <item>A movement WITHOUT splits contributes its own CategoryId and Amount.</item>
/// <item>A movement WITH splits contributes each split -- and never itself.</item>
/// </list>
/// So a -$220 movement divided into Esposa $70 + Comida $150 yields exactly those
/// two lines (total $220), never Comida $220 on top of them ($440).
///
/// Totals that are NOT per category (income, expenses, balance) keep reading
/// Transaction.Amount directly: dividing a movement never changes them.
/// </summary>
public static class CategoryAllocations
{
    /// <summary>
    /// SQL-side expansion (UNION ALL). <paramref name="movements"/> must already
    /// carry every filter the caller wants (user, dates, status, internal
    /// transfers, direction); the split side re-uses that same query, so both
    /// halves always describe the same set of movements.
    /// </summary>
    public static IQueryable<CategoryAllocation> Expand(IQueryable<Transaction> movements, INexoDbContext db)
    {
        var whole = movements
            .Where(t => !t.IsSplit)
            .Select(t => new CategoryAllocation
            {
                TransactionId = t.Id,
                CategoryId = t.CategoryId,
                Amount = t.Amount,
                Direction = t.Direction,
                TransactionDate = t.TransactionDate,
                IsRefund = t.CardMovementType == CreditCardMovementType.Refund,
            });

        var parts =
            from s in db.TransactionSplits
            join t in movements on s.TransactionId equals t.Id
            where t.IsSplit
            select new CategoryAllocation
            {
                TransactionId = t.Id,
                CategoryId = s.CategoryId,
                Amount = s.Amount,
                Direction = t.Direction,
                TransactionDate = t.TransactionDate,
                IsRefund = t.CardMovementType == CreditCardMovementType.Refund,
            };

        return whole.Concat(parts);
    }

    /// <summary>
    /// The splits of a set of already-loaded divided movements, grouped by movement,
    /// for the engines that work in memory (insights, pulsos).
    /// </summary>
    public static async Task<ILookup<Guid, (Guid? CategoryId, decimal Amount)>> LoadSplitsAsync(
        INexoDbContext db,
        IQueryable<Transaction> movements,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from s in db.TransactionSplits.AsNoTracking()
                join t in movements on s.TransactionId equals t.Id
                where t.IsSplit
                select new { s.TransactionId, s.CategoryId, s.Amount })
            .ToListAsync(cancellationToken);

        return rows.ToLookup(r => r.TransactionId, r => (r.CategoryId, r.Amount));
    }

    /// <summary>
    /// In-memory expansion: every item whose id has splits is replaced by one copy
    /// per split (with that split's category and amount); every other item passes
    /// through unchanged. Use it ONLY for per-category grouping -- counting
    /// movements or finding the largest one must keep the unexpanded list.
    /// </summary>
    public static IEnumerable<T> ExpandInMemory<T>(
        IEnumerable<T> items,
        Func<T, Guid> idOf,
        ILookup<Guid, (Guid? CategoryId, decimal Amount)> splits,
        Func<T, Guid?, decimal, T> withCategoryAndAmount)
    {
        foreach (var item in items)
        {
            var parts = splits[idOf(item)];
            var any = false;
            foreach (var (categoryId, amount) in parts)
            {
                any = true;
                yield return withCategoryAndAmount(item, categoryId, amount);
            }

            if (!any)
            {
                yield return item;
            }
        }
    }
}
