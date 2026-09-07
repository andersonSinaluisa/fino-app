using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Deleting a financial account had never been exercised end to end. It shares the
/// exact query shape that broke import and the movements list --
/// <c>importIds.Contains(r.ImportId)</c> composed with the global per-user filter
/// (backend/src/Nexo.Application/Privacy/PrivacyService.cs) -- so any account with
/// at least one confirmed import would have failed to delete with a 500. Fixed
/// alongside the other two call sites; see docs/architecture.md ADR-009.
/// </summary>
public class PrivacyFlowTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Statement = """
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        15/03/2026,ACREDITACION ROL DE PAGOS,ROL-4001,,500.00
        16/03/2026,FARMACIA CRUZ AZUL,TRX-4001,20.00,
        """;

    [Fact]
    public async Task Deleting_an_account_with_import_history_removes_everything_and_does_not_fail()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, Statement);
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var response = await user.Client.DeleteAsync($"/api/v1/privacy/accounts/{accountId}");
        await response.EnsureOkAsync();

        var summary = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, summary.GetProperty("transactionsDeleted").GetInt32());
        Assert.Equal(1, summary.GetProperty("accountsDeleted").GetInt32());
        Assert.Equal(1, summary.GetProperty("importsDeleted").GetInt32());

        var accounts = await user.GetJsonAsync("/api/v1/accounts");
        Assert.Equal(0, accounts.GetArrayLength());

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(0, transactions.GetProperty("totalCount").GetInt32());

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await user.Client.GetAsync($"/api/v1/accounts/{accountId}")).StatusCode);
    }
}
