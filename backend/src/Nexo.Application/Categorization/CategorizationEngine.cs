using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Domain.Categories;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Categorization;

public sealed record CategorySuggestion(Guid CategoryId, Guid RuleId, string RuleSource, int Priority);

public interface ICategorizationEngine
{
    /// <summary>Loads the rule set once so a whole import can be categorised without extra queries.</summary>
    Task<ICategorizationSession> StartSessionAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// "Categorización personal": one bulk update for every rule a session actually
    /// matched, so an import of thousands of rows costs one extra query total
    /// instead of one per row (see point 20, "performance"). Call once after the
    /// import/email loop that used <paramref name="session"/> is done.
    /// </summary>
    Task RecordHitsAsync(ICategorizationSession session, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface ICategorizationSession
{
    /// <summary>
    /// <paramref name="normalizedMerchant"/> is the transaction's own automatic
    /// merchant guess (normalized), never the person's display correction --
    /// matching stays tied to what the raw bank data actually says, the same way a
    /// merchant correction never rewrites the original description (Entregable 12).
    /// </summary>
    CategorySuggestion? Suggest(
        string normalizedDescription,
        string? normalizedMerchant,
        string providerCode,
        decimal amount,
        TransactionDirection direction);

    Guid FallbackCategoryId(TransactionDirection direction);

    /// <summary>Rule id -> number of movements it won in this session, for <see cref="ICategorizationEngine.RecordHitsAsync"/>.</summary>
    IReadOnlyDictionary<Guid, int> Hits { get; }
}

/// <summary>
/// Rules-only categorisation for the MVP: deterministic, explainable and cheap.
/// The interface is the seam where a model-based classifier can be added later
/// without touching the import or email pipelines (roadmap phase 3).
///
/// Entregable 14 ("Categorización v2"): a movement can match several rules at
/// once (e.g. both "UBER" and "UBER EATS" contain-match a transaction whose
/// merchant is "Uber Eats"). Resolution is "most specific wins", not "first
/// found wins" -- see the ordering in <see cref="Session.Suggest"/>.
/// </summary>
public sealed class CategorizationEngine(INexoDbContext db) : ICategorizationEngine
{
    public async Task<ICategorizationSession> StartSessionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rules = await db.CategorizationRules
            .AsNoTracking()
            .Where(r => r.IsActive && (r.UserId == null || r.UserId == userId))
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);

        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .Select(c => new { c.Id, c.Code })
            .ToListAsync(cancellationToken);

        var byCode = categories.ToDictionary(c => c.Code, c => c.Id, StringComparer.Ordinal);

        var incomeFallback = byCode.TryGetValue(CategoryCodes.Income, out var income) ? income : Guid.Empty;
        var otherFallback = byCode.TryGetValue(CategoryCodes.Other, out var other) ? other : Guid.Empty;

        return new Session(rules, incomeFallback, otherFallback);
    }

    /// <summary>
    /// Point 20 ("performance"): one query for every rule this session actually
    /// matched, never one write per movement. Rules that never fired are untouched.
    /// </summary>
    public async Task RecordHitsAsync(ICategorizationSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (session.Hits.Count == 0)
        {
            return;
        }

        var ruleIds = session.Hits.Keys.ToArray();
        var tracked = await db.CategorizationRules
            .Where(r => ruleIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        foreach (var rule in tracked)
        {
            var count = session.Hits[rule.Id];
            for (var i = 0; i < count; i++)
            {
                rule.RegisterHit(now);
            }
        }
    }

    private sealed class Session(
        IReadOnlyList<CategorizationRule> rules,
        Guid incomeFallback,
        Guid otherFallback) : ICategorizationSession
    {
        private readonly Dictionary<Guid, int> _hits = new();

        public IReadOnlyDictionary<Guid, int> Hits => _hits;

        public CategorySuggestion? Suggest(
            string normalizedDescription,
            string? normalizedMerchant,
            string providerCode,
            decimal amount,
            TransactionDirection direction)
        {
            if (string.IsNullOrWhiteSpace(normalizedDescription) && string.IsNullOrWhiteSpace(normalizedMerchant))
            {
                return null;
            }

            // "La regla más específica gana": collect every rule that matches, then
            // pick by (1) priority tier -- a personal rule always beats a system one,
            // (2) how many match dimensions it pins down, (3) the longer match text,
            // (4) the most recently created rule, as a final fully-deterministic
            // tiebreak that never depends on the order Postgres happens to return
            // rows in (point 12, "no depender del orden accidental"). Not
            // first-match-wins: a movement routinely satisfies more than one rule
            // (see the class doc's UBER example).
            CategorizationRule? best = null;

            foreach (var rule in rules)
            {
                if (!rule.Matches(normalizedDescription, normalizedMerchant, providerCode, amount, direction))
                {
                    continue;
                }

                if (best is null
                    || rule.Priority < best.Priority
                    || (rule.Priority == best.Priority && IsMoreSpecific(rule, best)))
                {
                    best = rule;
                }
            }

            if (best is null)
            {
                return null;
            }

            _hits[best.Id] = _hits.GetValueOrDefault(best.Id) + 1;

            return new CategorySuggestion(
                best.CategoryId,
                best.Id,
                best.IsSystem ? "system_rule" : "user_rule",
                best.Priority);
        }

        private static bool IsMoreSpecific(CategorizationRule candidate, CategorizationRule current)
        {
            if (candidate.Specificity != current.Specificity)
            {
                return candidate.Specificity > current.Specificity;
            }

            if (candidate.MatchTextLength != current.MatchTextLength)
            {
                return candidate.MatchTextLength > current.MatchTextLength;
            }

            // Final desempate: the more recently created rule wins (point 12.4).
            return candidate.CreatedAt > current.CreatedAt;
        }

        public Guid FallbackCategoryId(TransactionDirection direction) =>
            direction == TransactionDirection.Income ? incomeFallback : otherFallback;
    }
}
