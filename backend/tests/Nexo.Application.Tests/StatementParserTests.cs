using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Parsers;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Application.Tests;

public class StatementParserTests
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

    private static StatementFileContext Context(string csv, string fileName = "estado.csv", string? providerHint = null) =>
        new(fileName, "text/csv", CsvTableReader.ReadFromText(csv), providerHint);

    private const string PichinchaCsv = """
        Banco Pichincha - Estado de cuenta
        Cuenta: ****4821
        Fecha,Concepto,Documento,Debito,Credito,Saldo
        01/03/2026,ACREDITACION ROL DE PAGOS,ROL-0001,,1420.00,2420.00
        02/03/2026,SUPERMAXI ALBORADA,TRX-99881,48.20,,2371.80
        05/03/2026,NETFLIX.COM,TRX-99999,12.99,,2358.81
        """;

    [Fact]
    public void The_Pichincha_parser_claims_a_Pichincha_export()
    {
        var parser = Resolver().Resolve(Context(PichinchaCsv));

        Assert.NotNull(parser);
        Assert.Equal("PICHINCHA_V1", parser.ParserCode);
    }

    [Fact]
    public async Task Debit_and_credit_columns_become_directions()
    {
        var parser = Resolver().Resolve(Context(PichinchaCsv))!;
        var result = await parser.ParseAsync(Context(PichinchaCsv), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Transactions.Count);

        var salary = result.Transactions[0];
        Assert.Equal(TransactionDirection.Income, salary.Direction);
        Assert.Equal(1420.00m, salary.Amount);
        Assert.Equal("ROL-0001", salary.ExternalReference);

        var groceries = result.Transactions[1];
        Assert.Equal(TransactionDirection.Expense, groceries.Direction);
        Assert.Equal(48.20m, groceries.Amount);
        Assert.Equal("SUPERMAXI ALBORADA", groceries.Description);
    }

    [Fact]
    public async Task Statement_dates_are_interpreted_in_the_account_holders_timezone()
    {
        var parser = Resolver().Resolve(Context(PichinchaCsv))!;
        var result = await parser.ParseAsync(Context(PichinchaCsv), CancellationToken.None);

        var first = result.Transactions[0];
        Assert.Equal(new DateTime(2026, 3, 1), StatementDateInterpreter.Ecuador().ToLocalDate(first.TransactionDate));
    }

    [Fact]
    public void The_account_mask_is_picked_up_from_the_statement_header()
    {
        var parser = Resolver().Resolve(Context(PichinchaCsv))!;
        var result = parser.ParseAsync(Context(PichinchaCsv), CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal("4821", result.AccountMask);
    }

    [Fact]
    public void A_merchant_name_in_a_movement_is_not_a_bank_signature()
    {
        // Regression: "EMPRESA ELECTRICA GUAYAQUIL" is a utility bill, not a hint
        // that this is a Banco Guayaquil export. Signatures are only read from the
        // rows above the header.
        const string csv = """
            Banco Pichincha - Estado de cuenta
            Cuenta: ****4821
            Fecha,Concepto,Documento,Debito,Credito,Saldo
            01/03/2026,ACREDITACION ROL DE PAGOS,ROL-0001,,1420.00,2420.00
            10/03/2026,EMPRESA ELECTRICA GUAYAQUIL,TRX-99233,22.35,,2397.65
            """;

        var parser = Resolver().Resolve(Context(csv));

        Assert.NotNull(parser);
        Assert.Equal("PICHINCHA_V1", parser.ParserCode);
    }

    [Fact]
    public void The_account_provider_wins_when_two_parsers_could_claim_the_file()
    {
        // No letterhead at all: the account being imported into decides.
        const string csv = """
            Fecha,Concepto,Documento,Debito,Credito
            01/03/2026,PAGO SERVICIOS,DOC-1,25.50,
            """;

        Assert.Equal(
            "GUAYAQUIL_V1",
            Resolver().Resolve(Context(csv, providerHint: ProviderCodes.Guayaquil))!.ParserCode);

        Assert.Equal(
            "PRODUBANCO_V1",
            Resolver().Resolve(Context(csv, providerHint: ProviderCodes.Produbanco))!.ParserCode);
    }

    [Fact]
    public void An_unknown_bank_falls_back_to_the_generic_parser()
    {
        const string csv = """
            Fecha;Descripcion;Valor
            04/03/2026;PAGO SERVICIOS;-25,50
            """;

        var parser = Resolver().Resolve(Context(csv));

        Assert.NotNull(parser);
        Assert.Equal("GENERIC_V1", parser.ParserCode);
    }

    [Fact]
    public async Task A_single_signed_amount_column_is_supported()
    {
        const string csv = """
            Fecha;Descripcion;Valor
            04/03/2026;PAGO SERVICIOS;-25,50
            05/03/2026;TRANSFERENCIA RECIBIDA;350,00
            """;

        var parser = Resolver().Resolve(Context(csv))!;
        var result = await parser.ParseAsync(Context(csv), CancellationToken.None);

        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal(TransactionDirection.Expense, result.Transactions[0].Direction);
        Assert.Equal(25.50m, result.Transactions[0].Amount);
        Assert.Equal(TransactionDirection.Income, result.Transactions[1].Direction);
    }

    [Fact]
    public void Critical_case_8_a_file_no_parser_understands_is_rejected_not_guessed()
    {
        const string garbage = """
            Esto no es un estado de cuenta
            solo texto suelto
            sin columnas reconocibles
            """;

        Assert.Null(Resolver().Resolve(Context(garbage, "notas.txt")));
    }

    [Fact]
    public void Critical_case_3_an_empty_file_is_rejected() =>
        Assert.Null(Resolver().Resolve(Context(string.Empty)));

    [Fact]
    public async Task Critical_case_10_bad_rows_are_reported_and_the_good_ones_still_import()
    {
        const string csv = """
            Fecha,Concepto,Debito,Credito
            01/03/2026,ACREDITACION ROL DE PAGOS,,1420.00
            SALDO FINAL,,,
            no-es-fecha,COMPRA CON ERROR,10.00,
            03/03/2026,SUPERMAXI ALBORADA,48.20,
            04/03/2026,MOVIMIENTO SIN VALOR,,
            """;

        var parser = Resolver().Resolve(Context(csv))!;
        var result = await parser.ParseAsync(Context(csv), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Transactions.Count);
        Assert.Contains(result.Errors, e => e.Message.Contains("Fecha no reconocida", StringComparison.Ordinal));
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void The_provider_hint_lets_a_bank_parser_claim_an_export_without_a_branded_header()
    {
        const string csv = """
            Fecha,Concepto,Debito,Credito
            01/03/2026,PAGO SERVICIOS,25.50,
            """;

        var parser = Resolver().Resolve(Context(csv, providerHint: ProviderCodes.Pichincha));

        Assert.NotNull(parser);
        Assert.Equal("PICHINCHA_V1", parser.ParserCode);
    }

    [Fact]
    public async Task An_xlsx_export_goes_through_the_same_parsers()
    {
        var bytes = XlsxBuilder.Build(
        [
            ["Banco Guayaquil - Movimientos", "", "", ""],
            ["Fecha", "Descripcion", "Debito", "Credito"],
            ["2026-03-04", "PRIMAX VIA DAULE", "35", ""],
            ["2026-03-05", "TRANSFERENCIA RECIBIDA", "", "120"],
        ]);

        using var stream = new MemoryStream(bytes);
        var table = XlsxTableReader.Read(stream);
        var context = new StatementFileContext("movimientos.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", table);

        var parser = Resolver().Resolve(context);
        Assert.NotNull(parser);
        Assert.Equal("GUAYAQUIL_V1", parser.ParserCode);

        var result = await parser.ParseAsync(context, CancellationToken.None);

        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal(35m, result.Transactions[0].Amount);
        Assert.Equal(TransactionDirection.Income, result.Transactions[1].Direction);
    }
}
