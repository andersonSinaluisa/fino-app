using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Parsers;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Application.Tests;

/// <summary>
/// Entregable 10 (Generic CSV mapper): unit coverage for the parser that runs a
/// user's own by-hand column choices through the same row-walking, amount and
/// balance logic every bank-specific parser uses. This class is never returned
/// by <see cref="StatementParserResolver"/> -- <see cref="ImportService"/>
/// constructs it directly once a mapping is known, so these tests build one by
/// hand too, exactly as the service does.
/// </summary>
public class ManualMappingStatementParserTests
{
    // A layout no bundled parser recognises: Spanish headers with abbreviations
    // none of the synonym lists contain, and no letterhead at all.
    private const string UnrecognisableCsv = """
        F.Mov;Glosa;Ent;Sal;Doc
        01-02-2026;PAGO PROVEEDOR X;;85.00;DOC001
        03-02-2026;COBRO CLIENTE Y;220.50;;DOC002
        05-02-2026;COMISION MANEJO;;3.50;DOC003
        """;

    private static StatementFileContext Context(string csv = UnrecognisableCsv) =>
        new("estado_raro.csv", "text/csv", CsvTableReader.ReadFromText(csv));

    [Fact]
    public void No_bundled_parser_recognises_the_unusual_header_abbreviations()
    {
        IStatementParser[] all =
        [
            new PichinchaStatementParser(),
            new GuayaquilStatementParser(),
            new ProdubancoStatementParser(),
            new PacificoStatementParser(),
            new GenericStatementParser(),
        ];

        var resolver = new StatementParserResolver(all, Microsoft.Extensions.Logging.Abstractions.NullLogger<StatementParserResolver>.Instance);

        Assert.Null(resolver.Resolve(Context()));
    }

    [Fact]
    public async Task A_hand_picked_mapping_with_separate_debit_and_credit_columns_parses_every_row()
    {
        // Columns are zero-based: F.Mov=0, Glosa=1, Ent(credit)=2, Sal(debit)=3, Doc=4.
        var map = new StatementColumnMap { Date = 0, Description = 1, Credit = 2, Debit = 3, Reference = 4 };
        var parser = new ManualMappingStatementParser("PROVEEDOR_DESCONOCIDO", map, firstRowIsHeader: true);

        var result = await parser.ParseAsync(Context(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("MANUAL_MAPPING_V1", result.ParserCode);
        Assert.Equal(3, result.Transactions.Count);
        Assert.Empty(result.Errors);

        Assert.Equal(TransactionDirection.Expense, result.Transactions[0].Direction);
        Assert.Equal(85.00m, result.Transactions[0].Amount);
        Assert.Equal("DOC001", result.Transactions[0].ExternalReference);

        Assert.Equal(TransactionDirection.Income, result.Transactions[1].Direction);
        Assert.Equal(220.50m, result.Transactions[1].Amount);

        Assert.Equal(TransactionDirection.Expense, result.Transactions[2].Direction);
        Assert.Equal(3.50m, result.Transactions[2].Amount);
    }

    [Fact]
    public async Task When_the_file_truly_has_no_header_row_first_row_is_header_false_keeps_row_zero()
    {
        const string noHeaderCsv = """
            01-02-2026;PAGO PROVEEDOR X;;85.00;DOC001
            03-02-2026;COBRO CLIENTE Y;220.50;;DOC002
            """;

        var map = new StatementColumnMap { Date = 0, Description = 1, Credit = 2, Debit = 3, Reference = 4 };
        var parser = new ManualMappingStatementParser("PROVEEDOR_DESCONOCIDO", map, firstRowIsHeader: false);

        var result = await parser.ParseAsync(new StatementFileContext(
            "sin_encabezado.csv", "text/csv", CsvTableReader.ReadFromText(noHeaderCsv)), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Transactions.Count);
    }

    [Fact]
    public async Task An_incomplete_mapping_missing_the_amount_source_reports_every_row_as_an_error_not_a_crash()
    {
        // Only Date and Description mapped: HasAmountSource is false, but ParseRows
        // never checks IsUsable (that gate is TryFindHeader's job, which this parser
        // bypasses) -- TryReadAmount simply fails for every row, which is the
        // correct, contained failure mode for a mapping the caller got wrong.
        var incomplete = new StatementColumnMap { Date = 0, Description = 1 };
        var parser = new ManualMappingStatementParser("X", incomplete, firstRowIsHeader: true);

        var result = await parser.ParseAsync(Context(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Transactions);
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void CanParse_always_returns_false_because_the_resolver_must_never_pick_this_up_on_its_own()
    {
        var parser = new ManualMappingStatementParser("X", new StatementColumnMap { Date = 0, Description = 1, Amount = 2 }, true);

        Assert.False(parser.CanParse(Context()));
    }
}
