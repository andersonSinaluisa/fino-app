using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 7: a hostile or merely broken upload must always fail cleanly --
/// a 4xx with a message the user can act on -- never a 500, never an
/// unbounded allocation, never a value that could turn into a formula the day
/// Nexo ships a spreadsheet export. Some of these guards already existed
/// (empty file, size limit, content-type allowlist); this suite is their first
/// regression coverage plus the gaps Entregable 7 asked for: magic-byte
/// detection, a column ceiling, and CSV/XLSX formula-injection neutralisation.
/// </summary>
public class ImportRobustnessTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private static async Task<HttpResponseMessage> UploadRaw(
        TestUser user, Guid accountId, byte[] bytes, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);

        return await user.Client.PostAsync($"/api/v1/imports?accountId={accountId}", content);
    }

    [Fact]
    public async Task An_empty_file_is_rejected_with_a_clean_400_not_a_500()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var response = await UploadRaw(user, accountId, [], "vacio.csv", "text/csv");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_rejected_with_a_clean_415_not_a_500()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var oversized = new byte[10 * 1024 * 1024 + 1];
        var response = await UploadRaw(user, accountId, oversized, "enorme.csv", "text/csv");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task An_executable_disguised_with_a_csv_extension_is_rejected_by_content_type()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        // The declared content type, not the extension, is the first gate.
        var response = await UploadRaw(
            user, accountId, [0x4D, 0x5A, 0x90, 0x00], "factura.csv", "application/x-msdownload");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task A_binary_file_that_slips_past_the_content_type_check_fails_cleanly_not_as_garbage_rows()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        // A real device sends "application/vnd.ms-excel" for plenty of legacy
        // formats, so the content-type allowlist alone cannot catch every binary
        // file. NUL bytes never appear in a bank's own text export -- this is
        // the magic-byte check that catches what the allowlist does not. Sent
        // through UploadRaw (not the string-based helper) so the exact bytes --
        // NUL included -- reach the server untouched by any text encoding.
        var jpegLike = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00 };
        var response = await UploadRaw(user, accountId, jpegLike, "foto.csv", "application/vnd.ms-excel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var preview = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("Failed", preview.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("failureReason").GetString()));

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(0, transactions.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task An_unsupported_extension_is_rejected_before_the_file_is_ever_parsed()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var response = await UploadRaw(
            user, accountId, "no importa"u8.ToArray(), "estado.pdf", "text/csv");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task A_path_traversal_filename_is_reduced_to_its_base_name_and_never_touches_the_filesystem()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        const string csv = """
            Fecha,Concepto,Debito,Credito
            04/03/2026,PAGO SERVICIOS,25.50,
            """;

        // ImportEndpoints already runs every upload through Path.GetFileName;
        // this pins that behaviour so it cannot regress silently.
        var preview = await user.UploadStatementAsync(
            accountId, csv, fileName: "../../../../etc/passwd.csv");

        Assert.Equal("PreviewReady", preview.GetProperty("status").GetString());

        var stored = await user.GetJsonAsync($"/api/v1/imports/{preview.GetProperty("importId").GetGuid()}");
        Assert.Equal("passwd.csv", stored.GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task A_row_with_thousands_of_columns_is_rejected_instead_of_exhausting_memory()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var hostileRow = string.Join(',', Enumerable.Repeat("x", 5000));
        var csv = "Fecha,Concepto,Debito,Credito\n" + hostileRow;

        var preview = await user.UploadStatementAsync(accountId, csv, fileName: "hostil.csv");

        Assert.Equal("Failed", preview.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("failureReason").GetString()));
    }

    [Fact]
    public async Task A_description_starting_with_a_formula_trigger_is_neutralised()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        // A hostile bank export (or one that merely echoes what a merchant typed)
        // could put a spreadsheet formula in the description column. Nexo has no
        // CSV/XLSX export today, but the value still must never reach any future
        // one as a live formula.
        const string csv = """
            Fecha,Concepto,Debito,Credito
            04/03/2026,=cmd|'/c calc'!A1,25.50,
            """;

        var preview = await user.UploadStatementAsync(accountId, csv);
        Assert.Equal("PreviewReady", preview.GetProperty("status").GetString());
        Assert.Equal(1, preview.GetProperty("newRows").GetInt32());

        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        var description = transactions.GetProperty("items")[0].GetProperty("description").GetString();

        // Neutralised means "starts with an apostrophe so a spreadsheet reads it
        // as text, not a live formula" -- it does NOT mean the original text is
        // deleted (that would violate the no-silent-data-loss rule). Both must
        // hold: the leading apostrophe is there, and the underlying value the
        // bank actually sent is still fully readable behind it.
        Assert.StartsWith("'", description);
        Assert.Equal("'=cmd|'/c calc'!A1", description);
    }
}
