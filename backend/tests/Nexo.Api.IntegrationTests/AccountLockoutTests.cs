using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Domain.Common;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 20 ("Hardening de seguridad"): the per-IP rate limit on
/// /auth/login (see RateLimitPolicies.Authentication) is disabled entirely in
/// this test host (NexoApiFactory sets Nexo:RateLimiting:Enabled=false), which
/// is exactly why these tests exist -- without a per-account counter, nothing
/// in this suite would stop an attacker who simply keeps guessing one
/// account's password forever. A dedicated FixedClock (via WithWebHostBuilder,
/// the same pattern NotificationsTests uses for its spy push sender) lets the
/// lockout-expiry test fast-forward past the lockout window without a real
/// 15-minute sleep, without disturbing the shared factory's clock that every
/// other test in the suite relies on for its March-2026 fixtures.
/// </summary>
public class AccountLockoutTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Password = "NexoIntegration2026!";
    private const string WrongPassword = "esa-no-es-la-clave";

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_correct_password_until_the_window_passes()
    {
        var clock = new FixedClock(NexoApiFactory.Now);
        await using var gated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
            }));

        var client = gated.CreateClient();
        var email = $"lockout-{Guid.CreateVersion7():N}@nexo.test";
        await Register(client, email);

        // 5 wrong attempts is the configured MaxFailedLoginAttempts: the 5th one
        // is the attempt that flips the account into lockout.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await Login(client, email, WrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // The correct password is rejected too -- proof the account is locked,
        // not just that the wrong password keeps failing.
        var duringLockout = await Login(client, email, Password);
        Assert.Equal(HttpStatusCode.Unauthorized, duringLockout.StatusCode);

        // Fast-forward past the default 15-minute lockout window.
        clock.UtcNow = NexoApiFactory.Now.AddMinutes(16);

        var afterLockout = await Login(client, email, Password);
        Assert.Equal(HttpStatusCode.OK, afterLockout.StatusCode);
    }

    [Fact]
    public async Task Locking_one_account_does_not_touch_another_users_login()
    {
        var client = factory.CreateClient();
        var lockedOut = $"lockout-{Guid.CreateVersion7():N}@nexo.test";
        var unaffected = $"lockout-{Guid.CreateVersion7():N}@nexo.test";
        await Register(client, lockedOut);
        await Register(client, unaffected);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Login(client, lockedOut, WrongPassword);
        }

        var stillLocked = await Login(client, lockedOut, Password);
        Assert.Equal(HttpStatusCode.Unauthorized, stillLocked.StatusCode);

        var otherAccount = await Login(client, unaffected, Password);
        Assert.Equal(HttpStatusCode.OK, otherAccount.StatusCode);
    }

    private static async Task Register(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName = "Usuario de prueba",
        });
        await response.EnsureOkAsync();
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
}
