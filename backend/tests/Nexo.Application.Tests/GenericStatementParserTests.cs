using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Parsers;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Application.Tests;

/// <summary>
/// Entregable 9's fourth item: the generic CSV fallback that lets a user import
/// from an institution Nexo has no dedicated parser for -- as long as the file
/// has recognisable date/description/amount columns. GenericStatementParser
/// existed with zero test coverage before this. Its real column-mapping
/// wizard (letting a user who doesn't even have those headers point Nexo at
/// the right columns by hand) is Entregable 10, not this one.
///
/// The institution name and every figure below are invented; the point is a
/// bank Nexo has never heard of, not the fictional cooperative's plausibility.
/// </summary>
public class GenericStatementParserTests
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

    // A single signed "Valor" column, no separate Debito/Credito -- the other
    // common shape HeaderMappedStatementParser supports, unlike the fixtures
    // used for Produbanco/Pacífico.
    private const string Csv = """
        Cooperativa Ahorro Total
        Fecha,Descripcion,Valor,Saldo
        02/03/2026,DEPOSITO EFECTIVO,150.00,650.00
        04/03/2026,RETIRO CAJERO,-60.00,590.00
        06/03/2026,COMPRA FARMACIA DEMO,-22.75,567.25
        """;

    private static StatementFileContext Context(string? providerHint = null) =>
        new(
            "movimientos.csv",
            "text/csv",
            CsvTableReader.ReadFromText(Csv),
            providerHint);

    [Fact]
    public void An_institution_with_no_dedicated_parser_falls_through_to_the_generic_one()
    {
        var parser = Resolver().Resolve(Context());

        Assert.NotNull(parser);
        Assert.Equal("GENERIC_V1", parser.ParserCode);
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
    public async Task Direction_comes_from_the_sign_of_the_single_amount_column()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.Equal(TransactionDirection.Income, result.Transactions[0].Direction);
        Assert.Equal(150.00m, result.Transactions[0].Amount);

        Assert.Equal(TransactionDirection.Expense, result.Transactions[1].Direction);
        Assert.Equal(60.00m, result.Transactions[1].Amount);

        Assert.Equal(TransactionDirection.Expense, result.Transactions[2].Direction);
        Assert.Equal(22.75m, result.Transactions[2].Amount);
    }

    [Fact]
    public async Task The_closing_balance_still_reconciles_with_no_bank_specific_parser_involved()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.Equal(567.25m, result.DeclaredClosingBalance);
    }

    [Fact]
    public async Task A_file_missing_even_the_generic_columns_fails_cleanly()
    {
        const string unparseable = """
            Cooperativa Ahorro Total
            Nombre,Cedula,Telefono
            Juan Perez,0000000000,0999999999
            """;

        var context = new StatementFileContext(
            "no_es_un_estado_de_cuenta.csv",
            "text/csv",
            CsvTableReader.ReadFromText(unparseable));

        Assert.Null(Resolver().Resolve(context));
    }
}
