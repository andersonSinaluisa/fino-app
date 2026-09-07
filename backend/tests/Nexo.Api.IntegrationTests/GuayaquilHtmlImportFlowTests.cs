using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// End-to-end proof for Entregable 6: the exact file Banco Guayaquil's personal
/// banking portal produces -- an HTML document saved with a ".xls" extension --
/// goes through the whole pipeline (upload, content-based detection, parsing,
/// preview, confirm, movements, dashboard) with no manual step in between. Same
/// HTML structure as the real export examined during the 2026-09-05 audit;
/// dates, amounts and names below are entirely invented.
/// </summary>
public class GuayaquilHtmlImportFlowTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string GuayaquilHtmlStatement = """
        <html>
        <head>
          <meta charset="UTF-8">
          <style>.header-cell { color: #e0007a; }</style>
        </head>
        <body>
          <table>
            <tr><td class="brand-block" colspan="4"></td><td colspan="4" rowspan="2">¿Cómo usar este archivo?</td></tr>
            <tr><td colspan="2">Resumen del periodo</td></tr>
            <tr><td>Ingresos</td><td>$300,00</td></tr>
            <tr><td>Egresos</td><td>$92,50</td></tr>
            <tr><td>Saldo del periodo</td><td>$207,50</td></tr>
            <tr class="title-row"><th colspan="8">Movimientos</th></tr>
            <tr>
              <th>Fecha</th><th>Tipo</th><th>Monto</th><th>Detalle</th>
              <th>Beneficiario</th><th>Fecha contable</th><th>Saldo efectivo</th><th>Categoría</th>
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

    [Fact]
    public async Task An_html_document_saved_as_xls_is_accepted_parsed_and_confirmed()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(providerCode: "GUAYAQUIL", openingBalance: null);

        // Real device uploads report this as "application/vnd.ms-excel" -- the
        // classic legacy-Excel MIME type -- for a ".xls" file, whatever the bytes
        // inside actually are. Detection must not trust either one.
        var preview = await user.UploadStatementAsync(
            accountId,
            GuayaquilHtmlStatement,
            fileName: "Estado_de_cuenta_05_03_2026_07_03_2026.xls",
            contentType: "application/vnd.ms-excel");

        Assert.Equal("PreviewReady", preview.GetProperty("status").GetString());
        Assert.Equal("GUAYAQUIL_V1", preview.GetProperty("parserCode").GetString());
        Assert.Equal(3, preview.GetProperty("newRows").GetInt32());
        Assert.Equal(0, preview.GetProperty("duplicateRows").GetInt32());
        Assert.Equal(300.00m, preview.GetProperty("incomeTotal").GetDecimal());
        Assert.Equal(92.50m, preview.GetProperty("expenseTotal").GetDecimal());

        var result = await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());
        Assert.Equal(3, result.GetProperty("importedCount").GetInt32());

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(3, transactions.GetProperty("totalCount").GetInt32());

        var incoming = transactions.GetProperty("items").EnumerateArray()
            .First(t => t.GetProperty("description").GetString()!.Contains("MORAN VITERI"));
        Assert.Equal("Income", incoming.GetProperty("direction").GetString());
        Assert.Equal(300.00m, incoming.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Reimporting_the_same_html_statement_creates_no_duplicates()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(providerCode: "GUAYAQUIL", openingBalance: null);

        var first = await user.UploadStatementAsync(
            accountId, GuayaquilHtmlStatement, fileName: "estado.xls", contentType: "application/vnd.ms-excel");
        await user.ConfirmImportAsync(first.GetProperty("importId").GetGuid());

        var second = await user.UploadStatementAsync(
            accountId, GuayaquilHtmlStatement, fileName: "estado.xls", contentType: "application/vnd.ms-excel");

        Assert.Equal(0, second.GetProperty("newRows").GetInt32());
        Assert.Equal(3, second.GetProperty("duplicateRows").GetInt32());

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(3, transactions.GetProperty("totalCount").GetInt32());
    }
}
