using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

public sealed record TestUser(Guid Id, string Email, string AccessToken, HttpClient Client);

/// <summary>Small helpers so the tests read like the user journey they describe.</summary>
public static class TestClientExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<TestUser> RegisterUserAsync(this NexoApiFactory factory, string? email = null)
    {
        var client = factory.CreateClient();
        var address = email ?? $"user-{Guid.CreateVersion7():N}@nexo.test";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = address,
            password = "NexoIntegration2026!",
            displayName = "Usuario de prueba",
        });

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var accessToken = payload.GetProperty("accessToken").GetString()!;
        var userId = payload.GetProperty("user").GetProperty("id").GetGuid();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return new TestUser(userId, address, accessToken, client);
    }

    public static async Task<Guid> CreateAccountAsync(
        this TestUser user,
        string providerCode = "PICHINCHA",
        decimal? openingBalance = 1000m)
    {
        var response = await user.Client.PostAsJsonAsync("/api/v1/accounts", new
        {
            providerCode,
            alias = "Banco Pichincha",
            accountType = "Savings",
            connectionMode = "ManualImport",
            mask = "4821",
            openingVerifiedBalance = openingBalance,
            currency = "USD",
        });

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return payload.GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> UploadStatementAsync(
        this TestUser user,
        Guid accountId,
        string csv,
        string fileName = "estado.csv")
    {
        using var content = new MultipartFormDataContent();
        var file = new StringContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);

        var response = await user.Client.PostAsync($"/api/v1/imports?accountId={accountId}", content);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public static async Task<JsonElement> ConfirmImportAsync(this TestUser user, Guid importId)
    {
        var response = await user.Client.PostAsJsonAsync(
            $"/api/v1/imports/{importId}/confirm",
            new { excludedRowIds = Array.Empty<Guid>(), applyDeclaredClosingBalance = false });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public static async Task<JsonElement> GetJsonAsync(this TestUser user, string url)
    {
        var response = await user.Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }
}
