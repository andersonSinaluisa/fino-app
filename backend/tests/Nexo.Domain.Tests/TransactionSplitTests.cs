using Nexo.Domain.Common;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Domain.Tests;

/// <summary>
/// Movimientos divididos: the division rules live in the Transaction aggregate.
/// The bank movement itself (amount, date, description, reference, fingerprint)
/// must never change because of a division.
/// </summary>
public class TransactionSplitTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 17, 0, 0, TimeSpan.Zero);
    private static readonly Guid Esposa = Guid.CreateVersion7();
    private static readonly Guid Comida = Guid.CreateVersion7();

    private static Transaction Movement(decimal amount = 220m, TransactionDirection direction = TransactionDirection.Expense, Guid? category = null) =>
        Transaction.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "PICHINCHA",
            Now.AddDays(-2),
            amount,
            direction,
            "TRANSF DIRECTA CHILAN",
            TransactionSource.Import,
            Now,
            externalReference: "DOC-4821",
            categoryId: category ?? Comida);

    [Fact]
    public void The_real_case_divides_220_into_70_and_150_without_touching_the_bank_movement()
    {
        var movement = Movement();
        var fingerprint = movement.Fingerprint;
        var date = movement.TransactionDate;

        movement.Split([new SplitLine(Esposa, 70m), new SplitLine(Comida, 150m)], Now);

        Assert.True(movement.IsSplit);
        Assert.Equal(220m, movement.Amount);
        Assert.Equal(220m, movement.Splits.Sum(s => s.Amount));
        Assert.Equal(-220m, movement.SignedAmount);
        Assert.Equal("TRANSF DIRECTA CHILAN", movement.Description);
        Assert.Equal("DOC-4821", movement.ExternalReference);
        Assert.Equal(fingerprint, movement.Fingerprint);
        Assert.Equal(date, movement.TransactionDate);
        Assert.Null(movement.CategoryId);
        Assert.Equal(CategorySource.ManualSplit, movement.CategorySource);
        Assert.Equal(1, movement.SplitVersion);
    }

    [Fact]
    public void An_incomplete_division_is_rejected_with_what_is_missing()
    {
        var movement = Movement();
        var error = Assert.Throws<DomainException>(() =>
            movement.Split([new SplitLine(Esposa, 70m), new SplitLine(Comida, 100m)], Now));

        Assert.Equal("split_incomplete", error.Code);
        Assert.Contains("50.00", error.Message, StringComparison.Ordinal);
        Assert.False(movement.IsSplit);
        Assert.Equal(Comida, movement.CategoryId);
    }

    [Fact]
    public void A_division_above_the_total_says_by_how_much()
    {
        var movement = Movement();
        var error = Assert.Throws<DomainException>(() =>
            movement.Split([new SplitLine(Esposa, 100m), new SplitLine(Comida, 150m)], Now));

        Assert.Equal("split_exceeds_total", error.Code);
        Assert.Equal("Te pasaste por $30.00.", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    public void Zero_negative_and_over_precise_parts_are_rejected(double amount)
    {
        var movement = Movement();
        Assert.Throws<DomainException>(() =>
            movement.Split([new SplitLine(Esposa, (decimal)amount), new SplitLine(Comida, 220m - (decimal)amount)], Now));
    }

    [Fact]
    public void One_part_or_a_repeated_category_is_not_a_division()
    {
        var movement = Movement();
        Assert.Throws<DomainException>(() => movement.Split([new SplitLine(Comida, 220m)], Now));
        Assert.Throws<DomainException>(() => movement.Split([new SplitLine(Comida, 110m), new SplitLine(Comida, 110m)], Now));
    }

    [Fact]
    public void Cents_add_up_exactly()
    {
        var movement = Movement(0.30m);
        movement.Split([new SplitLine(Esposa, 0.10m), new SplitLine(Comida, 0.20m)], Now);
        Assert.Equal(0.30m, movement.Splits.Sum(s => s.Amount));
    }

    [Fact]
    public void Income_divides_the_same_way()
    {
        var salario = Guid.CreateVersion7();
        var freelance = Guid.CreateVersion7();
        var movement = Movement(1000m, TransactionDirection.Income, salario);

        movement.Split([new SplitLine(salario, 800m), new SplitLine(freelance, 150m), new SplitLine(null, 50m)], Now);

        Assert.Equal(1000m, movement.SignedAmount);
        Assert.Equal(3, movement.Splits.Count);
        Assert.Contains(movement.Splits, s => s.CategoryId is null && s.Amount == 50m);
    }

    [Fact]
    public void A_divided_movement_rejects_a_single_category_and_automatic_rules()
    {
        var movement = Movement();
        movement.Split([new SplitLine(Esposa, 70m), new SplitLine(Comida, 150m)], Now);

        Assert.Throws<DomainException>(() => movement.SetCategoryManually(Comida, Now));
        Assert.Throws<DomainException>(() => movement.ClearCategoryManually(Now));
        Assert.False(movement.ApplyAutomaticCategory(Comida, Now));
        Assert.True(movement.IsSplit);
    }

    [Fact]
    public void Editing_the_division_replaces_it_and_bumps_the_version()
    {
        var movement = Movement();
        movement.Split([new SplitLine(Esposa, 70m), new SplitLine(Comida, 150m)], Now);
        movement.Split([new SplitLine(Esposa, 20m), new SplitLine(Comida, 200m)], Now);

        Assert.Equal(2, movement.Splits.Count);
        Assert.Equal(20m, movement.Splits.Single(s => s.CategoryId == Esposa).Amount);
        Assert.Equal(2, movement.SplitVersion);
    }

    [Fact]
    public void Removing_the_division_leaves_the_chosen_category_and_the_same_movement()
    {
        var movement = Movement();
        movement.Split([new SplitLine(Esposa, 70m), new SplitLine(Comida, 150m)], Now);

        movement.RemoveSplit(Comida, Now);

        Assert.False(movement.IsSplit);
        Assert.Empty(movement.Splits);
        Assert.Equal(Comida, movement.CategoryId);
        Assert.Equal(CategorySource.Manual, movement.CategorySource);
        Assert.Equal(220m, movement.Amount);
    }

    [Fact]
    public void A_statement_upgrade_with_a_different_amount_keeps_the_division_balanced()
    {
        var movement = Movement(220m);
        movement.Split([new SplitLine(Esposa, 70m), new SplitLine(Comida, 150m)], Now);

        var statement = Movement(221.50m);
        movement.UpgradeFrom(statement, Now);

        Assert.True(movement.IsSplit);
        Assert.Equal(221.50m, movement.Amount);
        Assert.Equal(221.50m, movement.Splits.Sum(s => s.Amount));
        Assert.Equal(151.50m, movement.Splits.Single(s => s.CategoryId == Comida).Amount);
    }
}
