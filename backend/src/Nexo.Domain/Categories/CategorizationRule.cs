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
/// Deterministic, inspectable categorisation. No model, no black box: a rule is a
/// pattern plus a target category plus a priority, and the user can see and change it.
/// Rules learnt from a user's manual correction are stored with that user's id.
/// </summary>
public sealed class CategorizationRule : Entity
{
    private CategorizationRule()
    {
    }

    public Guid? UserId { get; private set; }

    public string Pattern { get; private set; } = null!;

    public RuleMatchKind MatchKind { get; private set; }

    public Guid CategoryId { get; private set; }

    /// <summary>Optional restriction: a rule may apply only to income or only to expenses.</summary>
    public TransactionDirection? Direction { get; private set; }

    /// <summary>Lower runs first. User rules default to 100, system rules to 1000.</summary>
    public int Priority { get; private set; }

    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>How many times this rule has classified a movement. Feeds rule quality review.</summary>
    public int TimesApplied { get; private set; }

    public static CategorizationRule SystemRule(
        string pattern,
        Guid categoryId,
        DateTimeOffset now,
        RuleMatchKind matchKind = RuleMatchKind.Contains,
        TransactionDirection? direction = null,
        int priority = 1000)
    {
        var rule = new CategorizationRule
        {
            Pattern = TextNormalizer.NormalizeForMatching(pattern),
            MatchKind = matchKind,
            CategoryId = categoryId,
            Direction = direction,
            Priority = priority,
            IsSystem = true,
        };
        DomainException.Require(rule.Pattern.Length > 0, "A categorisation rule needs a pattern.");
        rule.Stamp(now);
        return rule;
    }

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
        DomainException.Require(rule.Pattern.Length > 0, "A categorisation rule needs a pattern.");
        rule.Stamp(now);
        return rule;
    }

    public bool Matches(string normalizedDescription, TransactionDirection direction)
    {
        if (!IsActive || Pattern.Length == 0)
        {
            return false;
        }

        if (Direction is not null && Direction != direction)
        {
            return false;
        }

        return MatchKind switch
        {
            RuleMatchKind.Equals => string.Equals(normalizedDescription, Pattern, StringComparison.Ordinal),
            RuleMatchKind.StartsWith => normalizedDescription.StartsWith(Pattern, StringComparison.Ordinal),
            _ => normalizedDescription.Contains(Pattern, StringComparison.Ordinal),
        };
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
}
