using Nexo.Domain.Common;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Domain.Tests;

public class TransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Guid Account = Guid.CreateVersion7();

    private static Transaction Create(
        decimal amount,
        TransactionDirection direction,
        string description = "SUPERMAXI ALBORADA") =>
        Transaction.Create(User, Account, "PICHINCHA", Now, amount, direction, description, TransactionSource.Import, Now);

    [Theory]
    [InlineData(-48.20, TransactionDirection.Expense, 48.20, -48.20)]
    [InlineData(48.20, TransactionDirection.Expense, 48.20, -48.20)]
    [InlineData(350.00, TransactionDirection.Income, 350.00, 350.00)]
    [InlineData(-350.00, TransactionDirection.Income, 350.00, 350.00)]
    public void The_sign_lives_in_the_direction_not_in_the_amount(
        double input,
        TransactionDirection direction,
        double expectedAmount,
        double expectedSigned)
    {
        var transaction = Create((decimal)input, direction);

        Assert.Equal((decimal)expectedAmount, transaction.Amount);
        Assert.Equal((decimal)expectedSigned, transaction.SignedAmount);
    }

    [Fact]
    public void A_zero_amount_is_not_a_movement()
    {
        var error = Assert.Throws<DomainException>(() => Create(0m, TransactionDirection.Expense));
        Assert.Equal("zero_amount", error.Code);
    }

    [Fact]
    public void A_movement_without_a_description_is_rejected() =>
        Assert.Throws<DomainException>(() => Create(10m, TransactionDirection.Expense, "   "));

    [Fact]
    public void An_automatic_rule_never_overwrites_a_manual_category()
    {
        var transaction = Create(10m, TransactionDirection.Expense);
        var manual = Guid.CreateVersion7();
        var automatic = Guid.CreateVersion7();

        transaction.SetCategoryManually(manual, Now);
        var applied = transaction.ApplyAutomaticCategory(automatic, Now);

        Assert.False(applied);
        Assert.Equal(manual, transaction.CategoryId);
        Assert.True(transaction.CategoryManuallySet);
    }

    [Fact]
    public void Flagging_a_probable_duplicate_keeps_the_row_and_parks_it_for_review()
    {
        var transaction = Create(10m, TransactionDirection.Expense);
        var existing = Guid.CreateVersion7();

        transaction.FlagAsPossibleDuplicate(existing, Now);

        Assert.Equal(TransactionStatus.NeedsReview, transaction.Status);
        Assert.Equal(existing, transaction.PossibleDuplicateOfId);

        transaction.ConfirmNotDuplicate(Now);

        Assert.Equal(TransactionStatus.Posted, transaction.Status);
        Assert.Null(transaction.PossibleDuplicateOfId);
    }

    [Fact]
    public void An_email_movement_is_upgraded_by_the_statement_row()
    {
        var fromEmail = Transaction.Create(
            User, Account, "PICHINCHA", Now, 48.00m, TransactionDirection.Expense,
            "Compra en SUPERMAXI", TransactionSource.Email, Now,
            confidence: SourceConfidence.Medium, status: TransactionStatus.Pending);

        var fromStatement = Transaction.Create(
            User, Account, "PICHINCHA", Now, 48.20m, TransactionDirection.Expense,
            "SUPERMAXI ALBORADA GYE", TransactionSource.Import, Now, externalReference: "TRX-99881");

        fromEmail.UpgradeFrom(fromStatement, Now);

        Assert.Equal(48.20m, fromEmail.Amount);
        Assert.Equal(TransactionSource.Import, fromEmail.Source);
        Assert.Equal(SourceConfidence.High, fromEmail.SourceConfidence);
        Assert.Equal(TransactionStatus.Posted, fromEmail.Status);
        Assert.Equal("TRX-99881", fromEmail.ExternalReference);
    }
}
