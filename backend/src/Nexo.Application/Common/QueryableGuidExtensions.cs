using System.Linq.Expressions;

namespace Nexo.Application.Common;

/// <summary>
/// A translatable replacement for <c>ids.Contains(entity.SomeGuidColumn)</c>.
/// </summary>
/// <remarks>
/// <para>
/// SQLite has no native GUID type: <c>Microsoft.EntityFrameworkCore.Sqlite</c> maps
/// <see cref="Guid"/> columns through its own internal type mapping. When a query
/// composes (a) Nexo's global per-user filter — built in
/// <c>NexoDbContext.ApplyUserOwnedFilters</c> as an <c>OrElse</c> over a member
/// access on the current <c>DbContext</c> instance — with (b) a second
/// <c>Where</c> containing <c>Contains</c> against a client-side array of Guids,
/// the SQLite provider cannot produce a single translatable command for the
/// combined tree. EF Core reports this at query-compile time as "could not be
/// translated"; it is not a runtime data bug and nothing is written or read
/// incorrectly, but the query never runs.
/// </para>
/// <para>
/// This was found the hard way: <c>DeduplicationService.CheckBatchAsync</c> used
/// exactly this shape, so every statement import failed with a 500 as soon as it
/// tried to look up existing movements to compare against. The same shape existed
/// in <c>TransactionService.LoadAccountAliasesAsync</c> (blocks the movements list
/// as soon as any transaction exists) and in <c>PrivacyService</c>'s account
/// deletion (blocks deleting a financial account that has import rows). All three
/// are fixed by this helper — see ADR-009 in docs/architecture.md.
/// </para>
/// <para>
/// Plain equality on a Guid column has never had this problem — it is what every
/// other single-id lookup in this codebase already uses. So instead of an IN-style
/// membership check, this builds an explicit <c>OR</c> chain of equality
/// comparisons, which both providers Nexo targets (SQLite for tests, PostgreSQL in
/// every real environment) translate without issue. The id sets this is used
/// with — one account per import, a page of movements' distinct accounts, the
/// import rows of one deleted account — are always small; this is not meant for
/// filtering by thousands of ids, and a call site that might do that should measure
/// before reusing it.
/// </para>
/// </remarks>
public static class QueryableGuidExtensions
{
    public static IQueryable<T> WhereIdIn<T>(
        this IQueryable<T> source,
        Expression<Func<T, Guid>> idSelector,
        IReadOnlyCollection<Guid> ids)
    {
        var distinctIds = ids.Distinct().ToArray();

        if (distinctIds.Length == 0)
        {
            return source.Where(_ => false);
        }

        var parameter = idSelector.Parameters[0];
        Expression? disjunction = null;

        foreach (var id in distinctIds)
        {
            var equals = Expression.Equal(idSelector.Body, Expression.Constant(id));
            disjunction = disjunction is null ? equals : Expression.OrElse(disjunction, equals);
        }

        var predicate = Expression.Lambda<Func<T, bool>>(disjunction!, parameter);
        return source.Where(predicate);
    }
}
