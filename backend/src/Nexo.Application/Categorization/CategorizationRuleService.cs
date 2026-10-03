using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Categorization;

/// <summary>
/// "Categorización personal": direct management of a user's own rules (the
/// "Reglas de categorización" screen, point 15) plus the impact-preview and
/// bulk-recategorize operations that back both that screen and the "aplicar
/// también a movimientos similares" flow on a single movement
/// (TransactionService.UpdateCategoryAsync).
///
/// Every query here filters by <c>UserId</c> explicitly: <see cref="CategorizationRule"/>
/// has a nullable <c>UserId</c> (system rules have none) so, unlike <c>Transaction</c>,
/// it does NOT get NexoDbContext's automatic per-user query filter -- the isolation
/// has to be right in this file, by hand, every time (point 21, "seguridad
/// multiusuario"). A rule id that exists but belongs to another user is treated
/// exactly like one that does not exist (404, never 403): the caller learns
/// nothing about another user's rules either way.
/// </summary>
public interface ICategorizationRuleService
{
    Task<IReadOnlyList<CategorizationRuleDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<CategorizationRuleDto> CreateAsync(Guid userId, CreateCategorizationRuleRequest request, CancellationToken cancellationToken);

    Task<CategorizationRuleDto> UpdateAsync(Guid userId, Guid ruleId, UpdateCategorizationRuleRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, Guid ruleId, CancellationToken cancellationToken);

    Task<RulePreviewDto> PreviewAsync(Guid userId, RulePreviewRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Point 6/12: the description-based pattern a correction of this movement (to
    /// <paramref name="categoryId"/>) should learn as a rule, and any existing rule
    /// already sitting on that exact pattern. Starts from the single-token
    /// suggestion ("UBER"); if that pattern already belongs, for this user, to a
    /// DIFFERENT category's rule, escalates to a two-token pattern ("UBER EATS")
    /// instead of silently retargeting the broader rule out from under every other
    /// movement it already serves (point 12, "la regla más específica gana" --
    /// this is what lets the more specific rule exist in the first place). Falls
    /// back to the original single-token conflict when the two-token pattern is
    /// itself too generic or already taken by a third category, so the caller can
    /// still offer "actualizar regla existente / cancelar" (point 13). Returns an
    /// empty pattern when even the base suggestion is too generic to safely anchor
    /// a rule (point 18) -- callers must treat that as "no se puede crear regla".
    /// </summary>
    Task<(string Pattern, CategorizationRule? ExistingRule)> ResolveRulePatternAsync(
        Guid userId,
        Guid categoryId,
        string description,
        CancellationToken cancellationToken);

    /// <summary>
    /// Same as above, but honouring the part of the description the person picked
    /// in the app. <paramref name="requestedPattern"/> null/blank falls back to the
    /// automatic suggestion. A requested pattern is normalized exactly like the
    /// descriptions it will be compared against, must be part of THIS movement's
    /// normalized description (400 otherwise) and is never escalated: the person
    /// chose it on purpose. Too generic returns an empty pattern, like the
    /// automatic path.
    /// </summary>
    Task<(string Pattern, CategorizationRule? ExistingRule)> ResolveRulePatternAsync(
        Guid userId,
        Guid categoryId,
        string description,
        string? requestedPattern,
        CancellationToken cancellationToken);

    /// <summary>
    /// Point 7/16: recategorizes this user's own PAST movements that match
    /// <paramref name="rule"/> (never one the person already corrected by hand,
    /// never another user's), returning how many changed. Scoped to
    /// description-pattern rules -- a merchant-only rule (the older, Entregable 14
    /// correction flow) returns 0 here; see the class doc's note on why.
    /// </summary>
    Task<int> ApplyToExistingTransactionsAsync(
        Guid userId,
        CategorizationRule rule,
        Guid? excludeTransactionId,
        CancellationToken cancellationToken);
}

public sealed class CategorizationRuleService(INexoDbContext db, IClock clock) : ICategorizationRuleService
{
    public async Task<IReadOnlyList<CategorizationRuleDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rules = await db.CategorizationRules
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
        {
            return [];
        }

        var categories = await LoadCategoriesAsync(userId, rules.Select(r => r.CategoryId), cancellationToken);

        return rules.Select(r => Map(r, categories)).ToList();
    }

    public async Task<CategorizationRuleDto> CreateAsync(
        Guid userId,
        CreateCategorizationRuleRequest request,
        CancellationToken cancellationToken)
    {
        var matchKind = ParseMatchType(request.MatchType);
        var normalizedPattern = TextNormalizer.NormalizeForMatching(request.Pattern);

        var category = await RequireCategoryAsync(userId, request.CategoryId, cancellationToken);

        // Point 13 ("reglas contradictorias"): never silently duplicate a pattern
        // this user already has a rule for.
        var existing = await db.CategorizationRules
            .FirstOrDefaultAsync(
                r => r.UserId == userId && r.Pattern == normalizedPattern && r.MatchKind == matchKind,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.CategoryId == category.Id)
            {
                return Map(existing, new Dictionary<Guid, Category> { [category.Id] = category });
            }

            var existingCategory = await RequireCategoryAsync(userId, existing.CategoryId, cancellationToken);
            throw new RuleConflictException(
                $"Ya tienes una regla para \"{existing.Pattern}\" que asigna la categoría \"{existingCategory.Name}\".",
                existing.Id,
                existing.CategoryId,
                existingCategory.Name);
        }

        var now = clock.UtcNow;
        var rule = CategorizationRule.LearnedFromCorrection(userId, normalizedPattern, category.Id, direction: null, now, matchKind);

        db.CategorizationRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);

