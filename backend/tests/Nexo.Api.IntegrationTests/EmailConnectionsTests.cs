using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 23 ("Preparar email ingestion, sin OAuth"): closes a gap found while
/// reviewing the existing (already-built) email-ingestion architecture -- starting a
/// Forwarding connection left it in <c>PendingAuthorization</c> forever, because
/// <c>EmailConnection.Authorize</c> requires a secret and Forwarding has none to give
/// it. Nothing here exercises Gmail or Outlook beyond confirming they stay refused
/// without credentials -- that part is explicitly out of scope until Entregables
/// 24/25 bring real OAuth.
///
/// The shared <see cref="NexoApiFactory"/> has no Forwarding domain/secret configured
/// (matches a real deployment with no email ingestion set up at all), so every test
/// that needs Forwarding "enabled" builds its own gated factory via
/// <c>WithWebHostBuilder</c> -- the same pattern <c>AccountLockoutTests</c> uses for
/// its dedicated clock, chosen for the same reason: it must not change what every
/// other test in the suite sees.
/// </summary>
public class EmailConnectionsTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Password = "NexoIntegration2026!";

    private static WebApplicationFactory<Program> WithForwardingConfigured(NexoApiFactory factory) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Nexo:EmailIngestion:Forwarding:InboundDomain"] = "in.nexo.test",
                    ["Nexo:EmailIngestion:Forwarding:WebhookSecret"] = "entregable-23-webhook-secret",
                });
            }));

    private static async Task<HttpClient> RegisterAsync(HttpClient client, string? email = null)
    {
        var address = email ?? $"email-conn-{Guid.CreateVersion7():N}@nexo.test";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = address,
            password = Password,
            displayName = "Usuario de prueba",
            acceptedTerms = true,
            confirmedAdult = true,
        });
        await response.EnsureOkAsync();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", payload.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Starting_a_forwarding_connection_connects_immediately_and_returns_an_inbound_address()
    {
        await using var gated = WithForwardingConfigured(factory);
        var client = await RegisterAsync(gated.CreateClient());

        var start = await client.PostAsJsonAsync("/api/v1/email-connections", new
        {
            providerKind = "Forwarding",
            emailAddress = "notificaciones@midominio.test",
        });
        await start.EnsureOkAsync();

        var payload = await start.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Connected", payload.GetProperty("status").GetString());
        var inbound = payload.GetProperty("inboundAddress").GetString();
        Assert.NotNull(inbound);
        Assert.StartsWith("u-", inbound);
        Assert.Contains("@in.nexo.app", inbound);

        // Not just the response claiming it connected -- the list the profile
        // screen actually reads shows the same thing.
        var list = await client.GetAsync("/api/v1/email-connections");
        await list.EnsureOkAsync();
        var connections = await list.Content.ReadFromJsonAsync<JsonElement[]>();
        var mine = Assert.Single(connections!);
        Assert.Equal("Connected", mine.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Starting_gmail_or_outlook_without_credentials_is_refused_with_a_clear_reason()
    {
        // The shared factory: no provider configured at all, same as a fresh
        // deployment before anyone has set up email ingestion.
        var client = await RegisterAsync(factory.CreateClient());

        var gmail = await client.PostAsJsonAsync(
            "/api/v1/email-connections",
            new { providerKind = "Gmail", emailAddress = "persona@gmail.com" });
        Assert.Equal(HttpStatusCode.Conflict, gmail.StatusCode);

        var outlook = await client.PostAsJsonAsync(
            "/api/v1/email-connections",
            new { providerKind = "Outlook", emailAddress = "persona@outlook.com" });
        Assert.Equal(HttpStatusCode.Conflict, outlook.StatusCode);
    }

    [Fact]
    public async Task Revoking_a_connection_removes_it_from_the_list()
    {
        await using var gated = WithForwardingConfigured(factory);
        var client = await RegisterAsync(gated.CreateClient());

        var start = await client.PostAsJsonAsync(
            "/api/v1/email-connections",
            new { providerKind = "Forwarding", emailAddress = "notificaciones@midominio.test" });
        await start.EnsureOkAsync();
        var connectionId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var revoke = await client.DeleteAsync($"/api/v1/email-connections/{connectionId}");
        await revoke.EnsureOkAsync();

        var list = await client.GetAsync("/api/v1/email-connections");
        await list.EnsureOkAsync();
        var connections = await list.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.Empty(connections!);
    }

    [Fact]
    public async Task A_user_cannot_revoke_another_users_connection()
    {
        await using var gated = WithForwardingConfigured(factory);
        var owner = await RegisterAsync(gated.CreateClient());
        var stranger = await RegisterAsync(gated.CreateClient());

        var start = await owner.PostAsJsonAsync(
            "/api/v1/email-connections",
            new { providerKind = "Forwarding", emailAddress = "notificaciones@midominio.test" });
        await start.EnsureOkAsync();
        var connectionId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var revoke = await stranger.DeleteAsync($"/api/v1/email-connections/{connectionId}");
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);

        // Still there, untouched, from the owner's own point of view.
        var list = await owner.GetAsync("/api/v1/email-connections");
        await list.EnsureOkAsync();
        var connections = await list.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.Single(connections!);
    }
}
