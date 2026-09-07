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
    public void A_reference_alone_is_not_an_identity()
    {
        // A real Banco Pichincha statement puts a transfer, its commission and the
        // tax on that commission under one document number. Keying on the reference
        // alone collapsed 54 movements into 34 fingerprints.
        var when = new DateTimeOffset(2026, 8, 31, 17, 51, 0, TimeSpan.Zero);

        var transfer = TransactionFingerprint.Compute(
            "PICHINCHA", Account, "90000001", when, 116.00m, TransactionDirection.Expense,
            "TRANSFERENCIA INTERBANCARIA A PROVEEDOR");

        var commission = TransactionFingerprint.Compute(
            "PICHINCHA", Account, "90000001", when, 0.36m, TransactionDirection.Expense,
            "COMISION TRANSFERENCIA INTERBANCARIA ENVIADA");

        var tax = TransactionFingerprint.Compute(
            "PICHINCHA", Account, "90000001", when, 0.05m, TransactionDirection.Expense,
            "IVA COBRADO");

        Assert.Equal(3, new[] { transfer, commission, tax }.Distinct().Count());
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

    [Fact]
    public void The_calendar_day_used_is_the_account_holders_not_UTCs()
    {
        // Entregable 27: 23:30 on 4 March in Ecuador (UTC-5) is 04:30 UTC on 5
        // March -- a different UTC calendar day, but the same local one. The HEU
        // recipe has to key on the local day, or an email fired late at night and
        // its statement row (dated locally) can fail to fingerprint-match even
        // when everything else about them is identical.
        var lateLocal = TransactionFingerprint.Compute(
            "PICHINCHA", Account, null, new DateTimeOffset(2026, 3, 5, 4, 30, 0, TimeSpan.Zero),
            48.20m, TransactionDirection.Expense, "SUPERMAXI ALBORADA");

        var sameLocalDayNoon = TransactionFingerprint.Compute(
            "PICHINCHA", Account, null, new DateTimeOffset(2026, 3, 4, 17, 0, 0, TimeSpan.Zero),
            48.20m, TransactionDirection.Expense, "SUPERMAXI ALBORADA");

        Assert.Equal(lateLocal, sameLocalDayNoon);
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
