using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Domain.Tests;

public class TransactionFingerprintTests
{
    private static readonly Guid Account = Guid.Parse("0192f2c4-0000-7000-8000-000000000001");

    [Fact]
    public void Same_movement_described_differently_still_matches_when_a_reference_exists()
    {
        var fromEmail = TransactionFingerprint.Compute(
            "PICHINCHA", Account, "TRX-99881", new DateTimeOffset(2026, 3, 4, 18, 12, 0, TimeSpan.Zero),
            48.20m, TransactionDirection.Expense, "Compra en SUPERMAXI con tarjeta ****4821");

        var fromStatement = TransactionFingerprint.Compute(
            "PICHINCHA", Account, "TRX-99881", new DateTimeOffset(2026, 3, 5, 3, 0, 0, TimeSpan.Zero),
            48.20m, TransactionDirection.Expense, "SUPERMAXI ALBORADA GYE");

        Assert.Equal(fromEmail, fromStatement);
    }

    [Fact]
    public void Without_a_reference_the_calendar_day_amount_and_wording_decide()
    {
        var a = TransactionFingerprint.Compute(
            "PICHINCHA", Account, null, new DateTimeOffset(2026, 3, 4, 14, 0, 0, TimeSpan.Zero),
            48.20m, TransactionDirection.Expense, "Compra en SUPERMAXI ALBORADA");

        var b = TransactionFingerprint.Compute(
            "PICHINCHA", Account, null, new DateTimeOffset(2026, 3, 4, 22, 30, 0, TimeSpan.Zero),
            48.20m, TransactionDirection.Expense, "SUPERMAXI ALBORADA");

        Assert.Equal(a, b);
    }

    [Fact]
    public void A_different_amount_produces_a_different_fingerprint()
    {
        var a = TransactionFingerprint.Compute(
            "PICHINCHA", Account, null, DateTimeOffset.UnixEpoch, 48.20m, TransactionDirection.Expense, "SUPERMAXI");

        var b = TransactionFingerprint.Compute(
            "PICHINCHA", Account, null, DateTimeOffset.UnixEpoch, 48.21m, TransactionDirection.Expense, "SUPERMAXI");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Income_and_expense_of_the_same_amount_are_distinct()
    {
        var income = TransactionFingerprint.Compute(
            "PICHINCHA", Account, null, DateTimeOffset.UnixEpoch, 100m, TransactionDirection.Income, "TRANSFERENCIA");

        var expense = TransactionFingerprint.Compute(
            "PICHINCHA", Account, null, DateTimeOffset.UnixEpoch, 100m, TransactionDirection.Expense, "TRANSFERENCIA");

        Assert.NotEqual(income, expense);
    }

    [Fact]
    public void The_same_movement_in_two_different_accounts_is_two_movements()
    {
        var other = Guid.Parse("0192f2c4-0000-7000-8000-000000000002");

        var a = TransactionFingerprint.Compute(
            "PICHINCHA", Account, "REF-1234", DateTimeOffset.UnixEpoch, 10m, TransactionDirection.Expense, "X");

        var b = TransactionFingerprint.Compute(
            "PICHINCHA", other, "REF-1234", DateTimeOffset.UnixEpoch, 10m, TransactionDirection.Expense, "X");

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("---", false)]
    [InlineData("0000", false)]
    [InlineData("TRX-99881", true)]
    public void Useless_bank_references_are_not_treated_as_identifiers(string? reference, bool expected) =>
        Assert.Equal(expected, TransactionFingerprint.HasUsableReference(reference));
}
