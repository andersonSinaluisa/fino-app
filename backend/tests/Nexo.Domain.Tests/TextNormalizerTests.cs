using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Domain.Tests;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Compra en SUPERMAXI Albórada", "SUPERMAXI ALBORADA")]
    [InlineData("PAGO   TARJETA   ****4821  NETFLIX.COM", "NETFLIX COM")]
    [InlineData("Transferencia recibida de José Pérez", "TRANSFERENCIA RECIBIDA JOSE PEREZ")]
    [InlineData("  UBER   *TRIP ", "UBER TRIP")]
    [InlineData("Uber *Trip", "UBER TRIP")]
    [InlineData("UBER*TRIP", "UBER TRIP")]
    public void NormalizeForMatching_strips_accents_noise_and_numbers(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.NormalizeForMatching(input));

    [Theory]
    [InlineData("UBER *TRIP 829173", "UBER TRIP")]
    [InlineData("UBER *TRIP HELP.UBER.COM", "UBER TRIP HELP UBER COM")]
    [InlineData("UBER *TRIP 923821", "UBER TRIP")]
    public void NormalizeForMatching_strips_long_reference_numbers_but_not_the_merchant_words(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.NormalizeForMatching(input));

    [Fact]
    public void NormalizeForMatching_preserves_short_numbers_that_are_part_of_a_real_merchant_name()
    {
        // "Farmacias 911" is a real Ecuadorian pharmacy chain -- a short number like
        // this is a distinguishing part of the brand, not a masked card digit or an
        // authorization code, so it must survive normalization (point 2/22 of the
        // "categorización personal" spec: never strip numbers indiscriminately).
        Assert.Equal("FARMACIA 911", TextNormalizer.NormalizeForMatching("FARMACIA 911"));
    }

    [Fact]
    public void NormalizeForMatching_on_empty_input_returns_empty() =>
        Assert.Equal(string.Empty, TextNormalizer.NormalizeForMatching("   "));

    [Theory]
    [InlineData("UBER *TRIP 829173", "UBER")]
    [InlineData("UBER *TRIP HELP.UBER.COM", "UBER")]
    [InlineData("UBER *TRIP 923821", "UBER")]
    [InlineData("  UBER   *TRIP ", "UBER")]
    [InlineData("Uber *Trip", "UBER")]
    public void SuggestRulePattern_reduces_description_variants_to_the_same_token(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.SuggestRulePattern(input));

    [Fact]
    public void SuggestRulePattern_on_empty_input_returns_empty() =>
        Assert.Equal(string.Empty, TextNormalizer.SuggestRulePattern("   "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("A")]
    [InlineData("AB")]
    [InlineData("COMPRA")]
    [InlineData("PAGO")]
    [InlineData("TRANSFERENCIA")]
    [InlineData("COMPRA PAGO")]
    public void IsTooGenericRulePattern_rejects_short_or_content_free_patterns(string? pattern) =>
        Assert.True(TextNormalizer.IsTooGenericRulePattern(pattern));

    [Theory]
    [InlineData("UBER")]
    [InlineData("SUPERMAXI")]
    [InlineData("NETFLIX")]
    [InlineData("FARMACIA 911")]
    public void IsTooGenericRulePattern_accepts_real_merchant_tokens(string pattern) =>
        Assert.False(TextNormalizer.IsTooGenericRulePattern(pattern));

    [Fact]
    public void Email_and_statement_wording_of_the_same_purchase_converge()
    {
        const string fromEmail = "Compra en SUPERMAXI ALBORADA con tu tarjeta terminada en 4821";
        const string fromStatement = "SUPERMAXI ALBORADA";

        Assert.True(TextNormalizer.Similarity(fromEmail, fromStatement) >= 0.6d);
    }

    [Fact]
    public void Different_merchants_are_not_similar() =>
        Assert.True(TextNormalizer.Similarity("SUPERMAXI ALBORADA", "PRIMAX VIA DAULE") < 0.3d);

    [Fact]
    public void ExtractMerchant_title_cases_the_leading_tokens() =>
        Assert.Equal("Supermaxi Alborada Guayaquil", TextNormalizer.ExtractMerchant("SUPERMAXI ALBORADA GUAYAQUIL CENTRO"));
}
