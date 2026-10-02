using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.CreditCards;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Imports;

/// <summary>The official figures printed in a card statement's header, when present.</summary>
public sealed record CardStatementSummary(
    DateOnly? ClosingDate,
    DateOnly? DueDate,
    decimal? StatementBalance,
    decimal? MinimumPayment,
    decimal? CreditLimit)
{
    public bool IsEmpty => ClosingDate is null && DueDate is null && StatementBalance is null && MinimumPayment is null && CreditLimit is null;
}

/// <summary>
/// Tarjetas de crédito: what is different about importing a CARD statement into the
/// same import pipeline every account uses (parsers, deduplication, categorisation,
/// preview and confirm are shared, not copied):
/// <list type="bullet">
/// <item><b>Signs.</b> A bank statement prints a debit as negative; many card
/// statements print a purchase as POSITIVE ("cargos") and a payment as negative.
/// <see cref="Orient"/> decides deterministically, from the rows themselves, whether
/// the parsed directions must be flipped so that a purchase raises the debt.</item>
/// <item><b>Header figures.</b> <see cref="ReadSummary"/> reads corte, fecha máxima,
/// total a pagar, pago mínimo and cupo when the file prints them as label/value pairs.</item>
/// </list>
/// </summary>
public static class CreditCardStatementImport
{
    /// <summary>
    /// Returns the result with directions as they must be stored on a card (purchase =
    /// expense, payment = income) and whether they had to be flipped. Evidence, in order:
    /// rows whose text says "pago/abono" must be credits; rows that say "interés/comisión"
    /// must be charges; otherwise a card statement is mostly charges.
    /// </summary>
    public static (StatementParseResult Result, bool Flipped) Orient(StatementParseResult parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        if (!parsed.Succeeded || parsed.Transactions.Count == 0)
        {
            return (parsed, false);
        }

        var rows = parsed.Transactions;
        bool? flip = null;

        var paymentLike = rows
            .Where(r => CreditCardMovementRules.Classify(TransactionDirection.Income, r.Description) == CreditCardMovementType.Payment)
            .ToList();
        if (paymentLike.Count > 0)
        {
            var asExpense = paymentLike.Count(r => r.Direction == TransactionDirection.Expense);
            flip = asExpense * 2 > paymentLike.Count;
        }

        if (flip is null)
        {
            var chargeLike = rows
                .Where(r => CreditCardMovementRules.IsFinancialCharge(CreditCardMovementRules.Classify(TransactionDirection.Expense, r.Description)))
                .ToList();
            if (chargeLike.Count > 0)
            {
                var asIncome = chargeLike.Count(r => r.Direction == TransactionDirection.Income);
                flip = asIncome * 2 > chargeLike.Count;
            }
        }

        if (flip is null)
        {
            var incomes = rows.Count(r => r.Direction == TransactionDirection.Income);
            flip = incomes * 2 > rows.Count;
        }

        if (flip is not true)
        {
            return (parsed, false);
        }

        var flipped = rows
            .Select(r => r with
            {
                Direction = r.Direction == TransactionDirection.Income ? TransactionDirection.Expense : TransactionDirection.Income,
                RunningBalance = null,
            })
            .ToList();

        return (
            StatementParseResult.Success(
                parsed.ParserCode,
                flipped,
                parsed.Errors,
                parsed.DetectedProviderCode,
                parsed.AccountMask,
                // A bank-style closing balance means nothing once the signs are reversed.
                declaredClosingBalance: null),
            true);
    }

    /// <summary>
    /// Label/value pairs anywhere in the file: the value is the first parseable cell to
    /// the right of the label, or the cell right below it.
    /// </summary>
    public static CardStatementSummary ReadSummary(TabularTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        DateOnly? closing = null;
        DateOnly? due = null;
        decimal? balance = null;
        decimal? minimum = null;
        decimal? limit = null;

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < row.Cells.Count; c++)
            {
                var label = " " + TextNormalizer.Normalize(row.Cells[c]) + " ";
                if (label.Trim().Length == 0 || label.Length > 60)
                {
                    continue;
                }

                if (closing is null && Matches(label, ClosingLabels))
                {
                    closing = FindValue(table, r, c, TryDate);
                }
                else if (due is null && Matches(label, DueLabels))
                {
                    due = FindValue(table, r, c, TryDate);
                }
                else if (minimum is null && Matches(label, MinimumLabels))
                {
                    minimum = FindValue(table, r, c, TryAmount);
                }
                else if (balance is null && Matches(label, BalanceLabels))
                {
                    balance = FindValue(table, r, c, TryAmount);
                }
                else if (limit is null && Matches(label, LimitLabels) && !Matches(label, NotTheLimit))
                {
                    limit = FindValue(table, r, c, TryAmount);
                }
            }
        }

        return new CardStatementSummary(closing, due, balance, minimum, limit is > 0m ? limit : null);
    }

    private static bool Matches(string paddedLabel, IEnumerable<string> labels) =>
        labels.Any(l => paddedLabel.Contains(" " + l + " ", StringComparison.Ordinal));

    private static T? FindValue<T>(TabularTable table, int rowIndex, int columnIndex, Func<string, T?> parse)
        where T : struct
    {
        var row = table.Rows[rowIndex];
        for (var c = columnIndex + 1; c < row.Cells.Count; c++)
        {
            if (parse(row.Cells[c]) is { } value)
            {
                return value;
            }
        }

        if (rowIndex + 1 < table.Rows.Count && columnIndex < table.Rows[rowIndex + 1].Cells.Count)
        {
            return parse(table.Rows[rowIndex + 1].Cells[columnIndex]);
        }

        return null;
    }

    private static DateOnly? TryDate(string raw) =>
        DateParser.TryParseDate(raw, out var date) ? DateOnly.FromDateTime(date) : null;

    private static decimal? TryAmount(string raw) =>
        AmountParser.TryParse(raw, out var amount) ? Math.Abs(amount) : null;

    private static readonly string[] ClosingLabels = ["FECHA DE CORTE", "FECHA CORTE", "CORTE AL", "FECHA DE CIERRE"];

    private static readonly string[] DueLabels =
    [
        "FECHA MAXIMA DE PAGO", "FECHA MAXIMA PAGO", "FECHA LIMITE DE PAGO", "FECHA LIMITE PAGO", "PAGAR HASTA",
        "FECHA DE VENCIMIENTO", "FECHA DE PAGO",
    ];

    private static readonly string[] MinimumLabels = ["PAGO MINIMO", "MINIMO A PAGAR", "VALOR MINIMO A PAGAR", "VALOR MINIMO"];

    private static readonly string[] BalanceLabels =
    [
        "TOTAL A PAGAR", "SALDO TOTAL A PAGAR", "VALOR TOTAL A PAGAR", "PAGO TOTAL", "PAGO CONTADO", "SALDO A PAGAR", "VALOR A PAGAR",
    ];

    /// <summary>"Cupo disponible"/"cupo utilizado" are not the limit.</summary>
    private static readonly string[] NotTheLimit = ["DISPONIBLE", "UTILIZADO", "USADO"];

    private static readonly string[] LimitLabels = ["CUPO TOTAL", "CUPO AUTORIZADO", "CUPO", "LIMITE DE CREDITO", "LIMITE DE CUPO"];
}
