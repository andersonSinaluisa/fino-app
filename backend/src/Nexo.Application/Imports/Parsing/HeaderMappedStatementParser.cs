using System.Text.Json;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Imports.Parsing;

/// <summary>
/// Column roles a statement can expose. A bank uses either a single signed amount
/// column or a debit/credit pair; both shapes are supported.
/// </summary>
public sealed class StatementColumnMap
{
    public int Date { get; set; } = -1;

    public int Description { get; set; } = -1;

    public int Amount { get; set; } = -1;

    public int Debit { get; set; } = -1;

    public int Credit { get; set; } = -1;

    public int Balance { get; set; } = -1;

    public int Reference { get; set; } = -1;

    public int Office { get; set; } = -1;

    public bool HasAmountSource => Amount >= 0 || Debit >= 0 || Credit >= 0;

    public bool IsUsable => Date >= 0 && Description >= 0 && HasAmountSource;
}

/// <summary>
/// Shared machinery for every file-based statement parser: locate the header row,
/// map columns by synonym, then turn each remaining row into a movement.
/// A bank parser only declares its synonyms and its file signature.
/// </summary>
public abstract class HeaderMappedStatementParser : IStatementParser
{
    public abstract string ParserCode { get; }

    public abstract string? ProviderCode { get; }

    public virtual int Priority => 100;

    /// <summary>Words that must appear in the first rows for this parser to claim the file.</summary>
    protected abstract IReadOnlyList<string> FileSignatures { get; }

    protected virtual IReadOnlyList<string> DateHeaders => ["FECHA", "FECHA TRANSACCION", "FECHA DE TRANSACCION", "DATE", "F TRANSACCION"];

    protected virtual IReadOnlyList<string> DescriptionHeaders => ["DESCRIPCION", "CONCEPTO", "DETALLE", "REFERENCIA DESCRIPCION", "TRANSACCION", "DESCRIPTION"];

    protected virtual IReadOnlyList<string> AmountHeaders => ["VALOR", "MONTO", "IMPORTE", "AMOUNT"];

    protected virtual IReadOnlyList<string> DebitHeaders => ["DEBITO", "DEBITOS", "CARGO", "CARGOS", "EGRESO", "DEBIT"];

    protected virtual IReadOnlyList<string> CreditHeaders => ["CREDITO", "CREDITOS", "ABONO", "ABONOS", "INGRESO", "CREDIT"];

    protected virtual IReadOnlyList<string> BalanceHeaders => ["SALDO", "SALDO CONTABLE", "SALDO DISPONIBLE", "BALANCE"];

    protected virtual IReadOnlyList<string> ReferenceHeaders => ["DOCUMENTO", "NUMERO DOCUMENTO", "REFERENCIA", "COMPROBANTE", "NRO DOCUMENTO", "REFERENCE"];

    protected virtual IReadOnlyList<string> OfficeHeaders => ["OFICINA", "CANAL", "SUCURSAL"];

    /// <summary>
    /// Some statements express expenses as negative values in a single column;
    /// others use a positive debit column. Overridden where a bank inverts the sign.
    /// </summary>
    protected virtual bool ExpensesArePositiveInDebitColumn => true;

    public virtual bool CanParse(StatementFileContext context)
    {
        if (context.Table.RowCount == 0 || !TryFindHeader(context.Table, out var headerIndex, out _))
        {
            return false;
        }

        var heading = context.HeadingText(headerIndex);
        var signatureHit = FileSignatures.Count == 0
            || FileSignatures.Any(s => heading.Contains(s, StringComparison.Ordinal));

        // Either the letterhead names this bank, or the account the user is
        // importing into belongs to it. Never a merchant name in a movement.
        return signatureHit
               || string.Equals(context.ProviderCodeHint, ProviderCode, StringComparison.OrdinalIgnoreCase);
    }

    public virtual Task<StatementParseResult> ParseAsync(StatementFileContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryFindHeader(context.Table, out var headerIndex, out var map))
        {
            return Task.FromResult(StatementParseResult.Failure(
                ParserCode,
                "No se encontró una fila de encabezados con las columnas mínimas (fecha, descripción y valor)."));
        }

        var transactions = new List<ParsedStatementTransaction>();
        var errors = new List<StatementRowError>();

        // Closing balance = the balance on the latest-dated row. Taking the last row
        // read would give the opening balance on a newest-first export.
        decimal? closingBalance = null;
        DateTimeOffset? closingBalanceAt = null;

