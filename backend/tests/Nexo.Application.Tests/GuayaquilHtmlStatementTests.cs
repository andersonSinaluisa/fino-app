using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Parsers;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Application.Tests;

/// <summary>
/// The real Banco Guayaquil export -- from the personal-banking portal, saved
/// with a ".xls" extension -- is not a spreadsheet at all: it is an HTML document
/// (see Estado_de_cuenta_*.xls examined during the 2026-09-05 audit). Its shape
/// is nothing like the Debito/Credito CSV the original GUAYAQUIL_V1 fixture
/// assumed: one signed "Monto" column, direction spelled out in "Tipo"
/// (Ingreso/Egreso), and the counterparty named in its own "Beneficiario" column
/// separate from the channel in "Detalle". The bank's own branding is a CSS
/// color, never text in the file, so content-based detection has to work off the
/// header row's shape alone.
///
/// This fixture reproduces that exact HTML structure -- same tags, same column
/// order -- with entirely invented dates, amounts and names. No real statement
/// is stored in this repository.
/// </summary>
public class GuayaquilHtmlStatementTests
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
        new(AllParsers, Microsoft.Extensions.Logging.Abstractions.NullLogger<StatementParserResolver>.Instance);

    /// <summary>
    /// Mirrors the real export: a summary block with no mention of the bank's
    /// name anywhere in its text, then the movements table -- newest first, one
    /// signed amount per row, direction and counterparty each in their own
    /// column. Two Egresos and one Ingreso, all fictitious.
    /// </summary>
    private const string Html = """
        <html>
        <head>
          <meta charset="UTF-8">
          <style>
            table { border-collapse: collapse; }
            .header-cell { color: #e0007a; font-weight: 700; }
          </style>
        </head>
        <body>
          <table>
            <tr>
              <td class="brand-block" colspan="4"></td>
              <td colspan="4" rowspan="2">¿Cómo usar este archivo?</td>
            </tr>
            <tr><td colspan="2">Resumen del periodo</td></tr>
            <tr><td>Ingresos</td><td>$300,00</td></tr>
            <tr><td>Egresos</td><td>$92,50</td></tr>
            <tr><td>Saldo del periodo</td><td>$207,50</td></tr>
            <tr class="title-row"><th colspan="8">Movimientos</th></tr>
            <tr>
              <th class="header-cell">Fecha</th>
              <th class="header-cell">Tipo</th>
              <th class="header-cell">Monto</th>
              <th class="header-cell">Detalle</th>
              <th class="header-cell">Beneficiario</th>
              <th class="header-cell">Fecha contable</th>
              <th class="header-cell">Saldo efectivo</th>
              <th class="header-cell">Categoría</th>
            </tr>
            <tr>
              <td>07/03/2026</td><td>Ingreso</td><td>$300,00</td><td>Transferencia</td>
              <td>MORAN VITERI CARLOS ENRIQUE</td><td>07/03/2026</td><td>$207,50</td><td></td>
            </tr>
            <tr>
              <td>06/03/2026</td><td>Egreso</td><td>$60,00</td><td>Banco Guayaquil</td>
              <td>CAFE DEMO DEL PARQUE</td><td>06/03/2026</td><td>-$92,50</td><td></td>
            </tr>
            <tr>
              <td>05/03/2026</td><td>Egreso</td><td>$32,50</td><td>IVA</td>
              <td>SERVICIO DEMO SA</td><td>05/03/2026</td><td>-$32,50</td><td></td>
            </tr>
          </table>
        </body>
        </html>
        """;

    private static StatementFileContext Context(string? providerHint = null) =>
        new(
            "Estado_de_cuenta_05_03_2026_07_03_2026.xls",
            "application/vnd.ms-excel",
            HtmlTableReader.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Html))),
            providerHint);

    [Fact]
    public void The_file_is_recognised_as_html_regardless_of_its_xls_extension()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(Html);
        Assert.True(HtmlTableReader.LooksLikeHtml(bytes));
    }

    [Fact]
    public void The_Guayaquil_parser_claims_the_file_from_the_header_shape_alone()
    {
        // No letterhead names the bank anywhere in this file (its branding is a
        // CSS color); "Beneficiario" next to "Saldo efectivo" is what identifies
        // it, without a provider hint from the account being imported into.
        var parser = Resolver().Resolve(Context());

        Assert.NotNull(parser);
        Assert.Equal("GUAYAQUIL_V1", parser.ParserCode);
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
    public async Task Direction_comes_from_Tipo_not_from_a_sign_on_Monto()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.Equal(TransactionDirection.Income, result.Transactions[0].Direction);
        Assert.Equal(300.00m, result.Transactions[0].Amount);

        Assert.Equal(TransactionDirection.Expense, result.Transactions[1].Direction);
        Assert.Equal(60.00m, result.Transactions[1].Amount);

        Assert.Equal(TransactionDirection.Expense, result.Transactions[2].Direction);
        Assert.Equal(32.50m, result.Transactions[2].Amount);
    }

    [Fact]
    public async Task The_description_combines_the_counterparty_and_the_channel()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        // "Detalle" alone ("Banco Guayaquil", "IVA") would be useless for telling
        // two movements apart; "Beneficiario" is what the user actually recognises.
        // Counterparty comes first: the categorisation engine's merchant guess and
        // the stored Transaction.Merchant both take the leading tokens of this same
        // description, so the channel word must not crowd the person's name out of
        // them (see HeaderMappedStatementParser.BuildDescription).
        Assert.Equal("MORAN VITERI CARLOS ENRIQUE Transferencia", result.Transactions[0].Description);
        Assert.Equal("CAFE DEMO DEL PARQUE Banco Guayaquil", result.Transactions[1].Description);
        Assert.Equal("SERVICIO DEMO SA IVA", result.Transactions[2].Description);
    }

    [Fact]
    public async Task The_closing_balance_is_the_newest_rows_running_balance()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        // The file runs newest first, exactly like the real export: the closing
        // balance is the first row's "Saldo efectivo", not the last one read.
        Assert.Equal(207.50m, result.DeclaredClosingBalance);
    }

    [Fact]
    public async Task Amounts_written_as_dollar_with_comma_decimals_are_read_correctly()
    {
        var result = await Resolver().Resolve(Context())!.ParseAsync(Context(), CancellationToken.None);

        Assert.Equal(300.00m, result.Transactions[0].Amount);
        Assert.Equal(60.00m, result.Transactions[1].Amount);
        Assert.Equal(32.50m, result.Transactions[2].Amount);
    }
}
