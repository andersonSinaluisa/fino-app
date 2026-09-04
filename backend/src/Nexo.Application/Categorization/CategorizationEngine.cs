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
    CategorySuggestion? Suggest(string normalizedDescription, TransactionDirection direction);

    Guid FallbackCategoryId(TransactionDirection direction);
}

/// <summary>
/// Rules-only categorisation for the MVP: deterministic, explainable and cheap.
/// The interface is the seam where a model-based classifier can be added later
/// without touching the import or email pipelines (roadmap phase 3).
/// </summary>
public sealed class CategorizationEngine(INexoDbContext db) : ICategorizationEngine
{
    public async Task<ICategorizationSession> StartSessionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rules = await db.CategorizationRules
            .AsNoTracking()
            .Where(r => r.IsActive && (r.UserId == null || r.UserId == userId))
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.Pattern.Length)
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
        public CategorySuggestion? Suggest(string normalizedDescription, TransactionDirection direction)
        {
            if (string.IsNullOrWhiteSpace(normalizedDescription))
            {
                return null;
            }

            foreach (var rule in rules)
            {
                if (rule.Matches(normalizedDescription, direction))
                {
                    return new CategorySuggestion(
                        rule.CategoryId,
                        rule.IsSystem ? "system_rule" : "user_rule",
                        rule.Priority);
                }
            }

            return null;
        }

        public Guid FallbackCategoryId(TransactionDirection direction) =>
            direction == TransactionDirection.Income ? incomeFallback : otherFallback;
    }
}