        return Map(rule, new Dictionary<Guid, Category> { [category.Id] = category });
    }

    public async Task<CategorizationRuleDto> UpdateAsync(
        Guid userId,
        Guid ruleId,
        UpdateCategorizationRuleRequest request,
        CancellationToken cancellationToken)
    {
        var rule = await RequireRuleAsync(userId, ruleId, cancellationToken);
        var category = await RequireCategoryAsync(userId, request.CategoryId, cancellationToken);

        var now = clock.UtcNow;

        if (rule.CategoryId != category.Id)
        {
            rule.Retarget(category.Id, now);
        }

        if (request.IsActive is { } isActive)
        {
            if (isActive)
            {
                rule.Activate(now);
            }
            else
            {
                rule.Deactivate(now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (request.ApplyToExistingMatches)
        {
            // Applied after the rule's own change is saved, so the recount sees
            // the rule's final category.
            await ApplyToExistingTransactionsAsync(userId, rule, excludeTransactionId: null, cancellationToken);
        }

        return Map(rule, new Dictionary<Guid, Category> { [category.Id] = category });
    }

    public async Task DeleteAsync(Guid userId, Guid ruleId, CancellationToken cancellationToken)
    {
        var rule = await RequireRuleAsync(userId, ruleId, cancellationToken);

        // Point 17: deleting a rule only stops it from applying to future
        // movements. Already-categorised transactions are never touched here.
        db.CategorizationRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RulePreviewDto> PreviewAsync(
        Guid userId,
        RulePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId && t.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Transaction", request.TransactionId);

        var category = await RequireCategoryAsync(userId, request.CategoryId, cancellationToken);

        var (suggested, _) = await ResolveRulePatternAsync(userId, category.Id, transaction.Description, cancellationToken);
        var (pattern, existingRule) = string.IsNullOrWhiteSpace(request.Pattern)
            ? (suggested, await FindExistingForSuggestionAsync(userId, suggested, cancellationToken))
            : await ResolveRulePatternAsync(userId, category.Id, transaction.Description, request.Pattern, cancellationToken);
        var isTooGeneric = pattern.Length == 0;
        var normalizedDescription = TextNormalizer.NormalizeForMatching(transaction.Description);
        // Lo que se le muestra a la persona cuando el texto que eligió es
        // demasiado general: su propio texto, no un vacío.
        var shownPattern = isTooGeneric && !string.IsNullOrWhiteSpace(request.Pattern)
            ? TextNormalizer.NormalizeForMatching(request.Pattern)
            : pattern;

        Guid? conflictingRuleId = null;
        Guid? conflictingCategoryId = null;
        string? conflictingCategoryName = null;

        if (!isTooGeneric && existingRule is not null && existingRule.CategoryId != category.Id)
        {
            conflictingRuleId = existingRule.Id;
            conflictingCategoryId = existingRule.CategoryId;
            conflictingCategoryName = (await RequireCategoryAsync(userId, existingRule.CategoryId, cancellationToken)).Name;
        }

        if (isTooGeneric)
        {
            return new RulePreviewDto(shownPattern, "Contains", true, 0, [], conflictingRuleId, conflictingCategoryId, conflictingCategoryName, normalizedDescription, suggested);
        }

        var query = BuildPatternCandidateQuery(userId, pattern, RuleMatchKind.Contains, request.TransactionId, tracked: false);

        var matchedCount = await query.CountAsync(cancellationToken);
        var sampleRows = await query
            .OrderByDescending(t => t.TransactionDate)
            .Take(5)
            .Select(t => new { t.Id, t.Description, t.Amount, t.Direction, t.TransactionDate })
            .ToListAsync(cancellationToken);

        var sample = sampleRows
            .Select(t => new RulePreviewTransactionDto(
                t.Id,
                t.Description,
                t.Direction == TransactionDirection.Income ? t.Amount : -t.Amount,
                t.TransactionDate))
            .ToList();

        return new RulePreviewDto(pattern, "Contains", false, matchedCount, sample, conflictingRuleId, conflictingCategoryId, conflictingCategoryName, normalizedDescription, suggested);
    }

    public async Task<(string Pattern, CategorizationRule? ExistingRule)> ResolveRulePatternAsync(
        Guid userId,
        Guid categoryId,
        string description,
        CancellationToken cancellationToken)
    {
        var pattern = TextNormalizer.SuggestRulePattern(description);
        if (TextNormalizer.IsTooGenericRulePattern(pattern))
        {
            return (string.Empty, null);
        }

        var existing = await FindRuleByPatternAsync(userId, pattern, cancellationToken);

        if (existing is not null && existing.CategoryId != categoryId)
        {
            // The single-token pattern already belongs to a different category's
            // rule -- e.g. "UBER" -> Transporte already exists and this correction
            // is "UBER EATS ..." -> Comida. Try the two-token pattern instead of
            // colliding with (and, in the caller's silent-retarget path, corrupting)
            // the broader rule.
            var narrower = TextNormalizer.SuggestRulePattern(description, tokenCount: 2);
            if (narrower.Length > pattern.Length && !TextNormalizer.IsTooGenericRulePattern(narrower))
            {
                var narrowerExisting = await FindRuleByPatternAsync(userId, narrower, cancellationToken);
                if (narrowerExisting is null || narrowerExisting.CategoryId == categoryId)
                {
                    return (narrower, narrowerExisting);
                }
            }
        }

        return (pattern, existing);
    }

    public async Task<(string Pattern, CategorizationRule? ExistingRule)> ResolveRulePatternAsync(
        Guid userId,
        Guid categoryId,
        string description,
        string? requestedPattern,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestedPattern))
        {
            return await ResolveRulePatternAsync(userId, categoryId, description, cancellationToken);
        }

        var pattern = TextNormalizer.NormalizeForMatching(requestedPattern);
        var normalizedDescription = TextNormalizer.NormalizeForMatching(description);

        if (pattern.Length == 0 || !normalizedDescription.Contains(pattern, StringComparison.Ordinal))
        {
            throw new ValidationException("El texto de la regla tiene que ser parte de la descripción de este movimiento.");
        }

        if (TextNormalizer.IsTooGenericRulePattern(pattern))
        {
            return (string.Empty, null);
        }

        return (pattern, await FindRuleByPatternAsync(userId, pattern, cancellationToken));
    }

    private async Task<CategorizationRule?> FindExistingForSuggestionAsync(Guid userId, string pattern, CancellationToken cancellationToken) =>
        pattern.Length == 0 ? null : await FindRuleByPatternAsync(userId, pattern, cancellationToken);

    private Task<CategorizationRule?> FindRuleByPatternAsync(Guid userId, string pattern, CancellationToken cancellationToken) =>
        db.CategorizationRules.FirstOrDefaultAsync(
            r => r.UserId == userId && r.Pattern == pattern && r.MatchKind == RuleMatchKind.Contains,
            cancellationToken);

    public async Task<int> ApplyToExistingTransactionsAsync(
        Guid userId,
        CategorizationRule rule,
        Guid? excludeTransactionId,
        CancellationToken cancellationToken)
    {
        // Scoped to description-pattern rules: Transaction.Merchant is stored
        // title-cased for display while MerchantPattern is normalized upper-case,
        // so a SQL-side pre-filter on the raw column would silently miss almost
        // every real match -- worse than not offering it. The mobile flow this
        // feature adds always creates a Pattern rule (LearnedFromCorrection), so
        // this covers the feature's own rules end-to-end; see "Pendientes".
        if (rule.Pattern.Length == 0)
        {
            return 0;
        }

        var candidates = BuildPatternCandidateQuery(userId, rule.Pattern, rule.MatchKind, excludeTransactionId, tracked: true);
        var transactions = await candidates.ToListAsync(cancellationToken);

        if (transactions.Count == 0)
        {
            return 0;
        }

        var now = clock.UtcNow;
        var updated = 0;

        foreach (var transaction in transactions)
        {
            var normalizedMerchant = TextNormalizer.NormalizeForMatching(transaction.EffectiveMerchant);
            if (!rule.Matches(transaction.NormalizedDescription, normalizedMerchant, transaction.ProviderCode, transaction.Amount, transaction.Direction))
            {
                continue;
            }

            if (transaction.ApplyAutomaticCategory(rule.CategoryId, now, CategorySource.UserRule, rule.Id))
            {
                updated++;
            }
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }

    /// <summary>
    /// Point 20 ("performance"): always starts from <c>UserId</c> (the indexed
    /// column -- never a scan across other users' movements), then narrows with the
    /// pattern match in SQL so at most a handful of candidate rows ever reach the
    /// application for the full multi-dimension <see cref="CategorizationRule.Matches"/>
    /// check.
    /// </summary>
    private IQueryable<Transaction> BuildPatternCandidateQuery(
        Guid userId,
        string pattern,
        RuleMatchKind matchKind,
        Guid? excludeTransactionId,
        bool tracked)
    {
        var query = tracked ? db.Transactions.AsQueryable() : db.Transactions.AsNoTracking();

        query = query.Where(t =>
            t.UserId == userId
            && !t.CategoryManuallySet
            && !t.IsInternalTransfer
            && t.Status != TransactionStatus.Ignored);

        if (excludeTransactionId is { } excludeId)
        {
            query = query.Where(t => t.Id != excludeId);
        }

        return matchKind switch
        {
            RuleMatchKind.Exact => query.Where(t => t.NormalizedDescription == pattern),
            RuleMatchKind.StartsWith => query.Where(t => t.NormalizedDescription.StartsWith(pattern)),
            _ => query.Where(t => t.NormalizedDescription.Contains(pattern)),
        };
    }

    private async Task<CategorizationRule> RequireRuleAsync(Guid userId, Guid ruleId, CancellationToken cancellationToken) =>
        await db.CategorizationRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("CategorizationRule", ruleId);

    private async Task<Category> RequireCategoryAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken) =>
        await db.Categories.FirstOrDefaultAsync(
            c => c.Id == categoryId && (c.UserId == null || c.UserId == userId),
            cancellationToken)
        ?? throw new NotFoundException("Category", categoryId);

    private async Task<Dictionary<Guid, Category>> LoadCategoriesAsync(
        Guid userId,
        IEnumerable<Guid> categoryIds,
        CancellationToken cancellationToken)
    {
        var ids = categoryIds.Distinct().ToArray();
        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .ToListAsync(cancellationToken);

        return categories.Where(c => ids.Contains(c.Id)).ToDictionary(c => c.Id);
    }

    private static RuleMatchKind ParseMatchType(string matchType)
    {
        if (Enum.TryParse<RuleMatchKind>(matchType, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ValidationException(
            $"\"{matchType}\" no es un tipo de coincidencia válido.",
            new Dictionary<string, string[]> { ["matchType"] = ["Debe ser Contains, StartsWith o Exact."] });
    }

    private static CategorizationRuleDto Map(CategorizationRule rule, IReadOnlyDictionary<Guid, Category> categories)
    {
        categories.TryGetValue(rule.CategoryId, out var category);
        var pattern = rule.Pattern.Length > 0 ? rule.Pattern : rule.MerchantPattern ?? string.Empty;

        return new CategorizationRuleDto(
            rule.Id,
            pattern,
            rule.MatchKind.ToString(),
            rule.CategoryId,
            category?.Name ?? "Sin categoría",
            category?.Icon ?? "circle",
            category?.Color ?? "#ECE9E1",
            rule.IsActive,
            rule.Priority,
            rule.TimesApplied,
            rule.CreatedAt,
            rule.UpdatedAt,
            rule.LastMatchedAt);
    }
}
