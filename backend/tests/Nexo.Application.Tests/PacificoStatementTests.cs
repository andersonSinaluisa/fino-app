using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Parsers;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Application.Tests;

/// <summary>
/// Entregable 9: same gap as ProdubancoStatementParser -- PacificoStatementParser
/// has existed since the early parser work with zero test coverage, built on
/// assumed column names, never checked against a real Banco del Pacífico
/// export. This proves the parser's own logic (column mapping, direction,
/// balance reconciliation) is sound for that assumed shape; it does not prove
/// the shape itself is what the bank actually ships. See the Entregable 9
/// report.
///
/// All amounts, names and reference numbers below are invented. No real
/// statement is stored in this repository.
/// </summary>
public class PacificoStatementTests
{
    private static readonly IStatementParser[] AllParsers =
    [
        new PichinchaStatementParser(),
        new GuayaquilStatementParser(),
        new ProdubancoStatementParser(),
        new PacificoStatementParser(),
        new GenericStatementParser(),
    ];

    private static StatementParserResolver Resolver() =>
        new(AllParsers, NullLogger<StatementParserResolver>.Instance);

    // Newest first this time, deliberately -- same reason as the Pichincha
    // fixture: it proves the closing balance is picked by chronological order,
    // not by which row physically comes last in the file.
    private const string Csv = """
        BANCO DEL PACIFICO
        Fecha,Descripcion,Referencia,Debito,Credito,Saldo
        05/03/2026,PAGO SERVICIOS BASICOS DEMO,REF-3003,18.90,,542.10
        03/03/2026,TRANSFERENCIA RECIBIDA DEMO,REF-3002,,200.00,561.00
        01/03/2026,RETIRO CAJERO DEMO,,40.00,,361.00
        """;

    private static StatementFileContext Context(string? providerHint = null) =>
        new(
            "movimientos_pacifico.csv",
            "text/csv",
            CsvTableReader.ReadFromText(Csv),
            providerHint);

    [Fact]
    public void The_file_is_claimed_by_its_letterhead_not_a_provider_hint()
    {
        var parser = Resolver().Resolve(Context());

        Assert.NotNull(parser);
        Assert.Equal("PACIFICO_V1", parser.ParserCode);
    }

    [Fact]
    public async Task Every_movement_is_read_and_none_is_reported_as_an_error()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Transactions.Count);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Direction_and_amount_come_from_the_debito_credito_columns()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.Equal(2, result.Transactions.Count(t => t.Direction == TransactionDirection.Expense));
        Assert.Equal(1, result.Transactions.Count(t => t.Direction == TransactionDirection.Income));

        Assert.Equal(18.90m, result.Transactions[0].Amount);
        Assert.Equal(200.00m, result.Transactions[1].Amount);
        Assert.Equal(40.00m, result.Transactions[2].Amount);
    }

    [Fact]
    public async Task The_reference_column_becomes_the_external_reference_when_present()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.Equal("REF-3003", result.Transactions[0].ExternalReference);
        Assert.Equal("REF-3002", result.Transactions[1].ExternalReference);
        Assert.Null(result.Transactions[2].ExternalReference);
    }

    [Fact]
    public async Task The_closing_balance_is_the_newest_row_not_the_last_one_read()
    {
        // The file runs newest first, so the last row physically read (01/03) is
        // actually the oldest movement -- taking it would report an old balance
        // as if it were current.
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.Equal(542.10m, result.DeclaredClosingBalance);
    }
}
