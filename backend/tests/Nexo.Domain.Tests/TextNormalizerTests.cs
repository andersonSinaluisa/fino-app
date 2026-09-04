using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Domain.Tests;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Compra en SUPERMAXI Albórada", "SUPERMAXI ALBORADA")]
    [InlineData("PAGO   TARJETA   ****4821  NETFLIX.COM", "NETFLIX COM")]
    [InlineData("Transferencia recibida de José Pérez", "TRANSFERENCIA RECIBIDA JOSE PEREZ")]
    public void NormalizeForMatching_strips_accents_noise_and_numbers(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.NormalizeForMatching(input));

    [Fact]
    public void NormalizeForMatching_on_empty_input_returns_empty() =>
        Assert.Equal(string.Empty, TextNormalizer.NormalizeForMatching("   "));

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
