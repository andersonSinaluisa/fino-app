using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Parsers;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Application.Tests;

/// <summary>
/// The XLSX that Banco Pichincha's web banking exports has a shape none of the
/// obvious assumptions cover: the header sits on row 6, each movement spans two
/// physical rows, the date carries a 12-hour clock, the direction is spelled out
/// in its own column, and the rows run newest first.
///
/// These fixtures reproduce that layout with entirely invented amounts, names and
/// document numbers. No real statement is stored in this repository.
/// </summary>
public class PichinchaXlsxStatementTests
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

    /// <summary>Places values at the column indexes the real export uses.</summary>
    private static string[] Row(params (int Column, string Value)[] cells)
    {
        var row = new string[16];
        Array.Fill(row, string.Empty);

        foreach (var (column, value) in cells)
        {
            row[column] = value;
        }

        return row;
    }

    /// <summary>
    /// One interbank transfer and the two charges it drags along, all three under
    /// the same document number, plus an older incoming transfer. Newest first.
    /// </summary>
    private static byte[] Statement() => XlsxBuilder.Build(
    [
        Row(),
        Row((5, "Movimientos de Cuenta")),
        Row(),
        Row((1, "*Las transacciones de fines de semana o feriados se registran el siguiente día laborable.")),
        Row(),
        Row((3, "Fecha"), (4, "Concepto"), (8, "Nro. Documento"), (10, "Tipo"),
            (13, "Beneficiario"), (14, "Monto"), (15, "Saldo")),

        Row((3, "2026-8-31, 12:51 PM"), (4, "TRANSFERENCIA INTERBANCARIA A PROVEEDOR DEMO"),
            (10, "Débito"), (14, "-$116,00"), (15, "$0,86")),
        Row((7, "90000001"), (13, "******1234")),
        Row(),
        Row(),

        Row((3, "2026-8-31, 12:51 PM"), (4, "IVA COBRADO"),
            (10, "Débito"), (14, "-$0,05"), (15, "$116,86")),
        Row((7, "90000001")),
        Row(),
        Row(),

        Row((3, "2026-8-31, 12:51 PM"), (4, "COMISION TRANSFERENCIA INTERBANCARIA ENVIADA"),
            (10, "Débito"), (14, "-$0,36"), (15, "$116,91")),
        Row((7, "90000001")),
        Row(),
        Row(),

        Row((3, "2026-8-03, 1:01 PM"), (4, "TRANSFERENCIA RECIBIDA DE CLIENTE DEMO"),
            (10, "Crédito"), (14, "$117,27"), (15, "$117,27")),
        Row((7, "90000002"), (13, "******5678")),
    ]);

    private static StatementFileContext Context(string? providerHint = null)
    {
        using var stream = new MemoryStream(Statement());

        return new StatementFileContext(
            "Movimientos_cuenta_20260801_20260831.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            XlsxTableReader.Read(stream),
            providerHint);
    }

    [Fact]
    public void The_header_row_is_found_even_though_it_is_the_sixth()
    {
        var parser = Resolver().Resolve(Context());

        Assert.NotNull(parser);
        Assert.Equal("PICHINCHA_V1", parser.ParserCode);
    }

    [Fact]
    public void The_export_is_recognised_without_a_hint_because_of_its_header_shape()
    {
        // The file never names the bank: its heading says only "Movimientos de
        // Cuenta". The header combination is the only evidence inside the file.
        Assert.Equal("PICHINCHA_V1", Resolver().Resolve(Context())!.ParserCode);
    }

    [Fact]
    public async Task Every_movement_is_read_and_none_is_reported_as_an_error()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Transactions.Count);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task The_document_number_is_taken_from_the_second_row_of_each_movement()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.All(result.Transactions, t => Assert.False(string.IsNullOrWhiteSpace(t.ExternalReference)));
        Assert.Equal("90000001", result.Transactions[0].ExternalReference);
        Assert.Equal("90000002", result.Transactions[3].ExternalReference);
    }

    [Fact]
    public async Task The_direction_comes_from_the_Tipo_column()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.Equal(3, result.Transactions.Count(t => t.Direction == TransactionDirection.Expense));
        Assert.Equal(1, result.Transactions.Count(t => t.Direction == TransactionDirection.Income));
    }

    [Fact]
    public async Task The_time_of_day_survives_the_import()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        // 12:51 PM in Guayaquil is 17:51 UTC.
        Assert.Equal(new DateTime(2026, 8, 31, 17, 51, 0), result.Transactions[0].TransactionDate.UtcDateTime);
    }

    [Fact]
    public async Task The_closing_balance_is_the_newest_row_not_the_last_one_read()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        // The file runs newest first, so the last row read is the oldest. Two
        // movements even share a minute, which is why the order of the file — not a
        // date comparison — decides.
        Assert.Equal(0.86m, result.DeclaredClosingBalance);
    }

    [Fact]
    public async Task A_transfer_and_its_charges_share_a_document_number_and_stay_three_movements()
    {
        // The regression this whole fixture exists for: on a real statement, keying
        // on the document number alone collapsed 54 movements into 34.
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);
        var account = Guid.CreateVersion7();

        var fingerprints = result.Transactions
            .Select(t => TransactionFingerprint.Compute(
                "PICHINCHA", account, t.ExternalReference, t.TransactionDate, t.Amount, t.Direction, t.Description))
            .ToList();

        Assert.Equal(4, fingerprints.Distinct().Count());

        var underOneDocument = result.Transactions.Where(t => t.ExternalReference == "90000001").ToList();
        Assert.Equal(3, underOneDocument.Count);
        Assert.Equal(new[] { 116.00m, 0.05m, 0.36m }, underOneDocument.Select(t => t.Amount).ToArray());
    }

    [Fact]
    public async Task Amounts_written_as_dollar_with_comma_decimals_are_read_correctly()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.Equal(116.00m, result.Transactions[0].Amount);
        Assert.Equal(0.05m, result.Transactions[1].Amount);
        Assert.Equal(117.27m, result.Transactions[3].Amount);
    }
}
