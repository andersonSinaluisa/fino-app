using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 23 ("Preparar email ingestion, sin OAuth"): the inbound webhook
/// (<c>POST /api/v1/email-ingestion/inbound</c>) is anonymous by necessity -- a mail
/// relay has no Nexo user session -- which makes its HMAC check the entire perimeter
/// protecting it. That check existed and worked before this entregable, but had no
/// test of its own anywhere in the suite; this file is that coverage, not a behavior
/// change. It intentionally does not assert on the parsed outcome (accepted /
/// rejected / not-a-movement) -- that logic already has its own tests
/// (BankEmailParserTests, CrossSourceDeduplicationTests). This is only about who
/// gets past the front door.
/// </summary>
public class EmailIngestionInboundTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Secret = "entregable-23-inbound-secret";
    private const string Path = "/api/v1/email-ingestion/inbound";

    private static WebApplicationFactory<Program> WithForwardingConfigured(NexoApiFactory factory) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Nexo:EmailIngestion:Forwarding:InboundDomain"] = "in.nexo.test",
                    ["Nexo:EmailIngestion:Forwarding:WebhookSecret"] = Secret,
                });
            }));

    private static string Sign(string payload, string secret) =>
        Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));

    private static string SamplePayload(Guid? userId = null) => JsonSerializer.Serialize(new
    {
        userId = userId ?? Guid.CreateVersion7(),
        emailConnectionId = (Guid?)null,
        messageId = $"msg-{Guid.CreateVersion7():N}",
        envelopeFrom = "alguien@dominio-no-confiable.test",
        fromDisplayName = "Alguien",
        subject = "Hola",
        plainTextBody = "Este correo no es una notificación bancaria.",
        receivedAt = DateTimeOffset.UtcNow,
        passedAuthentication = true,
    });

    [Fact]
    public async Task Without_forwarding_configured_the_endpoint_answers_503_regardless_of_signature()
    {
        // The shared factory: no Forwarding domain/secret set at all.
        var client = factory.CreateClient();
        var payload = SamplePayload();

        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(
            Nexo.Api.Endpoints.EmailIngestionEndpoints.SignatureHeader,
            Sign(payload, "any-secret-does-not-matter-here"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task A_correctly_signed_payload_is_accepted()
    {
        await using var gated = WithForwardingConfigured(factory);
        var client = gated.CreateClient();
        var payload = SamplePayload();

        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(Nexo.Api.Endpoints.EmailIngestionEndpoints.SignatureHeader, Sign(payload, Secret));

        var response = await client.SendAsync(request);
        await response.EnsureOkAsync();
    }

    [Fact]
    public async Task A_missing_signature_is_rejected()
    {
        await using var gated = WithForwardingConfigured(factory);
        var client = gated.CreateClient();
        var payload = SamplePayload();

        var response = await client.PostAsync(Path, new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_signature_computed_with_the_wrong_secret_is_rejected()
    {
        await using var gated = WithForwardingConfigured(factory);
        var client = gated.CreateClient();
        var payload = SamplePayload();

        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(Nexo.Api.Endpoints.EmailIngestionEndpoints.SignatureHeader, Sign(payload, "not-the-real-secret"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Proves the signature covers the whole payload, not just the message id --
    /// the exact replay this endpoint's own doc comment says it defends against
    /// (see EmailIngestionEndpoints.cs): sign one request, then change the userId
    /// while keeping every other byte (messageId included) identical. A signature
    /// that only covered messageId would still validate here; this one must not.
    /// </summary>
    [Fact]
    public async Task A_signature_from_a_different_payload_with_the_same_message_id_is_rejected()
    {
        await using var gated = WithForwardingConfigured(factory);
        var client = gated.CreateClient();

        var messageId = $"msg-{Guid.CreateVersion7():N}";
        var originalPayload = JsonSerializer.Serialize(new
        {
            userId = Guid.CreateVersion7(),
            emailConnectionId = (Guid?)null,
            messageId,
            envelopeFrom = "alguien@dominio-no-confiable.test",
            fromDisplayName = "Alguien",
            subject = "Hola",
            plainTextBody = "Cuerpo original.",
            receivedAt = DateTimeOffset.UtcNow,
            passedAuthentication = true,
        });
        var signatureForOriginal = Sign(originalPayload, Secret);

        var replayedPayload = JsonSerializer.Serialize(new
        {
            userId = Guid.CreateVersion7(), // a different victim account
            emailConnectionId = (Guid?)null,
            messageId, // same message id as the signed original
            envelopeFrom = "alguien@dominio-no-confiable.test",
            fromDisplayName = "Alguien",
            subject = "Hola",
            plainTextBody = "Cuerpo original.",
            receivedAt = DateTimeOffset.UtcNow,
            passedAuthentication = true,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(replayedPayload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(Nexo.Api.Endpoints.EmailIngestionEndpoints.SignatureHeader, signatureForOriginal);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
