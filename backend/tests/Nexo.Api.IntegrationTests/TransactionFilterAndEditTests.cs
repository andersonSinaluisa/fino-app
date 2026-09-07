using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 12 ("Movimientos completos"): the filters GET /api/v1/transactions
/// accepts -- provider, monto mínimo/máximo, on top of the account/category/
/// direction/date/search filters that already existed -- and the merchant
/// correction endpoint, which must never touch the original bank description
/// or the automatic merchant guess.
/// </summary>
public class TransactionFilterAndEditTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Statement = """
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        01/03/2026,ACREDITACION ROL DE PAGOS,ROL-7001,,900.00
        02/03/2026,FARMACIA CRUZ AZUL,TRX-7001,15.50,
        """;

    private const string OtherProviderStatement = """
        Banco Guayaquil
        Fecha,Concepto,Documento,Debito,Credito
        03/03/2026,COMPRA SUPERMERCADO,TRX-8001,42.00,
        """;

    [Fact]
    public async Task Filters_by_provider_code_so_a_multi_bank_user_can_isolate_one_institution()
    {
        var user = await factory.RegisterUserAsync();
        var pichinchaAccount = await user.CreateAccountAsync(providerCode: "PICHINCHA", openingBalance: null);
        var guayaquilAccount = await user.CreateAccountAsync(providerCode: "GUAYAQUIL", openingBalance: null);

        await user.ConfirmImportAsync((await user.UploadStatementAsync(pichinchaAccount, Statement)).GetProperty("importId").GetGuid());
        await user.ConfirmImportAsync((await user.UploadStatementAsync(guayaquilAccount, OtherProviderStatement)).GetProperty("importId").GetGuid());

        var filtered = await user.GetJsonAsync("/api/v1/transactions?providerCode=GUAYAQUIL");

        Assert.Equal(1, filtered.GetProperty("totalCount").GetInt32());
        Assert.Equal("GUAYAQUIL", filtered.GetProperty("items")[0].GetProperty("providerCode").GetString());
    }

    [Fact]
    public async Task Filters_by_minimum_and_maximum_amount_using_the_movements_magnitude_not_the_signed_value()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);
        await user.ConfirmImportAsync((await user.UploadStatementAsync(accountId, Statement)).GetProperty("importId").GetGuid());

        var onlyLarge = await user.GetJsonAsync("/api/v1/transactions?minAmount=100");
        Assert.Equal(1, onlyLarge.GetProperty("totalCount").GetInt32());
        Assert.Equal(900.00m, onlyLarge.GetProperty("items")[0].GetProperty("amount").GetDecimal());

        var onlySmall = await user.GetJsonAsync("/api/v1/transactions?maxAmount=20");
        Assert.Equal(1, onlySmall.GetProperty("totalCount").GetInt32());
        Assert.Equal(15.50m, onlySmall.GetProperty("items")[0].GetProperty("amount").GetDecimal());

        var range = await user.GetJsonAsync("/api/v1/transactions?minAmount=10&maxAmount=20");
        Assert.Equal(1, range.GetProperty("totalCount").GetInt32());

        var none = await user.GetJsonAsync("/api/v1/transactions?minAmount=1000");
        Assert.Equal(0, none.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Amount_and_provider_filters_combine_with_the_pre_existing_ones()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(providerCode: "PICHINCHA", openingBalance: null);
        await user.ConfirmImportAsync((await user.UploadStatementAsync(accountId, Statement)).GetProperty("importId").GetGuid());

        // Direction + amount + provider, all at once: only the 900.00 income row
        // matches every clause simultaneously.
        var combined = await user.GetJsonAsync(
            $"/api/v1/transactions?providerCode=PICHINCHA&direction=Income&minAmount=500&accountId={accountId}");

        Assert.Equal(1, combined.GetProperty("totalCount").GetInt32());
        Assert.Equal("Income", combined.GetProperty("items")[0].GetProperty("direction").GetString());
    }

    [Fact]
    public async Task Correcting_the_merchant_never_touches_the_original_bank_description_or_the_automatic_guess()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);
        var confirmed = await user.ConfirmAndListAsync(accountId, Statement);
        var transactionId = confirmed[0].GetProperty("id").GetGuid();

        var before = await user.GetJsonAsync($"/api/v1/transactions/{transactionId}");
        var originalDescription = before.GetProperty("description").GetString();
        var automaticGuess = before.GetProperty("merchant").GetString();
        Assert.Null(before.GetProperty("merchantCorrected").GetString());

        var correctResponse = await user.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{transactionId}/merchant",
            new { merchant = "Rol de pagos - Empresa X" });
        await correctResponse.EnsureOkAsync();
        var corrected = await correctResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal("Rol de pagos - Empresa X", corrected.GetProperty("merchant").GetString());
        Assert.Equal("Rol de pagos - Empresa X", corrected.GetProperty("merchantCorrected").GetString());
        Assert.Equal(originalDescription, corrected.GetProperty("description").GetString());

        // The list endpoint (used by the movements screen) reflects the correction too.
        var list = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal("Rol de pagos - Empresa X", list.GetProperty("items")[0].GetProperty("merchant").GetString());

        // Clearing it (blank/null) falls back to the untouched automatic guess.
        var clearResponse = await user.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{transactionId}/merchant",
            new { merchant = (string?)null });
        await clearResponse.EnsureOkAsync();
        var cleared = await clearResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Null(cleared.GetProperty("merchantCorrected").GetString());
        Assert.Equal(automaticGuess, cleared.GetProperty("merchant").GetString());
        Assert.Equal(originalDescription, cleared.GetProperty("description").GetString());
    }

    [Fact]
    public async Task A_user_cannot_correct_another_users_transaction()
    {
        var owner = await factory.RegisterUserAsync();
        var accountId = await owner.CreateAccountAsync(openingBalance: null);
        var confirmed = await owner.ConfirmAndListAsync(accountId, Statement);
        var transactionId = confirmed[0].GetProperty("id").GetGuid();

        var intruder = await factory.RegisterUserAsync();
        var response = await intruder.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{transactionId}/merchant",
            new { merchant = "No debería poder" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
