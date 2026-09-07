using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 10 (Generic CSV mapper), end to end: a file no parser recognises
/// comes back Failed with a raw-column hint instead of just an error message;
/// POST /imports/manual takes the user's own column choices and produces a
/// normal PreviewReady/confirm flow held to the exact same dedup and
/// categorisation logic as a recognised bank file; and a saved mapping is
/// remembered so the SAME user's next unrecognised file from the SAME
/// institution imports without asking again -- but never leaks to another
/// user, per the app's per-user isolation rule.
/// </summary>
public class ManualMappingFlowTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Abbreviations no bundled parser's header synonym list contains, and no
    // bank letterhead -- exactly the shape that must fail the automatic path.
    private const string UnrecognisableCsv = """
        F.Mov;Glosa;Ent;Sal;Doc
        01-02-2026;PAGO PROVEEDOR X;;85.00;DOC001
        03-02-2026;COBRO CLIENTE Y;220.50;;DOC002
        """;

    private const string SecondUnrecognisableCsv = """
        F.Mov;Glosa;Ent;Sal;Doc
        10-02-2026;RETIRO CAJERO;;45.00;DOC010
        """;

    private static async Task<JsonElement> UploadAsync(TestUser user, Guid accountId, string csv, string fileName = "estado.csv")
    {
        using var content = new MultipartFormDataContent();
        var file = new StringContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);

        var response = await user.Client.PostAsync($"/api/v1/imports?accountId={accountId}", content);
        await response.EnsureOkAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static async Task<HttpResponseMessage> UploadManualAsync(
        TestUser user,
        Guid accountId,
        string csv,
        bool firstRowIsHeader = true,
        int dateColumn = 0,
        int descriptionColumn = 1,
        int? amountColumn = null,
        int? debitColumn = 3,
        int? creditColumn = 2,
        int? referenceColumn = 4,
        bool saveMapping = true,
        string fileName = "estado.csv")
    {
        using var content = new MultipartFormDataContent();
        var file = new StringContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);

        var query = $"accountId={accountId}&firstRowIsHeader={firstRowIsHeader}&dateColumn={dateColumn}&descriptionColumn={descriptionColumn}&saveMapping={saveMapping}"
            + (amountColumn is { } a ? $"&amountColumn={a}" : string.Empty)
            + (debitColumn is { } d ? $"&debitColumn={d}" : string.Empty)
            + (creditColumn is { } c ? $"&creditColumn={c}" : string.Empty)
            + (referenceColumn is { } r ? $"&referenceColumn={r}" : string.Empty);

        return await user.Client.PostAsync($"/api/v1/imports/manual?{query}", content);
    }

    [Fact]
    public async Task An_unrecognised_file_comes_back_failed_with_a_raw_column_hint_instead_of_only_an_error_message()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await UploadAsync(user, accountId, UnrecognisableCsv);

        Assert.Equal("Failed", preview.GetProperty("status").GetString());
        Assert.True(preview.TryGetProperty("unmappedColumns", out var columns));
        Assert.False(columns.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined);
        Assert.Equal(5, columns.GetArrayLength());
        Assert.Equal("F.Mov", columns[0].GetString());

        Assert.True(preview.TryGetProperty("unmappedSampleRows", out var sampleRows));
        Assert.True(sampleRows.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Manual_mapping_produces_a_normal_preview_ready_import_that_confirms_correctly()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var failed = await UploadAsync(user, accountId, UnrecognisableCsv);
        Assert.Equal("Failed", failed.GetProperty("status").GetString());

        var response = await UploadManualAsync(user, accountId, UnrecognisableCsv);
        await response.EnsureOkAsync();
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        Assert.Equal("PreviewReady", preview.GetProperty("status").GetString());
        Assert.Equal("MANUAL_MAPPING_V1", preview.GetProperty("parserCode").GetString());
        Assert.Equal(2, preview.GetProperty("newRows").GetInt32());
        Assert.Equal(0, preview.GetProperty("invalidRows").GetInt32());

        var rows = preview.GetProperty("rows");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal("Expense", rows[0].GetProperty("direction").GetString());
        Assert.Equal(85.00m, rows[0].GetProperty("amount").GetDecimal());
        Assert.Equal("Income", rows[1].GetProperty("direction").GetString());
        Assert.Equal(220.50m, rows[1].GetProperty("amount").GetDecimal());

        var result = await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());
        Assert.Equal(2, result.GetProperty("importedCount").GetInt32());
    }

    [Fact]
    public async Task A_saved_mapping_auto_applies_to_the_next_unrecognised_file_from_the_same_account_without_asking_again()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        await UploadAsync(user, accountId, UnrecognisableCsv);
        var manualResponse = await UploadManualAsync(user, accountId, UnrecognisableCsv, saveMapping: true);
        await manualResponse.EnsureOkAsync();

        // A different, still-unrecognisable file from the same account: no manual
        // endpoint call this time, just the plain upload -- the saved mapping
        // must be found and applied automatically.
        var second = await UploadAsync(user, accountId, SecondUnrecognisableCsv, fileName: "segundo.csv");

        Assert.Equal("PreviewReady", second.GetProperty("status").GetString());
        Assert.Equal("MANUAL_MAPPING_V1", second.GetProperty("parserCode").GetString());
        Assert.Equal(1, second.GetProperty("newRows").GetInt32());

        var rows = second.GetProperty("rows");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal("Expense", rows[0].GetProperty("direction").GetString());
        Assert.Equal(45.00m, rows[0].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Choosing_not_to_save_the_mapping_means_the_next_unrecognised_file_is_still_failed()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        await UploadAsync(user, accountId, UnrecognisableCsv);
        var manualResponse = await UploadManualAsync(user, accountId, UnrecognisableCsv, saveMapping: false);
        await manualResponse.EnsureOkAsync();

        var second = await UploadAsync(user, accountId, SecondUnrecognisableCsv, fileName: "segundo.csv");

        Assert.Equal("Failed", second.GetProperty("status").GetString());
        Assert.True(second.GetProperty("unmappedColumns").GetArrayLength() > 0);
    }

    [Fact]
    public async Task A_mapping_one_user_saves_never_applies_to_another_users_account()
    {
        var owner = await factory.RegisterUserAsync();
        var ownerAccount = await owner.CreateAccountAsync(openingBalance: null);
        await UploadAsync(owner, ownerAccount, UnrecognisableCsv);
        var ownerManual = await UploadManualAsync(owner, ownerAccount, UnrecognisableCsv, saveMapping: true);
        await ownerManual.EnsureOkAsync();

        var stranger = await factory.RegisterUserAsync();
        var strangerAccount = await stranger.CreateAccountAsync(openingBalance: null);

        var strangerUpload = await UploadAsync(stranger, strangerAccount, SecondUnrecognisableCsv, fileName: "segundo.csv");

        Assert.Equal("Failed", strangerUpload.GetProperty("status").GetString());
        Assert.True(strangerUpload.GetProperty("unmappedColumns").GetArrayLength() > 0);
    }

    [Fact]
    public async Task A_mapping_with_neither_an_amount_column_nor_debit_or_credit_is_rejected_as_unprocessable()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);
        await UploadAsync(user, accountId, UnrecognisableCsv);

        var response = await UploadManualAsync(
            user, accountId, UnrecognisableCsv, amountColumn: null, debitColumn: null, creditColumn: null);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }
}
