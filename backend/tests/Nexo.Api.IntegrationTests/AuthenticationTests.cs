using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Regression cover for a whole-API outage that no business test could name.
///
/// The API used to read <c>Nexo:Jwt:SigningKey</c> out of <c>builder.Configuration</c>
/// while composing the bearer middleware, and then hand a *lazily bound*
/// <c>IOptions&lt;JwtOptions&gt;</c> to the service that signs tokens. Those are two
/// different views of configuration: anything layered on after the application's own
/// providers — a test host, a late provider, a secret store — reaches the second and
/// not the first. Sign-in kept returning 200 with a perfectly valid-looking token, and
/// every request made with it answered 401.
///
/// The tests below assert the property that was silently violated: a token this API
/// issues is a token this API accepts, and it stays accepted for its stated lifetime.
/// </summary>
public class AuthenticationTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    [Fact]
    public async Task A_token_this_api_issued_is_accepted_by_this_api()
    {
        var user = await factory.RegisterUserAsync();

        var response = await user.Client.GetAsync("/api/v1/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Signing_in_again_returns_a_token_that_also_works()
    {
        var user = await factory.RegisterUserAsync();

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = user.Email,
            password = "NexoIntegration2026!",
        });

        await login.EnsureOkAsync();

        var token = (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/accounts")).StatusCode);
    }

    /// <summary>
    /// `nbf`/`exp` are compared by the bearer middleware against the wall clock, so
    /// they must not be minted from <c>IClock</c> — which this suite pins to the month
    /// its statement fixtures describe. A token stamped with the pinned clock is born
    /// expired, and the only symptom is an unexplained 401.
    /// </summary>
    [Fact]
    public async Task Tokens_are_stamped_with_the_wall_clock_not_the_pinned_business_clock()
    {
        var user = await factory.RegisterUserAsync();

        var payload = ReadPayload(user.AccessToken);
        var notBefore = DateTimeOffset.FromUnixTimeSeconds(payload.GetProperty("nbf").GetInt64());
        var expires = DateTimeOffset.FromUnixTimeSeconds(payload.GetProperty("exp").GetInt64());
        var now = DateTimeOffset.UtcNow;

        Assert.True(
            expires > now,
            $"The access token expired at {expires:O}, before it was ever used (now {now:O}).");
        Assert.True(
            notBefore <= now.AddSeconds(30),
            $"The access token is not valid until {notBefore:O} (now {now:O}).");

        // The pinned business clock sits months away from the wall clock, so a token
        // that followed the wall clock cannot be sitting on the fixture's instant.
        Assert.True(
            Math.Abs((notBefore - NexoApiFactory.Now).TotalDays) > 1,
            "The access token was stamped with the pinned business clock.");
    }

    /// <summary>Decodes the JWT payload without pulling in a JWT library.</summary>
    private static JsonElement ReadPayload(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        return JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(payload));
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_rejected()
    {
        var user = await factory.RegisterUserAsync();

        // Flip one character of the signature: same header, same payload, wrong key.
        var parts = user.AccessToken.Split('.');
        var signature = parts[2];
        parts[2] = (signature[0] == 'A' ? 'B' : 'A') + signature[1..];

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Join('.', parts));

        var response = await client.GetAsync("/api/v1/accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
