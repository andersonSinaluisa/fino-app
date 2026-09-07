using System.Text.Json;
using Nexo.Application.Common;
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

    /// <summary>Column spelling out "Débito"/"Crédito", when the export has one.</summary>
    public int Type { get; set; } = -1;

    /// <summary>The other party in the movement, when the export names them separately from the description.</summary>
    public int Counterparty { get; set; } = -1;

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

    /// <summary>
    /// Header cells that together identify this bank's layout. Some exports carry
    /// no letterhead at all — Pichincha's says only "Movimientos de Cuenta" — so
    /// the shape of the header row is the only evidence in the file itself.
    /// All of them must be present.
    /// </summary>
    protected virtual IReadOnlyList<string> HeaderSignatures => [];

    protected virtual IReadOnlyList<string> DateHeaders => ["FECHA", "FECHA TRANSACCION", "FECHA DE TRANSACCION", "DATE", "F TRANSACCION"];

    protected virtual IReadOnlyList<string> DescriptionHeaders => ["DESCRIPCION", "CONCEPTO", "DETALLE", "REFERENCIA DESCRIPCION", "TRANSACCION", "DESCRIPTION"];

    protected virtual IReadOnlyList<string> AmountHeaders => ["VALOR", "MONTO", "IMPORTE", "AMOUNT"];

    protected virtual IReadOnlyList<string> DebitHeaders => ["DEBITO", "DEBITOS", "CARGO", "CARGOS", "EGRESO", "DEBIT"];

    protected virtual IReadOnlyList<string> CreditHeaders => ["CREDITO", "CREDITOS", "ABONO", "ABONOS", "INGRESO", "CREDIT"];

    protected virtual IReadOnlyList<string> BalanceHeaders => ["SALDO", "SALDO CONTABLE", "SALDO DISPONIBLE", "BALANCE"];

    protected virtual IReadOnlyList<string> ReferenceHeaders => ["DOCUMENTO", "NUMERO DOCUMENTO", "REFERENCIA", "COMPROBANTE", "NRO DOCUMENTO", "REFERENCE"];

    protected virtual IReadOnlyList<string> OfficeHeaders => ["OFICINA", "CANAL", "SUCURSAL"];

    /// <summary>
    /// Column that spells the direction out ("Débito"/"Crédito"). When present it
    /// is more reliable than the sign of the amount.
    /// </summary>
    protected virtual IReadOnlyList<string> TypeHeaders =>
        ["TIPO", "TIPO MOVIMIENTO", "TIPO DE MOVIMIENTO", "TIPO TRANSACCION"];

    /// <summary>
    /// The other party in the movement, when an export names them in their own
    /// column instead of folding them into the description. Banco Guayaquil's own
    /// export does this: "Detalle" carries only the channel ("Banco Guayaquil",
    /// "IVA"), and "Beneficiario" carries the actual counterparty.
    /// </summary>
    protected virtual IReadOnlyList<string> CounterpartyHeaders =>
        ["BENEFICIARIO", "CONTRAPARTE", "PAGADO A", "RECIBIDO DE"];

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

        // Either the letterhead names this bank, or the header row has this bank's
        // distinctive shape, or the account being imported into belongs to it.
        // Never a merchant name inside a movement.
        return signatureHit
               || MatchesHeaderSignature(context.Table.Rows[headerIndex])
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

        return ParseRows(context, headerIndex, map, cancellationToken);
    }

    /// <summary>
    /// The row-walking logic every shape shares once a column map is known --
    /// whether that map came from matching header names (the normal path above)
    /// or from a user's own manual mapping (<see cref="Parsers.ManualMappingStatementParser"/>,
    /// Entregable 10). Column detection and row parsing are deliberately two
    /// separate steps so the second can be reused without the first.
    /// </summary>
    protected Task<StatementParseResult> ParseRows(
        StatementFileContext context,
        int headerIndex,
        StatementColumnMap map,
        CancellationToken cancellationToken)
    {
        var transactions = new List<ParsedStatementTransaction>();
        var errors = new List<StatementRowError>();

        // Closing balance. Comparing dates is not enough: a real statement puts two
        // movements in the same minute (a credit and the debit that follows it), and
        // then the timestamps tie and the wrong balance wins. What does disambiguate
        // them is the order of the file, so we keep the first and the last balance
        // and decide at the end which end is the most recent.
        decimal? firstBalance = null;
        decimal? lastBalance = null;
        DateTimeOffset? firstInstant = null;
        DateTimeOffset? lastInstant = null;

        // Some exports spread one movement over two physical rows: the first carries
        // date, description and amount, the second the document number. Tracking
        // where the last movement was read lets the row below it enrich it, without
        // mistaking a footer for a continuation.
        var lastMovementRow = -2;

        for (var i = headerIndex + 1; i < context.Table.RowCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = context.Table.Rows[i];
            if (row.IsEmpty)
            {
                continue;
            }

            var raw = SerializeRow(row);

            if (!DateParser.TryParseLocal(row[map.Date], out var localDate, out var timeOfDay))
            {
                if (i == lastMovementRow + 1 && TryEnrich(row, map, transactions))
                {
                    lastMovementRow = i;
                    continue;
                }

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

            var instant = context.Dates.ToInstant(localDate, timeOfDay);

            firstInstant ??= instant;
            lastInstant = instant;

            decimal? rowBalance = null;
            if (map.Balance >= 0 && AmountParser.TryParse(row[map.Balance], out var balance))
            {
                rowBalance = balance;
                firstBalance ??= balance;
                lastBalance = balance;
            }

            var reference = map.Reference >= 0 ? Clean(row[map.Reference]) : null;

            transactions.Add(new ParsedStatementTransaction(
                row.Number,
                instant,
                Math.Abs(signedAmount),
                ReadDirection(row, map, signedAmount),
                description,
                reference,
                rowBalance,
                raw));

            lastMovementRow = i;
        }

        return Task.FromResult(StatementParseResult.Success(
            ParserCode,
            transactions,
            errors,
            ProviderCode,
            ExtractAccountMask(context),
            ClosingBalance(firstInstant, lastInstant, firstBalance, lastBalance)));
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
            else if (map.Type < 0 && MatchesAny(header, TypeHeaders))
            {
                map.Type = column;
            }
            else if (map.Counterparty < 0 && MatchesAny(header, CounterpartyHeaders))
            {
                map.Counterparty = column;
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

    /// <summary>
    /// Counterparty first, then the description text, then the office/channel.
    /// Some exports (Guayaquil's HTML statement) name the counterparty in its own
    /// column, separate from "Detalle", which otherwise carries only the channel
    /// or bank name ("Banco Guayaquil", "IVA"). The categorisation engine's
    /// merchant guess (<see cref="TextNormalizer.ExtractMerchant"/>) takes the
    /// first three tokens of this combined description, and the stored
    /// <see cref="Transaction.Merchant"/> is derived from the very same field
    /// (<see cref="Transaction.Create"/>) -- so putting the counterparty first
    /// here is what makes both the live suggestion and the "correct once, learn
    /// forever" personal rule actually key on the person's name instead of the
    /// channel word, with no schema change needed on either path.
    /// </summary>
    private string BuildDescription(TabularRow row, StatementColumnMap map)
    {
        var parts = new List<string>();

        if (map.Counterparty >= 0)
        {
            var counterparty = Clean(row[map.Counterparty]);
            if (!string.IsNullOrWhiteSpace(counterparty))
            {
                parts.Add(counterparty);
            }
        }

        parts.Add(Clean(row[map.Description]) ?? string.Empty);

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

    /// <summary>
    /// The balance of whichever end of the file is chronologically last. Pichincha
    /// exports newest first, so there the closing balance is the very first row —
    /// taking the last one would report the opening balance instead.
    /// </summary>
    private static decimal? ClosingBalance(
        DateTimeOffset? firstInstant,
        DateTimeOffset? lastInstant,
        decimal? firstBalance,
        decimal? lastBalance)
    {
        if (firstInstant is null || lastInstant is null)
        {
            return null;
        }

        var newestFirst = firstInstant > lastInstant;
        return newestFirst ? firstBalance : lastBalance;
    }

    /// <summary>
    /// A row with no date, directly below a movement, and with no description of
    /// its own: it belongs to the movement above. What matters there is the bank's
    /// document number, which is the strongest deduplication key we can get.
    /// Its column is found by shape, not by header index, because merged header
    /// cells routinely sit one column away from their values.
    /// </summary>
    private static bool TryEnrich(
        TabularRow row,
        StatementColumnMap map,
        List<ParsedStatementTransaction> transactions)
    {
        if (transactions.Count == 0 || !string.IsNullOrWhiteSpace(row[map.Description]))
        {
            return false;
        }

        var last = transactions[^1];
        if (TransactionFingerprint.HasUsableReference(last.ExternalReference))
        {
            return false;
        }

        var reference = row.Cells
            .Select(c => c.Trim())
            .FirstOrDefault(c => c.Length is >= 4 and <= 24 && c.All(char.IsAsciiDigit));

        if (reference is null)
        {
            return false;
        }

        transactions[^1] = last with { ExternalReference = reference };
        return true;
    }

    /// <summary>
    /// "Débito"/"Crédito" when the export spells it out, the sign of the amount
    /// otherwise. An explicit column beats inferring from a sign convention.
    /// </summary>
    private static TransactionDirection ReadDirection(TabularRow row, StatementColumnMap map, decimal signedAmount)
    {
        if (map.Type >= 0)
        {
            var type = TextNormalizer.Normalize(row[map.Type]);

            if (type.StartsWith("DEBITO", StringComparison.Ordinal) || type.StartsWith("EGRESO", StringComparison.Ordinal))
            {
                return TransactionDirection.Expense;
            }

            if (type.StartsWith("CREDITO", StringComparison.Ordinal) || type.StartsWith("INGRESO", StringComparison.Ordinal))
            {
                return TransactionDirection.Income;
            }
        }

        return signedAmount >= 0 ? TransactionDirection.Income : TransactionDirection.Expense;
    }

    private bool MatchesHeaderSignature(TabularRow headerRow)
    {
        if (HeaderSignatures.Count == 0)
        {
            return false;
        }

        var cells = headerRow.Cells.Select(TextNormalizer.Normalize).ToArray();
        return HeaderSignatures.All(signature => cells.Any(c => c == signature));
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

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var collapsed = System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
        return CsvInjectionGuard.Neutralize(collapsed);
    }

    private static string SerializeRow(TabularRow row) =>
        JsonSerializer.Serialize(row.Cells);
}
