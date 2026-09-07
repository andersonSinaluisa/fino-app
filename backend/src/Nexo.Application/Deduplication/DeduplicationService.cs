using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Deduplication;

/// <summary>
/// Loads the smallest useful candidate window from the database and hands it to the
/// pure matcher. The window is bounded by account and by date, so the cost of an
/// import does not grow with the size of the user's history.
/// </summary>
public sealed class DeduplicationService(
    INexoDbContext db,
    IOptions<DeduplicationOptions> options) : IDeduplicationService
{
    private readonly DeduplicationOptions _options = options.Value;

    public async Task<DuplicateCheckResult> CheckAsync(
        Guid userId,
        IncomingMovement movement,
        CancellationToken cancellationToken)
    {
        var results = await CheckBatchAsync(userId, [movement], cancellationToken);
        return results[0];
    }

    public async Task<IReadOnlyList<DuplicateCheckResult>> CheckBatchAsync(
        Guid userId,
        IReadOnlyList<IncomingMovement> movements,
        CancellationToken cancellationToken)
    {
        if (movements.Count == 0)
        {
            return [];
        }

        var accountIds = movements.Select(m => m.FinancialAccountId).Distinct().ToArray();
        var window = TimeSpan.FromDays(_options.DateWindowDays + 1);
        var from = movements.Min(m => m.TransactionDate) - window;
        var to = movements.Max(m => m.TransactionDate) + window;

        // Not `accountIds.Contains(t.FinancialAccountId)`: combined with the global
        // per-user filter, that shape does not translate on the SQLite provider our
        // integration tests run against (see QueryableGuidExtensions.WhereIdIn for
        // the full explanation — this was the root cause of every import failing).
        var stored = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TransactionDate >= from
                        && t.TransactionDate <= to)
            .WhereIdIn(t => t.FinancialAccountId, accountIds)
            .Select(t => new DeduplicationMatcher.ExistingMovement(
                t.Id,
                t.FinancialAccountId,
                t.TransactionDate,
                t.Amount,
                t.Direction,
                t.ExternalReference,
                t.NormalizedDescription,
                t.Fingerprint,
                t.Status))
            .ToListAsync(cancellationToken);

        var matcher = new DeduplicationMatcher(_options);
        var results = new List<DuplicateCheckResult>(movements.Count);

        // Incoming rows are matched only against what is already stored, never
        // against each other: a statement lists every movement exactly once, so two
        // identical rows in one file are two real purchases (same coffee shop, same
        // price, same day). Cross-source repeats — the email notification and then
        // the statement line — are what the stored window catches.
        // An existing movement can only absorb one incoming row: once matched it
        // leaves the pool, so a statement listing two identical purchases when only
        // one was already known still imports the second one.
        var pool = new List<DeduplicationMatcher.ExistingMovement>(stored);

        foreach (var movement in movements)
        {
            var result = matcher.Match(movement, pool);
            results.Add(result);

            if (result.IsExact && result.Match is not null)
            {
                pool.RemoveAll(c => c.Id == result.Match.TransactionId);
            }
        }

        // Entregable 28 ("Observabilidad"): a business metric, not a log line --
        // "how often do movements from different sources turn out to be the same
        // one" is exactly the kind of question a log line answers badly and a
        // counter answers well. Grouped once instead of one Add per movement so an
        // import of a thousand rows records at most three measurements.
        foreach (var group in results.GroupBy(r => r.MatchType))
        {
            NexoTelemetry.DeduplicationChecks.Add(
                group.Count(),
                new KeyValuePair<string, object?>("match_type", group.Key.ToString()));
        }

        return results;
    }
}
