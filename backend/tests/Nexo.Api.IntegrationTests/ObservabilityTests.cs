using System.Net;
using Nexo.Api.Setup;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 28 ("Observabilidad"): the health endpoints and the correlation-id
/// contract had zero coverage before this file — every other test simply happened
/// to exercise them as a side effect of the middleware pipeline running, without
/// ever asserting on them. These tests assert directly on the two documented
/// operational contracts: /health/live and /health/ready never require
/// authentication and answer with the documented shape, and X-Correlation-Id is
/// either honoured or generated on every request.
///
/// What this suite cannot do in this harness: force /health/ready to report
/// Unhealthy. The integration host runs against a real (file-based) SQLite
/// database (see NexoApiFactory), so there is no seam here to simulate "the
/// database is down" without either faking IHealthCheck registration (which
/// would stop testing the real DatabaseHealthCheck) or tearing down the file
/// mid-request (which races the connection pool rather than reliably failing
/// it). The healthy path — the one this harness can exercise honestly — is
/// covered below; the unhealthy path is documented as untested in
/// docs/deployment.md instead of faked here.
/// </summary>
public class ObservabilityTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    [Fact]
    public async Task Health_live_answers_ok_without_authentication_and_checks_nothing()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await response.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task Health_ready_answers_ok_without_authentication_when_the_database_is_reachable()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await response.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task A_supplied_correlation_id_is_echoed_back_unchanged()
    {
        var client = factory.CreateClient();
        var correlationId = $"test-{Guid.CreateVersion7():N}";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);

        var response = await client.SendAsync(request);

        Assert.Equal(
            correlationId,
            Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));
    }

    [Fact]
    public async Task A_missing_correlation_id_is_generated_and_returned()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        var generated = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        Assert.False(string.IsNullOrWhiteSpace(generated));
        Assert.True(generated.Length <= 64);
    }

    [Fact]
    public async Task An_oversized_correlation_id_is_replaced_with_a_generated_one_instead_of_being_echoed()
    {
        var client = factory.CreateClient();
        var tooLong = new string('a', 65);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, tooLong);

        var response = await client.SendAsync(request);

        var returned = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        Assert.NotEqual(tooLong, returned);
        Assert.True(returned.Length <= 64);
    }
}
