using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Critical case 4: a user must never reach another user's money — not by guessing
/// an id, not by omitting a token, not by reusing a stale one.
/// </summary>
public class AuthorizationTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Csv = """
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        01/03/2026,ACREDITACION ROL DE PAGOS,ROL-0001,,1420.00
        02/03/2026,SUPERMAXI ALBORADA,TRX-99881,48.20,
        """;

    [Fact]
    public async Task A_user_cannot_read_another_users_account()
    {
        var owner = await factory.RegisterUserAsync();
        var accountId = await owner.CreateAccountAsync();

        var intruder = await factory.RegisterUserAsync();
        var response = await intruder.Client.GetAsync($"/api/v1/accounts/{accountId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_user_cannot_read_another_users_transaction()
    {
        var owner = await factory.RegisterUserAsync();
        var accountId = await owner.CreateAccountAsync();
        var preview = await owner.UploadStatementAsync(accountId, Csv);
        await owner.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var list = await owner.GetJsonAsync("/api/v1/transactions");
        var transactionId = list.GetProperty("items")[0].GetProperty("id").GetGuid();

        var intruder = await factory.RegisterUserAsync();
        var response = await intruder.Client.GetAsync($"/api/v1/transactions/{transactionId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_user_cannot_recategorise_another_users_transaction()
    {
        var owner = await factory.RegisterUserAsync();
        var accountId = await owner.CreateAccountAsync();
        var preview = await owner.UploadStatementAsync(accountId, Csv);
        await owner.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var list = await owner.GetJsonAsync("/api/v1/transactions");
        var transactionId = list.GetProperty("items")[0].GetProperty("id").GetGuid();

        var categories = await owner.GetJsonAsync("/api/v1/categories");
        var categoryId = categories[0].GetProperty("id").GetGuid();

        var intruder = await factory.RegisterUserAsync();
        var response = await intruder.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{transactionId}/category",
            new { categoryId, createRule = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_user_cannot_import_into_another_users_account()
    {
        var owner = await factory.RegisterUserAsync();
        var accountId = await owner.CreateAccountAsync();

        var intruder = await factory.RegisterUserAsync();

        using var content = new MultipartFormDataContent();
        var file = new StringContent(Csv);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "estado.csv");

        var response = await intruder.Client.PostAsync($"/api/v1/imports?accountId={accountId}", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/summary")).StatusCode);
    }

    [Fact]
    public async Task Each_users_summary_only_counts_their_own_money()
    {
        var first = await factory.RegisterUserAsync();
        var firstAccount = await first.CreateAccountAsync(openingBalance: null);
        var preview = await first.UploadStatementAsync(firstAccount, Csv);
        await first.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var second = await factory.RegisterUserAsync();
        await second.CreateAccountAsync(openingBalance: 25m);

        var firstSummary = await first.GetJsonAsync("/api/v1/summary");
        var secondSummary = await second.GetJsonAsync("/api/v1/summary");

        Assert.Equal(1371.80m, firstSummary.GetProperty("totalBalance").GetDecimal());
        Assert.Equal(25m, secondSummary.GetProperty("totalBalance").GetDecimal());
        Assert.Equal(1, secondSummary.GetProperty("accountCount").GetInt32());
        Assert.Empty(secondSummary.GetProperty("recentTransactions").EnumerateArray());
    }

    [Fact]
    public async Task A_revoked_refresh_token_cannot_be_used_again()
    {
        var client = factory.CreateClient();
        var email = $"rotate-{Guid.CreateVersion7():N}@nexo.test";

        var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "NexoIntegration2026!",
            displayName = "Rotación",
            acceptedTerms = true,
            confirmedAdult = true,
        });
        await registration.EnsureOkAsync();

        var payload = await registration.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = payload.GetProperty("refreshToken").GetString()!;

        var first = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });
        await first.EnsureOkAsync();

        // Replaying the original token is the signature of a stolen token.
        var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // ...and the whole family is dead, including the one just issued.
        var rotated = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("refreshToken").GetString()!;

        var afterReuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Registering_the_same_email_twice_does_not_reveal_that_it_exists()
    {
        var client = factory.CreateClient();
        var email = $"dup-{Guid.CreateVersion7():N}@nexo.test";

        var body = new { email, password = "NexoIntegration2026!", displayName = "Duplicado", acceptedTerms = true, confirmedAdult = true };

        await (await client.PostAsJsonAsync("/api/v1/auth/register", body)).EnsureOkAsync();

        var second = await client.PostAsJsonAsync("/api/v1/auth/register", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(email, problem.GetProperty("detail").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
