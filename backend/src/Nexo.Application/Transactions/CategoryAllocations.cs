using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
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
