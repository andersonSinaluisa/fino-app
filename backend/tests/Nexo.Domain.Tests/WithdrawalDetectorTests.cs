using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Domain.Tests;

/// <summary>
/// §25: las variantes reales con las que los bancos ecuatorianos escriben un retiro,
/// y -- más importante -- las que NO son un retiro.
///
/// El caso que más vigila este archivo es "COMISION RETIRO ATM". Confundirlo con un
/// retiro no es un fallo cosmético: convertiría un gasto real en una transferencia
/// interna y lo sacaría de las estadísticas de gasto, así que la persona vería menos
/// gasto del que tuvo. Es el error más caro que puede cometer este detector.
/// </summary>
public class WithdrawalDetectorTests
{
    private static WithdrawalSignal Detect(string description, decimal amount = 100m) =>
        WithdrawalDetector.Detect(description, TransactionDirection.Expense, amount);

    [Theory]
    [InlineData("RETIRO")]
    [InlineData("RETIRO ATM")]
    [InlineData("RETIRO CAJERO")]
    [InlineData("RETIRO EFECTIVO")]
    [InlineData("RETIRO CAJERO AUTOMATICO")]
    [InlineData("RETIRO BCO")]
    [InlineData("RETIRO ATM INTERNACIONAL")]
    [InlineData("RETIRO RED")]
    [InlineData("RETINJ")]
    [InlineData("RETIINJ")]
    [InlineData("RETIRO NACIONAL")]
    [InlineData("RETIRO OFICINA")]
    [InlineData("RETIRO VENTANILLA")]
    [InlineData("ATM WITHDRAWAL")]
    [InlineData("CASH WITHDRAWAL")]
    [InlineData("RETIRO INTERNACIONAL")]
    public void Recognises_every_variant_the_spec_listed(string description)
    {
        Assert.True(Detect(description).IsCandidate, description);
    }

    [Fact]
    public void Ignores_accents_case_punctuation_and_glued_codes()
    {
        // "RETIRO-ATM 00123" y "retiro cajero automático" son el mismo movimiento
        // escrito por dos bancos distintos.
        Assert.True(Detect("RETIRO-ATM 00123").IsCandidate);
        Assert.True(Detect("retiro cajero automático").IsCandidate);
        Assert.True(Detect("  Retiro   ATM  ").IsCandidate);
        Assert.True(Detect("RETINJ0012 ATM").IsCandidate);
    }

    [Fact]
    public void A_fee_is_never_a_withdrawal()
    {
        // §15: el caso caro. Lleva "RETIRO" y "ATM", todas las señales de retiro
        // posibles, y aun así es un gasto real que debe seguir contando como gasto.
        Assert.False(Detect("COMISION RETIRO ATM", 0.50m).IsCandidate);
        Assert.False(Detect("COMISION POR RETIRO EN CAJERO", 0.45m).IsCandidate);
        Assert.False(Detect("IVA COMISION RETIRO", 0.06m).IsCandidate);
        Assert.False(Detect("COSTO RETIRO ATM OTRA RED", 1.00m).IsCandidate);
        Assert.False(Detect("TARIFA CAJERO", 0.50m).IsCandidate);
    }

    [Fact]
    public void Atm_on_its_own_never_reaches_high_confidence()
    {
        // §3: "no marcar automáticamente como retiro solo por contener ATM".
        var signal = Detect("PAGO ATM SERVICIOS", 20m);

        Assert.NotEqual(WithdrawalConfidence.High, signal.Confidence);
    }

    [Fact]
    public void An_explicit_withdrawal_word_beats_a_pile_of_weak_signals()
    {
        var explicitWithdrawal = Detect("RETIRO ATM", 100m);
        var weakOnly = Detect("ATM INTERNACIONAL NACIONAL", 100m);

        Assert.Equal(WithdrawalConfidence.High, explicitWithdrawal.Confidence);
        Assert.True(explicitWithdrawal.Score > weakOnly.Score);
    }

    [Fact]
    public void A_round_amount_supports_but_never_creates_a_detection()
    {
        // Un monto redondo suma cuando ya hay otra señal...
        Assert.True(Detect("RETIRO", 100m).Score > Detect("RETIRO", 97.35m).Score);

        // ...pero por sí solo no convierte un consumo en un retiro.
        Assert.False(Detect("SUPERMAXI ALBORADA", 100m).IsCandidate);
        Assert.False(Detect("NETFLIX.COM", 20m).IsCandidate);
    }

    [Fact]
    public void Income_is_never_a_withdrawal()
    {
        // Una línea que diga "RETIRO" pero sume dinero es otra cosa (la reversa de
        // un retiro, por ejemplo). Proponerla como retiro crearía una transferencia
        // con las dos patas en el mismo sentido.
        var signal = WithdrawalDetector.Detect("REVERSO RETIRO ATM", TransactionDirection.Income, 100m);

        Assert.False(signal.IsCandidate);
    }

    [Fact]
    public void Ordinary_spending_is_left_alone()
    {
        foreach (var description in new[]
                 {
                     "SUPERMAXI ALBORADA", "UBER TRIP", "NETFLIX.COM", "KFC MALL DEL SOL",
                     "PAGO TARJETA DE CREDITO", "TRANSFERENCIA INTERBANCARIA",
                     "ACREDITACION ROL DE PAGOS", "FYBECA URDESA",
                 })
        {
            Assert.False(Detect(description).IsCandidate, description);
        }
    }

    [Fact]
    public void An_empty_description_is_not_a_withdrawal()
    {
        Assert.False(Detect("").IsCandidate);
        Assert.False(Detect("   ").IsCandidate);
        Assert.False(WithdrawalDetector.Detect(null, TransactionDirection.Expense, 100m).IsCandidate);
    }

    [Fact]
    public void The_reference_code_counts_when_the_concept_says_nothing()
    {
        // Algunos extractos dejan la pista en el número de documento, no en el
        // concepto.
        var signal = WithdrawalDetector.Detect(
            "MOVIMIENTO",
            TransactionDirection.Expense,
            100m,
            externalReference: "RETIRO ATM 4821");

        Assert.True(signal.IsCandidate);
    }

    [Fact]
    public void The_matched_term_is_reported_so_the_suggestion_can_explain_itself()
    {
        Assert.Equal("RETIRO", Detect("RETIRO ATM").MatchedTerm);
        Assert.Equal("CAJERO", Detect("CAJERO AUTOMATICO").MatchedTerm);
    }

    [Fact]
    public void Suggests_a_learnable_pattern()
    {
        // §14: el patrón que se guardará como regla aprendida. Sale del mismo
        // ayudante que usa la categorización personal, así que una regla aprendida
        // de un retiro es indistinguible de cualquier otra regla del usuario.
        var pattern = WithdrawalDetector.SuggestPattern("RETIRO ATM 00123");

        Assert.False(string.IsNullOrWhiteSpace(pattern));
        Assert.DoesNotContain("00123", pattern);
    }
}