        for (var i = headerIndex + 1; i < context.Table.RowCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = context.Table.Rows[i];
            if (row.IsEmpty)
            {
                continue;
            }

            var raw = SerializeRow(row);

            if (!DateParser.TryParseDate(row[map.Date], out var localDate))
            {
                // Statement footers ("Saldo final", totals) land here; only complain
                // when the row actually looks like a movement.
                if (LooksLikeMovement(row, map))
                {
                    errors.Add(new StatementRowError(row.Number, $"Fecha no reconocida: '{row[map.Date]}'.", raw));
                }

                continue;
            }

            if (!TryReadAmount(row, map, out var signedAmount))
            {
                errors.Add(new StatementRowError(row.Number, "No se pudo leer el valor del movimiento.", raw));
                continue;
            }

            if (signedAmount == 0m)
            {
                errors.Add(new StatementRowError(row.Number, "El movimiento tiene valor cero.", raw));
                continue;
            }

            var description = BuildDescription(row, map);
            if (string.IsNullOrWhiteSpace(description))
            {
                errors.Add(new StatementRowError(row.Number, "El movimiento no tiene descripción.", raw));
                continue;
            }

            var instant = context.Dates.ToInstant(localDate);

            decimal? rowBalance = null;
            if (map.Balance >= 0 && AmountParser.TryParse(row[map.Balance], out var balance))
            {
                rowBalance = balance;
                if (closingBalanceAt is null || instant >= closingBalanceAt)
                {
                    closingBalance = balance;
                    closingBalanceAt = instant;
                }
            }

            var reference = map.Reference >= 0 ? Clean(row[map.Reference]) : null;

            transactions.Add(new ParsedStatementTransaction(
                row.Number,
                instant,
                Math.Abs(signedAmount),
                signedAmount >= 0 ? TransactionDirection.Income : TransactionDirection.Expense,
                description,
                reference,
                rowBalance,
                raw));
        }

        return Task.FromResult(StatementParseResult.Success(
            ParserCode,
            transactions,
            errors,
            ProviderCode,
            ExtractAccountMask(context),
            closingBalance));
    }

    protected bool TryFindHeader(TabularTable table, out int headerIndex, out StatementColumnMap map)
    {
        headerIndex = -1;
        map = new StatementColumnMap();

        var limit = Math.Min(table.RowCount, 30);
        for (var i = 0; i < limit; i++)
        {
            var candidate = BuildMap(table.Rows[i]);
            if (candidate.IsUsable)
            {
                headerIndex = i;
                map = candidate;
                return true;
            }
        }

        return false;
    }

    private StatementColumnMap BuildMap(TabularRow row)
    {
        var map = new StatementColumnMap();

        for (var column = 0; column < row.Cells.Count; column++)
        {
            var header = TextNormalizer.Normalize(row[column]);
            if (header.Length == 0)
            {
                continue;
            }

            if (map.Date < 0 && MatchesAny(header, DateHeaders))
            {
                map.Date = column;
            }
            else if (map.Description < 0 && MatchesAny(header, DescriptionHeaders))
            {
                map.Description = column;
            }
            else if (map.Debit < 0 && MatchesAny(header, DebitHeaders))
            {
                map.Debit = column;
            }
            else if (map.Credit < 0 && MatchesAny(header, CreditHeaders))
            {
                map.Credit = column;
            }
            else if (map.Balance < 0 && MatchesAny(header, BalanceHeaders))
            {
                map.Balance = column;
            }
            else if (map.Amount < 0 && MatchesAny(header, AmountHeaders))
            {
                map.Amount = column;
            }
            else if (map.Reference < 0 && MatchesAny(header, ReferenceHeaders))
            {
                map.Reference = column;
            }
            else if (map.Office < 0 && MatchesAny(header, OfficeHeaders))
            {
                map.Office = column;
            }
        }

        return map;
    }

    private static bool MatchesAny(string header, IReadOnlyList<string> candidates) =>
        candidates.Any(c =>
            string.Equals(header, c, StringComparison.Ordinal)
            || header.StartsWith(c + " ", StringComparison.Ordinal)
            || header.EndsWith(" " + c, StringComparison.Ordinal));

    private bool TryReadAmount(TabularRow row, StatementColumnMap map, out decimal signedAmount)
    {
        signedAmount = 0m;

        var debit = ReadCell(row, map.Debit);
        var credit = ReadCell(row, map.Credit);

        if (debit != 0m || credit != 0m)
        {
            var outflow = ExpensesArePositiveInDebitColumn ? Math.Abs(debit) : -Math.Abs(debit);
            signedAmount = Math.Abs(credit) - outflow;
            return signedAmount != 0m;
        }

        if (map.Amount >= 0 && AmountParser.TryParse(row[map.Amount], out var amount))
        {
            signedAmount = amount;
            return true;
        }

        return false;
    }

    private static decimal ReadCell(TabularRow row, int column) =>
        column >= 0 && AmountParser.TryParse(row[column], out var value) ? value : 0m;

    private string BuildDescription(TabularRow row, StatementColumnMap map)
    {
        var parts = new List<string> { Clean(row[map.Description]) ?? string.Empty };

        if (map.Office >= 0)
        {
            var office = Clean(row[map.Office]);
            if (!string.IsNullOrWhiteSpace(office))
            {
                parts.Add(office);
            }
        }

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
    }

    private static bool LooksLikeMovement(TabularRow row, StatementColumnMap map)
    {
        var description = row[map.Description];
        return !string.IsNullOrWhiteSpace(description) && description.Trim().Length > 3;
    }

    protected virtual string? ExtractAccountMask(StatementFileContext context)
    {
        var limit = TryFindHeader(context.Table, out var headerIndex, out _) && headerIndex > 0
            ? headerIndex
            : Math.Min(context.Table.RowCount, 15);

        foreach (var row in context.Table.Rows.Take(limit))
        {
            var text = row.Join();
            var index = text.IndexOf("****", StringComparison.Ordinal);
            if (index >= 0)
            {
                var digits = new string(text[index..].Where(char.IsAsciiDigit).Take(4).ToArray());
                if (digits.Length == 4)
                {
                    return digits;
                }
            }
        }

        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");

    private static string SerializeRow(TabularRow row) =>
        JsonSerializer.Serialize(row.Cells);
}
