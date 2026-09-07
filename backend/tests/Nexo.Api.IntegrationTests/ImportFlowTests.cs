using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// The acceptance path from the requirement: import, preview, confirm, re-import
/// without duplicating, filter, recategorise, and tell an estimated balance from a
/// verified one.
/// </summary>
public class ImportFlowTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string MarchStatement = """
        Banco Pichincha - Estado de cuenta
        Cuenta: ****4821
        Fecha,Concepto,Documento,Debito,Credito,Saldo
        01/03/2026,ACREDITACION ROL DE PAGOS,ROL-0001,,1420.00,2420.00
        02/03/2026,SUPERMAXI ALBORADA,TRX-99881,48.20,,2371.80
        05/03/2026,NETFLIX.COM,TRX-99999,12.99,,2358.81
        07/03/2026,UBER TRIP,TRX-99123,6.80,,2352.01
        """;

    private const string OverlappingStatement = """
        Banco Pichincha - Estado de cuenta
        Cuenta: ****4821
        Fecha,Concepto,Documento,Debito,Credito,Saldo
        05/03/2026,NETFLIX.COM,TRX-99999,12.99,,2358.81
        07/03/2026,UBER TRIP,TRX-99123,6.80,,2352.01
        09/03/2026,PRIMAX VIA DAULE,TRX-99555,35.00,,2317.01
        """;

    [Fact]
    public async Task Uploading_a_statement_produces_a_preview_and_writes_nothing_yet()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, MarchStatement);

        Assert.Equal("PreviewReady", preview.GetProperty("status").GetString());
        Assert.Equal("PICHINCHA_V1", preview.GetProperty("parserCode").GetString());
        Assert.Equal(4, preview.GetProperty("totalRows").GetInt32());
        Assert.Equal(4, preview.GetProperty("newRows").GetInt32());
        Assert.Equal(0, preview.GetProperty("duplicateRows").GetInt32());
        Assert.Equal(1420.00m, preview.GetProperty("incomeTotal").GetDecimal());
        Assert.Equal(67.99m, preview.GetProperty("expenseTotal").GetDecimal());

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(0, transactions.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Confirming_the_preview_writes_the_movements_and_updates_the_balance()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, MarchStatement);
        var result = await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        Assert.Equal(4, result.GetProperty("importedCount").GetInt32());
        Assert.Equal(1352.01m, result.GetProperty("newEstimatedBalance").GetDecimal());
        Assert.Equal("Estimated", result.GetProperty("balanceType").GetString());

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(4, transactions.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Critical_case_1_re_importing_the_same_file_creates_no_duplicates()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var first = await user.UploadStatementAsync(accountId, MarchStatement);
        await user.ConfirmImportAsync(first.GetProperty("importId").GetGuid());

        var second = await user.UploadStatementAsync(accountId, MarchStatement);

        Assert.Equal(0, second.GetProperty("newRows").GetInt32());
        Assert.Equal(4, second.GetProperty("duplicateRows").GetInt32());
        Assert.True(second.GetProperty("previouslyImportedFile").GetBoolean());

        var confirmation = await user.ConfirmImportAsync(second.GetProperty("importId").GetGuid());
        Assert.Equal(0, confirmation.GetProperty("importedCount").GetInt32());

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(4, transactions.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task An_overlapping_period_only_imports_what_is_new()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var first = await user.UploadStatementAsync(accountId, MarchStatement);
        await user.ConfirmImportAsync(first.GetProperty("importId").GetGuid());

        var second = await user.UploadStatementAsync(accountId, OverlappingStatement);

        Assert.Equal(1, second.GetProperty("newRows").GetInt32());
        Assert.Equal(2, second.GetProperty("duplicateRows").GetInt32());

        await user.ConfirmImportAsync(second.GetProperty("importId").GetGuid());

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(5, transactions.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Critical_case_3_an_unreadable_file_fails_without_touching_the_ledger()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(
            accountId,
            "esto no es un estado de cuenta\nsolo texto",
            "notas.csv");

        Assert.Equal("Failed", preview.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("failureReason").GetString()));

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(0, transactions.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Critical_case_10_a_partially_valid_file_imports_the_good_rows_and_reports_the_rest()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        const string partial = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            01/03/2026,ACREDITACION ROL DE PAGOS,ROL-0001,,1420.00
            fecha-invalida,COMPRA CON ERROR,TRX-1,10.00,
            03/03/2026,SUPERMAXI ALBORADA,TRX-2,48.20,
            """;

        var preview = await user.UploadStatementAsync(accountId, partial);

        Assert.Equal("PreviewReady", preview.GetProperty("status").GetString());
        Assert.Equal(2, preview.GetProperty("newRows").GetInt32());
        Assert.Equal(1, preview.GetProperty("invalidRows").GetInt32());

        var result = await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());
        Assert.Equal(2, result.GetProperty("importedCount").GetInt32());
    }

    [Fact]
    public async Task Rows_can_be_excluded_before_confirming()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, MarchStatement);
        var excluded = preview.GetProperty("rows")[0].GetProperty("id").GetGuid();

        var response = await user.Client.PostAsJsonAsync(
            $"/api/v1/imports/{preview.GetProperty("importId").GetGuid()}/confirm",
            new { excludedRowIds = new[] { excluded }, applyDeclaredClosingBalance = false });

        await response.EnsureOkAsync();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(3, result.GetProperty("importedCount").GetInt32());
    }

    [Fact]
    public async Task A_verified_balance_stays_verified_until_a_later_movement_arrives()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, MarchStatement);
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var verified = await user.Client.PostAsJsonAsync(
            $"/api/v1/accounts/{accountId}/verified-balance",
            new { balance = 2352.01m, asOf = new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero) });

        await verified.EnsureOkAsync();
        var account = await verified.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2352.01m, account.GetProperty("balance").GetDecimal());
        Assert.Equal("Verified", account.GetProperty("balanceType").GetString());
    }

    [Fact]
    public async Task Movements_can_be_searched_and_filtered()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, MarchStatement);
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var search = await user.GetJsonAsync("/api/v1/transactions?search=supermaxi");
        Assert.Equal(1, search.GetProperty("totalCount").GetInt32());

        var income = await user.GetJsonAsync("/api/v1/transactions?direction=Income");
        Assert.Equal(1, income.GetProperty("totalCount").GetInt32());

        var expenses = await user.GetJsonAsync("/api/v1/transactions?direction=Expense");
        Assert.Equal(3, expenses.GetProperty("totalCount").GetInt32());

        var paged = await user.GetJsonAsync("/api/v1/transactions?page=1&pageSize=2");
        Assert.Equal(2, paged.GetProperty("items").GetArrayLength());
        Assert.True(paged.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Rules_categorise_on_import_and_a_manual_correction_wins()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, MarchStatement);
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var netflix = (await user.GetJsonAsync("/api/v1/transactions?search=netflix"))
            .GetProperty("items")[0];

        Assert.Equal("Suscripciones", netflix.GetProperty("categoryName").GetString());

        var categories = await user.GetJsonAsync("/api/v1/categories");
        var entertainment = categories.EnumerateArray()
            .First(c => c.GetProperty("code").GetString() == "ENTRETENIMIENTO")
            .GetProperty("id").GetGuid();

        var response = await user.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{netflix.GetProperty("id").GetGuid()}/category",
            new { categoryId = entertainment, createRule = true });

        await response.EnsureOkAsync();
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Entretenimiento", updated.GetProperty("categoryName").GetString());
        Assert.True(updated.GetProperty("categoryManuallySet").GetBoolean());
    }

    [Fact]
    public async Task The_home_summary_answers_every_question_the_first_screen_asks()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, MarchStatement);
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var summary = await user.GetJsonAsync("/api/v1/summary");

        Assert.False(string.IsNullOrWhiteSpace(summary.GetProperty("greeting").GetString()));
        Assert.Equal("Usuario de prueba", summary.GetProperty("displayName").GetString());
        Assert.Equal(1352.01m, summary.GetProperty("totalBalance").GetDecimal());
        Assert.Equal(1, summary.GetProperty("accountCount").GetInt32());
        Assert.True(summary.GetProperty("anyEstimatedBalance").GetBoolean());
        Assert.Equal(4, summary.GetProperty("recentTransactions").GetArrayLength());
        Assert.NotEmpty(summary.GetProperty("categoryBreakdown").EnumerateArray());
    }

    [Fact]
    public async Task Exporting_my_data_returns_everything_Nexo_holds()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, MarchStatement);
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var response = await user.Client.GetAsync("/api/v1/privacy/export");
        await response.EnsureOkAsync();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(user.Email, document.RootElement.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal(4, document.RootElement.GetProperty("transactions").GetArrayLength());
        Assert.Equal(1, document.RootElement.GetProperty("accounts").GetArrayLength());
    }

    [Fact]
    public async Task Providers_only_advertise_what_is_implemented()
    {
        var user = await factory.RegisterUserAsync();
        var providers = await user.GetJsonAsync("/api/v1/providers");

        var pichincha = providers.EnumerateArray()
            .First(p => p.GetProperty("code").GetString() == "PICHINCHA");

        Assert.True(pichincha.GetProperty("supportsStatementImport").GetBoolean());

        var capabilities = pichincha.GetProperty("capabilities").EnumerateArray().ToList();
        Assert.Single(capabilities);
        Assert.Equal("ManualImport", capabilities[0].GetProperty("mode").GetString());
        Assert.False(capabilities[0].GetProperty("isAutomatic").GetBoolean());
    }

    [Fact]
    public async Task Connecting_a_mailbox_fails_loudly_when_the_environment_has_no_credentials()
    {
        var user = await factory.RegisterUserAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/v1/email-connections",
            new { providerKind = "Gmail", emailAddress = "anderson@example.com" });

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
    }
}
