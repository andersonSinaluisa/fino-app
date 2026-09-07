using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Parsers;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Application.Tests;

/// <summary>
/// Entregable 9: ProdubancoStatementParser has existed since the early parser
/// work but never had a single test -- it was built on the column names
/// typical of Ecuadorian bank exports (FECHA/DESCRIPCION/DEBITO/CREDITO/SALDO),
/// never against a real Produbanco file. This suite proves the parser's own
/// logic is sound against that assumed shape; it is NOT a claim that the shape
/// itself matches what Produbanco actually exports -- that still needs a real
/// file from the bank, same as Banco Guayaquil needed one before Entregable 6
/// could be trusted. See the Entregable 9 report for that gap.
///
/// All amounts, names and document numbers below are invented. No real
/// statement is stored in this repository.
/// </summary>
public class ProdubancoStatementTests
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

    // Oldest first -- the shape most plain CSV/XLSX bank exports use, unlike
    // Pichincha's web-banking XLSX which runs newest first.
    private const string Csv = """
        PRODUBANCO
        Fecha,Descripcion,Documento,Debito,Credito,Saldo
        01/03/2026,COMPRA SUPERMAXI DEMO,,45.30,,954.70
        03/03/2026,DEPOSITO CHEQUE DEMO,DEP-1002,,300.00,1254.70
        05/03/2026,PAGO TARJETA CREDITO DEMO,REF-2003,120.00,,1134.70
        """;

    private static StatementFileContext Context(string? providerHint = null) =>
        new(
            "movimientos_produbanco.csv",
            "text/csv",
            CsvTableReader.ReadFromText(Csv),
            providerHint);

    [Fact]
    public void The_file_is_claimed_by_its_letterhead_not_a_provider_hint()
    {
        var parser = Resolver().Resolve(Context());

        Assert.NotNull(parser);
        Assert.Equal("PRODUBANCO_V1", parser.ParserCode);
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

        Assert.Equal(TransactionDirection.Expense, result.Transactions[0].Direction);
        Assert.Equal(45.30m, result.Transactions[0].Amount);

        Assert.Equal(TransactionDirection.Income, result.Transactions[1].Direction);
        Assert.Equal(300.00m, result.Transactions[1].Amount);

        Assert.Equal(TransactionDirection.Expense, result.Transactions[2].Direction);
        Assert.Equal(120.00m, result.Transactions[2].Amount);
    }

    [Fact]
    public async Task The_document_number_becomes_the_external_reference_when_present()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.Null(result.Transactions[0].ExternalReference);
        Assert.Equal("DEP-1002", result.Transactions[1].ExternalReference);
        Assert.Equal("REF-2003", result.Transactions[2].ExternalReference);
    }

    [Fact]
    public async Task The_closing_balance_reconciles_to_the_last_movement_in_this_oldest_first_file()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.Equal(1134.70m, result.DeclaredClosingBalance);
    }
}
