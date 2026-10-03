using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 22 ("Privacidad completa"): validates the privacy flows
/// end-to-end rather than just "does it 500". Three things fixed here, each
/// with its own coverage below: (1) the 7-day grace period on "eliminar mi
/// cuenta" was not actually reversible -- every session dies at request time
/// and login itself rejected the account, so there was no door back in; a
/// fresh login now cancels the pending deletion instead of being refused. (2)
/// deleting one leg of a confirmed internal transfer (Entregable 13) left the
/// surviving leg pointing at a row that no longer exists. (3) the data export
/// was missing whole categories of the user's own data despite claiming to be
/// "everything Nexo holds".
/// </summary>
public class PrivacyEndToEndTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Password = "NexoIntegration2026!";

    private static string OneRow(string date, string concept, string reference, decimal? debit, decimal? credit) =>
        $"""
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        {date},{concept},{reference},{debit?.ToString("0.00") ?? ""},{credit?.ToString("0.00") ?? ""}
        """;

    [Fact]
    public async Task Requesting_account_deletion_kills_the_session_but_a_fresh_login_cancels_it()
    {
        var client = factory.CreateClient();
        var email = $"privacy-{Guid.CreateVersion7():N}@nexo.test";

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName = "Usuario de prueba",
            acceptedTerms = true,
            confirmedAdult = true,
        });
        await register.EnsureOkAsync();
        var registerPayload = await register.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", registerPayload.GetProperty("accessToken").GetString());
        var refreshToken = registerPayload.GetProperty("refreshToken").GetString()!;

        var deleteRequest = await client.PostAsync("/api/v1/privacy/delete-account", content: null);
        Assert.Equal(HttpStatusCode.Accepted, deleteRequest.StatusCode);

        // The session that asked for deletion is dead immediately.
        var deadRefresh = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, deadRefresh.StatusCode);

        // The one door left during the grace period: a fresh login, which must
        // undo the pending deletion instead of rejecting the account outright.
        var login = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        await login.EnsureOkAsync();
        var loginPayload = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(loginPayload.GetProperty("accountDeletionCancelled").GetBoolean());

        // Genuinely usable again, not just holding a token that says so.
        var afterClient = factory.CreateClient();
        afterClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginPayload.GetProperty("accessToken").GetString());
        var accounts = await afterClient.GetAsync("/api/v1/accounts");
        Assert.Equal(HttpStatusCode.OK, accounts.StatusCode);
    }

    [Fact]
    public async Task An_ordinary_login_never_reports_a_cancelled_deletion()
    {
        var user = await factory.RegisterUserAsync();

        var login = await user.Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = user.Email, password = Password });
        await login.EnsureOkAsync();

        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(payload.GetProperty("accountDeletionCancelled").GetBoolean());
    }

    [Fact]
    public async Task Deleting_a_financial_account_clears_the_dangling_link_on_the_surviving_transfer_leg()
    {
        var user = await factory.RegisterUserAsync();
        var checking = await user.CreateAccountAsync(openingBalance: null);
        var savings = await user.CreateAccountAsync(openingBalance: null);

        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            checking, OneRow("05/03/2026", "TRANSFERENCIA A AHORROS", "TRX-9801", 300.00m, null)))
            .GetProperty("importId").GetGuid());
        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            savings, OneRow("06/03/2026", "TRANSFERENCIA DESDE CORRIENTE", "TRX-9802", null, 300.00m)))
            .GetProperty("importId").GetGuid());

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/candidates"))[0];
        var outgoingId = candidate.GetProperty("outgoingTransactionId").GetGuid();
        var incomingId = candidate.GetProperty("incomingTransactionId").GetGuid();

        await (await user.Client.PostAsJsonAsync(
            "/api/v1/transfers/confirm",
            new { outgoingTransactionId = outgoingId, incomingTransactionId = incomingId })).EnsureOkAsync();

        // Deletes the account holding the OUTGOING leg; the surviving leg lives
        // on `savings`.
        var deleteAccount = await user.Client.DeleteAsync($"/api/v1/privacy/accounts/{checking}");
        await deleteAccount.EnsureOkAsync();

        var survivor = await user.GetJsonAsync($"/api/v1/transactions/{incomingId}");
        Assert.False(survivor.GetProperty("isInternalTransfer").GetBoolean());
        Assert.Equal(JsonValueKind.Null, survivor.GetProperty("internalTransferLinkId").ValueKind);
    }

    [Fact]
    public async Task Deleting_my_movements_on_one_account_clears_the_dangling_link_on_the_other_accounts_leg()
    {
        var user = await factory.RegisterUserAsync();
        var checking = await user.CreateAccountAsync(openingBalance: null);
        var savings = await user.CreateAccountAsync(openingBalance: null);

        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            checking, OneRow("05/03/2026", "TRANSFERENCIA A AHORROS", "TRX-9901", 300.00m, null)))
            .GetProperty("importId").GetGuid());
        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            savings, OneRow("06/03/2026", "TRANSFERENCIA DESDE CORRIENTE", "TRX-9902", null, 300.00m)))
            .GetProperty("importId").GetGuid());

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/candidates"))[0];
        var outgoingId = candidate.GetProperty("outgoingTransactionId").GetGuid();
        var incomingId = candidate.GetProperty("incomingTransactionId").GetGuid();

        await (await user.Client.PostAsJsonAsync(
            "/api/v1/transfers/confirm",
            new { outgoingTransactionId = outgoingId, incomingTransactionId = incomingId })).EnsureOkAsync();

        // "Eliminar mis movimientos" scoped to just `checking` -- only the
        // outgoing leg goes.
        var deleteTransactions = await user.Client.PostAsJsonAsync("/api/v1/privacy/transactions/delete", new
        {
            financialAccountId = checking,
            before = (string?)null,
        });
        await deleteTransactions.EnsureOkAsync();

        var survivor = await user.GetJsonAsync($"/api/v1/transactions/{incomingId}");
        Assert.False(survivor.GetProperty("isInternalTransfer").GetBoolean());
        Assert.Equal(JsonValueKind.Null, survivor.GetProperty("internalTransferLinkId").ValueKind);
    }

    [Fact]
    public async Task Exporting_data_includes_categories_rules_insights_notifications_and_sessions()
    {
        var user = await factory.RegisterUserAsync();
        await user.CreateAccountAsync();

        var export = await user.Client.GetAsync("/api/v1/privacy/export");
        await export.EnsureOkAsync();
        var payload = await export.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(payload.TryGetProperty("categories", out _));
        Assert.True(payload.TryGetProperty("categorizationRules", out _));
        Assert.True(payload.TryGetProperty("insights", out _));
        Assert.True(payload.TryGetProperty("notifications", out _));

        // The session that just made the export call is itself in the export.
        var sessions = payload.GetProperty("sessions");
        Assert.True(sessions.GetArrayLength() >= 1);
    }
}
