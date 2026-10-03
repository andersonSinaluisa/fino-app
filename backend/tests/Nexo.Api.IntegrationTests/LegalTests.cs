using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Documentos legales y consentimientos (LOPDP arts. 8, 12 y 21): sin 18+ y
/// aceptación explícita no hay cuenta; la aceptación queda registrada con su
/// versión; los datos de uso son opcionales, apagados por defecto y revocables;
/// y los documentos son públicos (para las tiendas) sin dejar pasar HTML.
/// </summary>
public class LegalTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Password = "NexoIntegration2026!";

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Registration_requires_adult_confirmation_and_explicit_acceptance(bool acceptedTerms, bool confirmedAdult)
    {
        var client = factory.CreateClient();
        var email = $"legal-{Guid.CreateVersion7():N}@nexo.test";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName = "Sin aceptar",
            acceptedTerms,
            confirmedAdult,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // No quedó una cuenta a medias: el mismo correo puede registrarse luego.
        var retry = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = Password,
            displayName = "Ahora sí",
            acceptedTerms = true,
            confirmedAdult = true,
        });
        await retry.EnsureOkAsync();
    }

    [Fact]
    public async Task Registration_records_the_accepted_versions_and_analytics_is_off_unless_chosen()
    {
        var user = await factory.RegisterUserAsync();

        var status = await user.GetJsonAsync("/api/v1/legal/status");
        Assert.False(status.GetProperty("needsAcceptance").GetBoolean());
        Assert.Equal(status.GetProperty("termsVersion").GetString(), status.GetProperty("acceptedTermsVersion").GetString());
        Assert.Equal(status.GetProperty("privacyVersion").GetString(), status.GetProperty("acceptedPrivacyVersion").GetString());
        Assert.False(status.GetProperty("analyticsConsent").GetBoolean());
        Assert.Equal("info@brix-dev.com", status.GetProperty("contactEmail").GetString());

        var granted = await user.Client.PutAsJsonAsync("/api/v1/legal/analytics", new { granted = true });
        await granted.EnsureOkAsync();
        Assert.True((await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("analyticsConsent").GetBoolean());

        var revoked = await user.Client.PutAsJsonAsync("/api/v1/legal/analytics", new { granted = false });
        await revoked.EnsureOkAsync();
        Assert.False((await user.GetJsonAsync("/api/v1/legal/status")).GetProperty("analyticsConsent").GetBoolean());
    }

    [Fact]
    public async Task Registration_can_opt_into_analytics_explicitly()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"legal-{Guid.CreateVersion7():N}@nexo.test",
            password = Password,
            displayName = "Con analítica",
            acceptedTerms = true,
            confirmedAdult = true,
            analyticsConsent = true,
        });
        await response.EnsureOkAsync();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var status = await (await client.GetAsync("/api/v1/legal/status")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status.GetProperty("analyticsConsent").GetBoolean());
    }

    [Fact]
    public async Task Accepting_an_outdated_version_is_rejected()
    {
        var user = await factory.RegisterUserAsync();
        var status = await user.GetJsonAsync("/api/v1/legal/status");

        var stale = await user.Client.PostAsJsonAsync("/api/v1/legal/accept", new { termsVersion = "2000-01-01", privacyVersion = "2000-01-01", confirmedAdult = true });
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);

        var withoutAge = await user.Client.PostAsJsonAsync("/api/v1/legal/accept", new
        {
            termsVersion = status.GetProperty("termsVersion").GetString(),
            privacyVersion = status.GetProperty("privacyVersion").GetString(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, withoutAge.StatusCode);

        var current = await user.Client.PostAsJsonAsync("/api/v1/legal/accept", new
        {
            termsVersion = status.GetProperty("termsVersion").GetString(),
            privacyVersion = status.GetProperty("privacyVersion").GetString(),
            confirmedAdult = true,
            analyticsConsent = true,
        });
        await current.EnsureOkAsync();
        var after = await current.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(after.GetProperty("needsAcceptance").GetBoolean());
        Assert.True(after.GetProperty("analyticsConsent").GetBoolean());
    }

    [Fact]
    public async Task Documents_are_public_and_versioned()
    {
        var client = factory.CreateClient();

        var privacy = await (await client.GetAsync("/api/v1/legal/privacidad")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("privacidad", privacy.GetProperty("kind").GetString());
        Assert.False(string.IsNullOrWhiteSpace(privacy.GetProperty("version").GetString()));
        var markdown = privacy.GetProperty("markdown").GetString()!;
        Assert.Contains("info@brix-dev.com", markdown);
        Assert.Contains("18 años", markdown);
        Assert.DoesNotContain("{{", markdown);

        var terms = await (await client.GetAsync("/api/v1/legal/terms")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("no** presta asesoría financiera", terms.GetProperty("markdown").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/legal/otra-cosa")).StatusCode);

        var page = await client.GetAsync("/legal/privacidad");
        await page.EnsureOkAsync();
        Assert.StartsWith("text/html", page.Content.Headers.ContentType?.ToString());
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("<h1>Política de privacidad de Fino</h1>", html);
        Assert.Contains("<strong>", html);
        Assert.DoesNotContain("**", html);
    }

    [Fact]
    public async Task Legal_status_requires_authentication()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/legal/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/v1/legal/analytics", new { granted = true })).StatusCode);
    }

    [Fact]
    public async Task Data_export_can_be_downloaded_through_a_short_lived_link_without_the_session_token()
    {
        var user = await factory.RegisterUserAsync();
        var link = await user.Client.PostAsync("/api/v1/privacy/export-link", null);
        await link.EnsureOkAsync();
        var path = (await link.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("path").GetString()!;

        var browser = factory.CreateClient();
        var download = await browser.GetAsync(path);
        await download.EnsureOkAsync();
        var export = await download.Content.ReadAsStringAsync();
        Assert.Contains(user.Email, export);
        Assert.Contains("consents", export);
        Assert.Contains("AgeConfirmation", export);

        var tampered = await browser.GetAsync(path[..^4] + "AAAA");
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
    }
}
