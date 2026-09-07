using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Domain.Tests;

/// <summary>
/// Entregable 14 ("Categorización v2"): a rule can now match by merchant, provider,
/// amount range and direction, not only a description pattern, and resolution
/// picks the most specific matching rule. These tests cover the domain half of
/// that -- <see cref="CategorizationRule.Specificity"/> and
/// <see cref="CategorizationRule.Matches"/> -- independently of the engine's
/// selection loop (covered end-to-end in Nexo.Api.IntegrationTests).
/// </summary>
public class CategorizationRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Guid Category = Guid.CreateVersion7();

    [Fact]
    public void A_merchant_rule_is_more_specific_than_a_plain_description_rule()
    {
        // The spec's own example: "UBER EATS -> Comida ; UBER -> Transporte".
        var descriptionRule = CategorizationRule.SystemRule("UBER", Category, Now);
        var merchantRule = CategorizationRule.SystemRule(string.Empty, Category, Now, merchantPattern: "UBER EATS");

        Assert.True(merchantRule.Specificity > descriptionRule.Specificity);
    }

    [Fact]
    public void Specificity_grows_with_every_extra_match_dimension()
    {
        var bare = CategorizationRule.SystemRule("UBER", Category, Now);
        var withDirection = CategorizationRule.SystemRule("UBER", Category, Now, direction: TransactionDirection.Expense);
        var withProviderAndAmount = CategorizationRule.SystemRule(
            "UBER",
            Category,
            Now,
            direction: TransactionDirection.Expense,
            providerCode: "PICHINCHA",
            minAmount: 1m,
            maxAmount: 50m);

        Assert.True(withDirection.Specificity > bare.Specificity);
        Assert.True(withProviderAndAmount.Specificity > withDirection.Specificity);
    }

    [Fact]
    public void A_longer_match_text_breaks_a_tie_in_specificity()
    {
        var shorter = CategorizationRule.SystemRule("UBER", Category, Now);
        var longer = CategorizationRule.SystemRule("UBER EATS", Category, Now);

        // Both are a bare description rule (Specificity 1); the longer pattern wins.
        Assert.Equal(shorter.Specificity, longer.Specificity);
        Assert.True(longer.MatchTextLength > shorter.MatchTextLength);
    }

    [Fact]
    public void A_rule_needs_at_least_one_match_criterion()
    {
        var error = Assert.Throws<DomainException>(() => CategorizationRule.SystemRule(string.Empty, Category, Now));
        Assert.Contains("merchant", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Amount_bounds_cannot_be_negative()
    {
        Assert.Throws<DomainException>(() =>
            CategorizationRule.SystemRule("UBER", Category, Now, minAmount: -1m));
        Assert.Throws<DomainException>(() =>
            CategorizationRule.SystemRule("UBER", Category, Now, maxAmount: -1m));
    }

    [Fact]
    public void Minimum_amount_cannot_exceed_the_maximum()
    {
        Assert.Throws<DomainException>(() =>
            CategorizationRule.SystemRule("UBER", Category, Now, minAmount: 50m, maxAmount: 10m));
    }

    [Fact]
    public void Matches_checks_every_configured_dimension()
    {
        var rule = CategorizationRule.SystemRule(
            "UBER",
            Category,
            Now,
            direction: TransactionDirection.Expense,
            providerCode: "pichincha",
            minAmount: 5m,
            maxAmount: 20m);

        Assert.True(rule.Matches("COMPRA UBER TRIP", null, "PICHINCHA", 10m, TransactionDirection.Expense));

        Assert.False(rule.Matches("COMPRA UBER TRIP", null, "PICHINCHA", 10m, TransactionDirection.Income));
        Assert.False(rule.Matches("COMPRA UBER TRIP", null, "GUAYAQUIL", 10m, TransactionDirection.Expense));
        Assert.False(rule.Matches("COMPRA UBER TRIP", null, "PICHINCHA", 100m, TransactionDirection.Expense));
        Assert.False(rule.Matches("PAGO NETFLIX", null, "PICHINCHA", 10m, TransactionDirection.Expense));
    }

    [Fact]
    public void A_merchant_pattern_matches_the_merchant_not_the_description()
    {
        var rule = CategorizationRule.SystemRule(string.Empty, Category, Now, merchantPattern: "UBER EATS");

        Assert.True(rule.Matches("COMPRA UBER EATS QUITO", "UBER EATS QUITO", "PICHINCHA", 12m, TransactionDirection.Expense));

        // No merchant guess at all -> a merchant-only rule never fires.
        Assert.False(rule.Matches("COMPRA UBER EATS QUITO", null, "PICHINCHA", 12m, TransactionDirection.Expense));

        // A different merchant (a plain Uber ride) doesn't match "UBER EATS".
        Assert.False(rule.Matches("COMPRA UBER TRIP", "UBER", "PICHINCHA", 12m, TransactionDirection.Expense));
    }

    [Fact]
    public void An_inactive_rule_never_matches()
    {
        var rule = CategorizationRule.SystemRule("UBER", Category, Now);
        rule.Deactivate(Now);

        Assert.False(rule.Matches("COMPRA UBER TRIP", null, "PICHINCHA", 10m, TransactionDirection.Expense));
    }

    [Fact]
    public void LearnedFromMerchant_creates_a_personal_rule_scoped_to_the_user()
    {
        var rule = CategorizationRule.LearnedFromMerchant(User, "UBER EATS", Category, TransactionDirection.Expense, Now);

        Assert.Equal(User, rule.UserId);
        Assert.False(rule.IsSystem);
        Assert.Equal("UBER EATS", rule.MerchantPattern);
        Assert.Equal(string.Empty, rule.Pattern);
        Assert.Equal(100, rule.Priority);
    }

    [Fact]
    public void Retargeting_a_rule_changes_only_its_category()
    {
        var rule = CategorizationRule.LearnedFromMerchant(User, "FRUTERIA DON PEPE", Category, TransactionDirection.Expense, Now);
        var newCategory = Guid.CreateVersion7();

        rule.Retarget(newCategory, Now);

        Assert.Equal(newCategory, rule.CategoryId);
        Assert.Equal("FRUTERIA DON PEPE", rule.MerchantPattern);
    }

    /// <summary>
    /// "Categorización personal": <see cref="CategorizationRule.LearnedFromCorrection"/>
    /// is now the active flow behind "aplicar también a movimientos similares", keyed
    /// on the description pattern rather than the merchant guess.
    /// </summary>
    [Fact]
    public void LearnedFromCorrection_creates_a_description_based_personal_rule()
    {
        var rule = CategorizationRule.LearnedFromCorrection(User, "UBER", Category, direction: null, Now);

        Assert.Equal(User, rule.UserId);
        Assert.False(rule.IsSystem);
        Assert.Equal("UBER", rule.Pattern);
        Assert.Equal(RuleMatchKind.Contains, rule.MatchKind);
        Assert.Equal(100, rule.Priority);
        Assert.True(rule.IsActive);
    }

    [Fact]
    public void LearnedFromCorrection_accepts_an_explicit_match_kind()
    {
        var rule = CategorizationRule.LearnedFromCorrection(User, "UBER", Category, direction: null, Now, RuleMatchKind.Exact);

        Assert.Equal(RuleMatchKind.Exact, rule.MatchKind);
    }

    /// <summary>
    /// Point 18 ("matching seguro"): the safety check is enforced in the domain
    /// entity itself, not only wherever a caller happens to validate first -- a
    /// personal rule can never be created with a dangerously generic pattern no
    /// matter which application-layer path constructs it.
    /// </summary>
    [Theory]
    [InlineData("PAGO")]
    [InlineData("A")]
    [InlineData("TRANSFERENCIA")]
    public void LearnedFromCorrection_rejects_a_pattern_with_too_little_information(string pattern) =>
        Assert.Throws<DomainException>(() => CategorizationRule.LearnedFromCorrection(User, pattern, Category, direction: null, Now));

    [Fact]
    public void LearnedFromMerchant_also_rejects_a_too_generic_merchant_pattern() =>
        Assert.Throws<DomainException>(() =>
            CategorizationRule.LearnedFromMerchant(User, "PAGO", Category, TransactionDirection.Expense, Now));

    [Fact]
    public void SystemRule_seeds_are_exempt_from_the_safety_check()
    {
        // Nexo's own curated catalog can use a short, deliberate brand code (e.g.
        // "TIA", "CNT") that a user's own rule would never be allowed to use.
        var rule = CategorizationRule.SystemRule("TIA", Category, Now);

        Assert.Equal("TIA", rule.Pattern);
    }

    [Fact]
    public void RegisterHit_increments_the_counter_and_stamps_when_it_last_matched()
    {
        var rule = CategorizationRule.LearnedFromCorrection(User, "UBER", Category, direction: null, Now);
        var later = Now.AddDays(3);

        rule.RegisterHit(later);

        Assert.Equal(1, rule.TimesApplied);
        Assert.Equal(later, rule.LastMatchedAt);
    }

    [Fact]
    public void Deactivating_and_reactivating_a_rule_never_touches_its_match_count()
    {
        var rule = CategorizationRule.LearnedFromCorrection(User, "UBER", Category, direction: null, Now);
        rule.RegisterHit(Now);

        rule.Deactivate(Now);
        Assert.False(rule.IsActive);

        rule.Activate(Now);
        Assert.True(rule.IsActive);
        Assert.Equal(1, rule.TimesApplied);
    }
}
