using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Domain.Categories;

public enum RuleMatchKind
{
    /// <summary>Normalized description contains the pattern as a whole token sequence.</summary>
    Contains = 0,

    /// <summary>Normalized description starts with the pattern.</summary>
    StartsWith = 1,

    /// <summary>Normalized description equals the pattern exactly.</summary>
    Equals = 2,
}

/// <summary>
/// Deterministic, inspectable categorisation. No model, no black box: a rule is one
/// or more match criteria (description text, merchant, provider, direction, amount
/// range) plus a target category plus a priority, and the user can see and change
/// it. Rules learnt from a user's manual correction are stored with that user's id.
///
/// Entregable 14 ("Categorización v2"): a rule used to be a single description
/// text pattern. It can now also match by merchant, provider and amount range, and
/// resolution picks the MOST SPECIFIC matching rule rather than the first one found
/// -- see <see cref="Specificity"/> and CategorizationEngine.Session.Suggest.
/// </summary>
public sealed class CategorizationRule : Entity
{
    private CategorizationRule()
    {
    }

    public Guid? UserId { get; private set; }

    /// <summary>Description-text match. Empty when the rule matches by merchant alone.</summary>
    public string Pattern { get; private set; } = string.Empty;

    public RuleMatchKind MatchKind { get; private set; }

    public Guid CategoryId { get; private set; }

    /// <summary>Optional restriction: a rule may apply only to income or only to expenses.</summary>
    public TransactionDirection? Direction { get; private set; }

    /// <summary>
    /// Normalized merchant text (Contains match against the transaction's automatic
    /// merchant guess). A merchant match is a stronger, more specific signal than a
    /// bare description match -- see <see cref="Specificity"/>.
    /// </summary>
    public string? MerchantPattern { get; private set; }

    /// <summary>Optional restriction to one institution (e.g. a bank-specific fee).</summary>
    public string? ProviderCode { get; private set; }

    /// <summary>Inclusive lower bound on the movement's magnitude (never signed).</summary>
    public decimal? MinAmount { get; private set; }

    /// <summary>Inclusive upper bound on the movement's magnitude.</summary>
    public decimal? MaxAmount { get; private set; }

    /// <summary>Lower runs first. User rules default to 100, system rules to 1000.</summary>
    public int Priority { get; private set; }

    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>How many times this rule has classified a movement. Feeds rule quality review.</summary>
    public int TimesApplied { get; private set; }

    /// <summary>
    /// How many match dimensions this rule pins down. The tiebreaker within a
    /// priority tier: "UBER EATS" (merchant + description, Specificity 3) beats a
    /// plain "UBER" description rule (Specificity 1) for an Uber Eats movement,
    /// regardless of which rule happens to have the longer pattern string.
    /// </summary>
    public int Specificity =>
        (Pattern.Length > 0 ? 1 : 0) +
        (MerchantPattern is { Length: > 0 } ? 2 : 0) +
        (ProviderCode is not null ? 1 : 0) +
        (Direction is not null ? 1 : 0) +
        (MinAmount is not null || MaxAmount is not null ? 1 : 0);

    /// <summary>Final tiebreak once two rules tie on <see cref="Specificity"/>: the longer match text wins.</summary>
    public int MatchTextLength => Pattern.Length + (MerchantPattern?.Length ?? 0);

    public static CategorizationRule SystemRule(
        string pattern,
        Guid categoryId,
        DateTimeOffset now,
        RuleMatchKind matchKind = RuleMatchKind.Contains,
        TransactionDirection? direction = null,
        int priority = 1000,
        string? merchantPattern = null,
        string? providerCode = null,
        decimal? minAmount = null,
        decimal? maxAmount = null)
    {
        var rule = new CategorizationRule
        {
            Pattern = TextNormalizer.NormalizeForMatching(pattern),
            MatchKind = matchKind,
            CategoryId = categoryId,
            Direction = direction,
            MerchantPattern = NormalizeOptional(merchantPattern),
            ProviderCode = NormalizeProviderCode(providerCode),
            MinAmount = minAmount,
            MaxAmount = maxAmount,
            Priority = priority,
            IsSystem = true,
        };
        RequireMatchCriterion(rule);
        RequireValidAmountRange(rule);
        rule.Stamp(now);
        return rule;
    }

