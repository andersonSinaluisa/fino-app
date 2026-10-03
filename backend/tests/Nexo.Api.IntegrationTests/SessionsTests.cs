using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 19 ("Sesiones y dispositivos"): a refresh token is single-use and
/// rotates on every refresh, so the set of currently active RefreshToken rows
/// for a user already IS the set of their logged-in devices -- one row per
/// device, since a device only ever rotates its own row. These tests log the
/// same user in from a "second device" (a second HttpClient with its own
/// X-Device-Label) to exercise listing, revoking a specific session, the
/// refusal to self-revoke the calling session, and cross-user isolation.
/// </summary>
public class SessionsTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Password = "NexoIntegration2026!";

    [Fact]
    public async Task Listing_sessions_shows_every_active_device_with_exactly_one_marked_current()
    {
        var first = await RegisterAsync(factory, "Pixel de Anderson");
        await LoginAsync(factory, first.Email, "iPhone de prueba");

        var sessions = await GetSessionsAsync(first.Client);
        Assert.Equal(2, sessions.GetArrayLength());

        var current = sessions.EnumerateArray().Where(s => s.GetProperty("isCurrent").GetBoolean()).ToList();
        Assert.Single(current);

        var labels = sessions.EnumerateArray().Select(s => s.GetProperty("deviceLabel").GetString()).ToHashSet();
        Assert.Contains("Pixel de Anderson", labels);
        Assert.Contains("iPhone de prueba", labels);
    }

    [Fact]
    public async Task Revoking_another_session_makes_its_refresh_token_unusable_but_leaves_the_caller_alone()
    {
        var first = await RegisterAsync(factory, "Dispositivo uno");
        var second = await LoginAsync(factory, first.Email, "Dispositivo dos");

        var sessions = await GetSessionsAsync(first.Client);
        var otherId = sessions.EnumerateArray().First(s => !s.GetProperty("isCurrent").GetBoolean()).GetProperty("id").GetGuid();

        var revoke = await first.Client.DeleteAsync($"/api/v1/auth/sessions/{otherId}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        // Device two's refresh token is now dead...
        var deadRefresh = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = second.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, deadRefresh.StatusCode);

        // ...but device one (the caller) was never touched.
        var stillWorks = await first.Client.GetAsync("/api/v1/auth/sessions");
        await stillWorks.EnsureOkAsync();
    }

    [Fact]
    public async Task Revoking_your_own_current_session_is_refused_in_favor_of_logout()
    {
        var user = await RegisterAsync(factory);

        var sessions = await GetSessionsAsync(user.Client);
        var currentId = sessions.EnumerateArray().First(s => s.GetProperty("isCurrent").GetBoolean()).GetProperty("id").GetGuid();

        var response = await user.Client.DeleteAsync($"/api/v1/auth/sessions/{currentId}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Refused, not silently ignored: the session is still there afterwards.
        var afterwards = await GetSessionsAsync(user.Client);
        Assert.Contains(afterwards.EnumerateArray(), s => s.GetProperty("id").GetGuid() == currentId);
    }

    [Fact]
    public async Task Revoking_someone_elses_session_or_an_unknown_id_is_a_404_not_a_leak()
    {
        var owner = await RegisterAsync(factory, "Dispositivo del dueño");
        await LoginAsync(factory, owner.Email, "Segundo dispositivo del dueño");
        var ownerSessions = await GetSessionsAsync(owner.Client);
        var ownerOtherSessionId = ownerSessions.EnumerateArray()
            .First(s => !s.GetProperty("isCurrent").GetBoolean())
            .GetProperty("id").GetGuid();

        var intruder = await RegisterAsync(factory);

        var responseToOwners = await intruder.Client.DeleteAsync($"/api/v1/auth/sessions/{ownerOtherSessionId}");
        Assert.Equal(HttpStatusCode.NotFound, responseToOwners.StatusCode);

        var responseToUnknown = await intruder.Client.DeleteAsync($"/api/v1/auth/sessions/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, responseToUnknown.StatusCode);

        // Neither attempt touched the owner's real session.
        var ownerSessionsAfter = await GetSessionsAsync(owner.Client);
        Assert.Equal(2, ownerSessionsAfter.GetArrayLength());
    }

    [Fact]
    public async Task Logout_all_revokes_every_session_including_the_one_that_called_it()
    {
        var first = await RegisterAsync(factory, "Dispositivo uno");
        var second = await LoginAsync(factory, first.Email, "Dispositivo dos");

        var response = await first.Client.PostAsync("/api/v1/auth/logout-all", content: null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var firstRefresh = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, firstRefresh.StatusCode);

        var secondRefresh = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = second.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, secondRefresh.StatusCode);
    }

    private static async Task<JsonElement> GetSessionsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/auth/sessions");
        await response.EnsureOkAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<(string Email, string RefreshToken, HttpClient Client)> RegisterAsync(
        NexoApiFactory factory,
        string? deviceLabel = null)
    {
        var client = factory.CreateClient();
        if (deviceLabel is not null)
        {
            client.DefaultRequestHeaders.Add("X-Device-Label", deviceLabel);
        }

        var email = $"user-{Guid.CreateVersion7():N}@nexo.test";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName = "Usuario de prueba",
            acceptedTerms = true,
            confirmedAdult = true,
        });
        await response.EnsureOkAsync();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload.GetProperty("accessToken").GetString());

        return (email, payload.GetProperty("refreshToken").GetString()!, client);
    }

    private static async Task<(string RefreshToken, HttpClient Client)> LoginAsync(
        NexoApiFactory factory,
        string email,
        string? deviceLabel = null)
    {
        var client = factory.CreateClient();
        if (deviceLabel is not null)
        {
            client.DefaultRequestHeaders.Add("X-Device-Label", deviceLabel);
        }

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        await response.EnsureOkAsync();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload.GetProperty("accessToken").GetString());

        return (payload.GetProperty("refreshToken").GetString()!, client);
    }
}
