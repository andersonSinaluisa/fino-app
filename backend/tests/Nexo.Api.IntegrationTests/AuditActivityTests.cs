using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 21 ("Auditoría"): GET /api/v1/auth/activity, the user's own
/// audit trail translated to plain Spanish sentences
/// (AuditActivityService.LabelFor). AuditLogEntry.UserId is nullable and the
/// entity does not implement IUserOwned, so unlike everything else in this
/// suite it never goes through the automatic per-user query filter -- these
/// tests are the only thing standing between "trust the filter" and "trust an
/// explicit predicate that happens to be correct".
/// </summary>
public class AuditActivityTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    [Fact]
    public async Task Registering_then_logging_in_shows_up_newest_first_in_plain_spanish()
    {
        var user = await factory.RegisterUserAsync();

        var login = await user.Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = user.Email,
            password = "NexoIntegration2026!",
        });
        await login.EnsureOkAsync();

        var activity = await user.GetJsonAsync("/api/v1/auth/activity");
        var items = activity.GetProperty("items").EnumerateArray().ToList();

        Assert.True(items.Count >= 2, "Expected at least the register and the login events.");

        var labels = items.Select(i => i.GetProperty("label").GetString()).ToList();
        Assert.Equal("Iniciaste sesión.", labels[0]);
        Assert.Contains("Creaste tu cuenta Nexo.", labels);

        // Newest first: the login (which happened after registering) leads.
        var createdAtValues = items.Select(i => i.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.True(
            createdAtValues.Zip(createdAtValues.Skip(1)).All(pair => pair.First >= pair.Second),
            "Expected the activity feed to be sorted newest first.");
    }

    [Fact]
    public async Task One_users_activity_never_shows_up_for_another_user()
    {
        var owner = await factory.RegisterUserAsync();
        var intruder = await factory.RegisterUserAsync();

        var ownerActivity = await owner.GetJsonAsync("/api/v1/auth/activity");
        var intruderActivity = await intruder.GetJsonAsync("/api/v1/auth/activity");

        var ownerIds = ownerActivity.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToHashSet();
        var intruderIds = intruderActivity.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToHashSet();

        Assert.DoesNotContain(ownerIds, intruderIds.Contains);
    }

    [Fact]
    public async Task A_failed_login_is_recorded_without_leaking_which_account_it_targeted_by_status_code()
    {
        var user = await factory.RegisterUserAsync();

        var badLogin = await user.Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = user.Email,
            password = "esa-no-es-la-clave",
        });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, badLogin.StatusCode);

        var activity = await user.GetJsonAsync("/api/v1/auth/activity");
        var items = activity.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, i => i.GetProperty("label").GetString() == "Alguien intentó iniciar sesión con la contraseña incorrecta."
            && !i.GetProperty("succeeded").GetBoolean());
    }
}