    /// <summary>
    /// Description-text learning, kept for completeness. The correction flow
    /// (TransactionService.UpdateCategoryAsync) uses <see cref="LearnedFromMerchant"/>
    /// instead as of Entregable 14, since the spec is "reglas por merchant".
    /// </summary>
    public static CategorizationRule LearnedFromCorrection(
        Guid userId,
        string pattern,
        Guid categoryId,
        TransactionDirection? direction,
        DateTimeOffset now)
    {
        var rule = new CategorizationRule
        {
            UserId = userId,
            Pattern = TextNormalizer.NormalizeForMatching(pattern),
            MatchKind = RuleMatchKind.Contains,
            CategoryId = categoryId,
            Direction = direction,
            Priority = 100,
            IsSystem = false,
        };
        RequireMatchCriterion(rule);
        rule.Stamp(now);
        return rule;
    }

    /// <summary>
    /// Entregable 14: what a manual category correction actually learns -- a
    /// personal rule scoped to this merchant (Nexo's automatic guess, not the
    /// person's own display correction), so the next movement from the same
    /// merchant is categorised correctly without asking again.
    /// </summary>
    public static CategorizationRule LearnedFromMerchant(
        Guid userId,
        string merchantPattern,
        Guid categoryId,
        TransactionDirection? direction,
        DateTimeOffset now)
    {
        var rule = new CategorizationRule
        {
            UserId = userId,
            Pattern = string.Empty,
            MatchKind = RuleMatchKind.Contains,
            CategoryId = categoryId,
            Direction = direction,
            MerchantPattern = NormalizeOptional(merchantPattern),
            Priority = 100,
            IsSystem = false,
        };
        RequireMatchCriterion(rule);
        rule.Stamp(now);
        return rule;
    }

    /// <summary>
    /// All match dimensions this rule cares about must hold. <paramref name="normalizedMerchant"/>
    /// and <paramref name="providerCode"/> come from the transaction's own automatic
    /// guess/institution, never from a person's display correction.
    /// </summary>
    public bool Matches(
        string normalizedDescription,
        string? normalizedMerchant,
        string providerCode,
        decimal amount,
        TransactionDirection direction)
    {
        if (!IsActive)
        {
            return false;
        }

        if (Direction is not null && Direction != direction)
        {
            return false;
        }

        if (ProviderCode is not null && !string.Equals(ProviderCode, providerCode, StringComparison.Ordinal))
        {
            return false;
        }

        if (MinAmount is { } min && amount < min)
        {
            return false;
        }

        if (MaxAmount is { } max && amount > max)
        {
            return false;
        }

        if (MerchantPattern is { Length: > 0 })
        {
            if (string.IsNullOrEmpty(normalizedMerchant)
                || !normalizedMerchant.Contains(MerchantPattern, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (Pattern.Length > 0)
        {
            var matchesPattern = MatchKind switch
            {
                RuleMatchKind.Equals => string.Equals(normalizedDescription, Pattern, StringComparison.Ordinal),
                RuleMatchKind.StartsWith => normalizedDescription.StartsWith(Pattern, StringComparison.Ordinal),
                _ => normalizedDescription.Contains(Pattern, StringComparison.Ordinal),
            };

            if (!matchesPattern)
            {
                return false;
            }
        }

        return true;
    }

    public void RegisterHit(DateTimeOffset now)
    {
        TimesApplied++;
        Stamp(now);
    }

    public void Retarget(Guid categoryId, DateTimeOffset now)
    {
        CategoryId = categoryId;
        Stamp(now);
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        Stamp(now);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = TextNormalizer.NormalizeForMatching(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? NormalizeProviderCode(string? providerCode) =>
        string.IsNullOrWhiteSpace(providerCode) ? null : providerCode.Trim().ToUpperInvariant();

    private static void RequireMatchCriterion(CategorizationRule rule) =>
        DomainException.Require(
            rule.Pattern.Length > 0 || rule.MerchantPattern is { Length: > 0 },
            "A categorisation rule needs a description pattern or a merchant to match on.");

    private static void RequireValidAmountRange(CategorizationRule rule)
    {
        DomainException.Require(rule.MinAmount is not { } min || min >= 0m, "El monto mínimo no puede ser negativo.");
        DomainException.Require(rule.MaxAmount is not { } max || max >= 0m, "El monto máximo no puede ser negativo.");
        DomainException.Require(
            rule.MinAmount is not { } lo || rule.MaxAmount is not { } hi || lo <= hi,
            "El monto mínimo no puede ser mayor que el máximo.");
    }
}
