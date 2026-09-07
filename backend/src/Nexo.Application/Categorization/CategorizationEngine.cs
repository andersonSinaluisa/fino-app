using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Domain.Categories;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Categorization;

public sealed record CategorySuggestion(Guid CategoryId, string RuleSource, int Priority);

public interface ICategorizationEngine
{
    /// <summary>Loads the rule set once so a whole import can be categorised without extra queries.</summary>
    Task<ICategorizationSession> StartSessionAsync(Guid userId, CancellationToken cancellationToken);
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

    private sealed class Session(
        IReadOnlyList<CategorizationRule> rules,
        Guid incomeFallback,
        Guid otherFallback) : ICategorizationSession
    {
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
            // (2) how many match dimensions it pins down, (3) the longer match text
            // as a final, deterministic tiebreak. Not first-match-wins: a movement
            // routinely satisfies more than one rule (see the class doc's UBER example).
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

            return best is null
                ? null
                : new CategorySuggestion(best.CategoryId, best.IsSystem ? "system_rule" : "user_rule", best.Priority);
        }

        private static bool IsMoreSpecific(CategorizationRule candidate, CategorizationRule current) =>
            candidate.Specificity > current.Specificity
            || (candidate.Specificity == current.Specificity && candidate.MatchTextLength > current.MatchTextLength);

        public Guid FallbackCategoryId(TransactionDirection direction) =>
            direction == TransactionDirection.Income ? incomeFallback : otherFallback;
    }
}
