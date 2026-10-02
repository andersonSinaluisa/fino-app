using Nexo.Application.Budgets;
using Nexo.Application.Imports;
using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Budgets;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Application.Tests;

/// <summary>
/// Tarjetas de crédito: importing a card statement through the shared pipeline.
/// </summary>
public class CreditCardStatementImportTests
{
    private static readonly DateTimeOffset Day = new(2026, 9, 10, 17, 0, 0, TimeSpan.Zero);

    private static ParsedStatementTransaction Row(int number, string description, TransactionDirection direction, decimal amount = 10m) =>
        new(number, Day, amount, direction, description, null, null, null);

    private static StatementParseResult Parsed(params ParsedStatementTransaction[] rows) =>
        StatementParseResult.Success("GENERIC_V1", rows, []);

    [Fact]
    public void Purchases_printed_as_positive_amounts_are_flipped_into_charges()
    {
        // A generic parser reads "+42.80" as money in; on a card it is a purchase.
        var (result, flipped) = CreditCardStatementImport.Orient(Parsed(
            Row(1, "SUPERMAXI", TransactionDirection.Income),
            Row(2, "SU PAGO GRACIAS", TransactionDirection.Expense),
            Row(3, "UBER", TransactionDirection.Income)));

        Assert.True(flipped);
        Assert.Equal(TransactionDirection.Expense, result.Transactions[0].Direction);
        Assert.Equal(TransactionDirection.Income, result.Transactions[1].Direction);
    }

    [Fact]
    public void A_file_already_in_card_terms_is_left_alone()
    {
        var (result, flipped) = CreditCardStatementImport.Orient(Parsed(
            Row(1, "SUPERMAXI", TransactionDirection.Expense),
            Row(2, "ABONO TARJETA", TransactionDirection.Income),
            Row(3, "INTERESES", TransactionDirection.Expense)));

        Assert.False(flipped);
        Assert.Equal(TransactionDirection.Expense, result.Transactions[0].Direction);
    }

    [Fact]
    public void Without_payments_interest_rows_decide_the_orientation()
    {
        var (_, flipped) = CreditCardStatementImport.Orient(Parsed(
            Row(1, "KFC", TransactionDirection.Expense),
            Row(2, "INTERESES ROTATIVOS", TransactionDirection.Income)));

        Assert.True(flipped);
    }

    [Fact]
    public void Reads_the_header_figures_and_never_takes_available_credit_as_the_limit()
    {
        var table = new TabularTable(
        [
            new TabularRow(1, ["Fecha de corte", "15/09/2026"]),
            new TabularRow(2, ["Fecha máxima de pago", "30/09/2026"]),
            new TabularRow(3, ["Cupo disponible", "1,249.68"]),
            new TabularRow(4, ["Cupo total", "2,000.00"]),
            new TabularRow(5, ["Total a pagar", "$420.00", "Pago mínimo", "$45.00"]),
            new TabularRow(6, ["Fecha", "Descripción", "Valor"]),
        ]);

        var summary = CreditCardStatementImport.ReadSummary(table);

        Assert.Equal(new DateOnly(2026, 9, 15), summary.ClosingDate);
        Assert.Equal(new DateOnly(2026, 9, 30), summary.DueDate);
        Assert.Equal(420m, summary.StatementBalance);
        Assert.Equal(45m, summary.MinimumPayment);
        Assert.Equal(2000m, summary.CreditLimit);
    }

    [Fact]
    public void A_bank_statement_has_no_card_summary()
    {
        var table = new TabularTable([new TabularRow(1, ["Fecha", "Concepto", "Debito", "Credito"])]);
        Assert.True(CreditCardStatementImport.ReadSummary(table).IsEmpty);
    }

    [Fact]
    public void A_card_refund_without_a_tracked_category_gives_back_to_the_general_budget()
    {
        var window = new BudgetWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var rows = new[]
        {
            new SpendingRow(Guid.CreateVersion7(), null, TransactionDirection.Expense, 100m, new DateOnly(2026, 9, 5)),
            new SpendingRow(Guid.CreateVersion7(), null, TransactionDirection.Income, 30m, new DateOnly(2026, 9, 6), IsRefund: true),
            new SpendingRow(Guid.CreateVersion7(), null, TransactionDirection.Income, 500m, new DateOnly(2026, 9, 7)),
        };

        var match = BudgetSpendingMatcher.Match(null, window, rows, new HashSet<Guid>(), (_, _) => false);

        Assert.Equal(100m, match.Expenses);
        Assert.Equal(30m, match.Refunds);
    }
}
