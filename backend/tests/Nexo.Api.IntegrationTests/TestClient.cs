using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace Nexo.Api.IntegrationTests;

public sealed record TestUser(Guid Id, string Email, string AccessToken, HttpClient Client);

/// <summary>Small helpers so the tests read like the user journey they describe.</summary>
public static class TestClientExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Replaces <c>EnsureSuccessStatusCode</c>, whose message says only "401
    /// (Unauthorized)" and leaves the cause to guesswork. The bearer scheme puts the
    /// real reason — expired, bad signature, wrong audience — in WWW-Authenticate,
    /// and every other failure puts it in the ProblemDetails body. Both belong in the
    /// assertion message.
    /// </summary>
    public static async Task<HttpResponseMessage> EnsureOkAsync(this HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var request = response.RequestMessage;
        var challenge = response.Headers.WwwAuthenticate.Count > 0
            ? string.Join("; ", response.Headers.WwwAuthenticate)
            : "(none)";
        var body = await response.Content.ReadAsStringAsync();

        if (body.Length > 1000)
        {
            body = body[..1000] + "…";
        }

        throw new XunitException(
            $"{request?.Method} {request?.RequestUri?.PathAndQuery} -> {(int)response.StatusCode} {response.StatusCode}"
            + $"{Environment.NewLine}WWW-Authenticate: {challenge}"
            + $"{Environment.NewLine}Body: {body}");
    }

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

        await response.EnsureOkAsync();

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

        await response.EnsureOkAsync();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return payload.GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> UploadStatementAsync(
        this TestUser user,
        Guid accountId,
        string csv,
        string fileName = "estado.csv",
        string contentType = "text/csv")
    {
        using var content = new MultipartFormDataContent();
        var file = new StringContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);

        var response = await user.Client.PostAsync($"/api/v1/imports?accountId={accountId}", content);
        await response.EnsureOkAsync();

        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public static async Task<JsonElement> ConfirmImportAsync(this TestUser user, Guid importId)
    {
        var response = await user.Client.PostAsJsonAsync(
            $"/api/v1/imports/{importId}/confirm",
            new { excludedRowIds = Array.Empty<Guid>(), applyDeclaredClosingBalance = false });

        await response.EnsureOkAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public static async Task<JsonElement> GetJsonAsync(this TestUser user, string url)
    {
        var response = await user.Client.GetAsync(url);
        await response.EnsureOkAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>POST with no request body, returning the parsed JSON response -- PULSO's POST /pulses/evaluate and similar action endpoints.</summary>
    public static async Task<JsonElement> PostForJsonAsync(this TestUser user, string url)
    {
        var response = await user.Client.PostAsync(url, content: null);
        await response.EnsureOkAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>
    /// Uploads a statement, confirms it, then returns the resulting "items" array
    /// from GET /transactions -- the common "give me a posted transaction to act
    /// on" setup that several tests only need one line for.
    /// </summary>
    public static async Task<JsonElement> ConfirmAndListAsync(this TestUser user, Guid accountId, string csv)
    {
        var preview = await user.UploadStatementAsync(accountId, csv);
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());
        var list = await user.GetJsonAsync("/api/v1/transactions");
        return list.GetProperty("items");
    }
}
