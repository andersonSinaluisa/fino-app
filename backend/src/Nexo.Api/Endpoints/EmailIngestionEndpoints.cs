using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nexo.Application.EmailIngestion;
using Nexo.Infrastructure.Email;

namespace Nexo.Api.Endpoints;

/// <summary>
/// Inbound endpoint for forwarded bank notifications.
///
/// It is wired end to end and it works, but it only accepts traffic once
/// <c>Nexo:EmailIngestion:Forwarding</c> has a domain and a webhook secret: without
/// them there is no way to tell a real relay from anyone on the internet, so the
/// endpoint answers 503 instead of pretending. Gmail and Microsoft OAuth follow the
/// same rule (see docs/email-ingestion.md).
/// </summary>
public static class EmailIngestionEndpoints
{
    public const string SignatureHeader = "X-Nexo-Signature";

    private const int MaxBodyBytes = 512 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapEmailIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/email-ingestion/inbound", async (
                HttpContext http,
                IEmailIngestionPipeline pipeline,
                IOptions<EmailIngestionOptions> options,
                CancellationToken cancellationToken) =>
            {
                var forwarding = options.Value.Forwarding;
                if (!forwarding.IsConfigured)
                {
                    return Results.Problem(
                        title: "Ingesta por correo no configurada",
                        detail: "Configura Nexo:EmailIngestion:Forwarding para habilitar este endpoint.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                // The signature has to cover the whole payload. Signing only the
                // message id would let anyone who ever saw one valid pair replay it
                // with a different UserId and post movements into another account.
                var body = await ReadBodyAsync(http, cancellationToken);
                if (body is null)
                {
                    return Results.BadRequest();
                }

                var signature = http.Request.Headers[SignatureHeader].FirstOrDefault();
                if (!IsSignatureValid(signature, body, forwarding.WebhookSecret))
                {
                    return Results.Unauthorized();
                }

                InboundEmailRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<InboundEmailRequest>(body, JsonOptions);
                }
                catch (JsonException)
                {
                    return Results.BadRequest();
                }

                if (request is null || string.IsNullOrWhiteSpace(request.MessageId))
                {
                    return Results.BadRequest();
                }

                var message = new EmailMessage(
                    request.MessageId,
                    request.EnvelopeFrom,
                    request.FromDisplayName,
                    request.Subject,
                    request.PlainTextBody,
                    request.ReceivedAt,
                    request.PassedAuthentication);

                var result = await pipeline.ProcessAsync(
                    request.UserId,
                    request.EmailConnectionId,
                    message,
                    cancellationToken);

                return Results.Ok(result);
            })
            .AllowAnonymous()
            .WithTags("EmailIngestion")
            .RequireRateLimiting(RateLimitPolicies.Uploads)
            .WithSummary("Recibe una notificación bancaria reenviada por el relay de correo.");

        return app;
    }

    /// <summary>
    /// Reads at most <see cref="MaxBodyBytes"/>. Checking ContentLength alone is not
    /// enough: a chunked request has none, and the whole body would be buffered
    /// before any limit was applied.
    /// </summary>
    private static async Task<string?> ReadBodyAsync(HttpContext http, CancellationToken cancellationToken)
    {
        if (http.Request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var total = 0;

        while (true)
        {
            var read = await http.Request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaxBodyBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return total == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>HMAC-SHA256 over the raw payload, compared in constant time.</summary>
    private static bool IsSignatureValid(string? signature, string payload, string secret)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));

        var provided = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return provided.Length == expectedBytes.Length
               && CryptographicOperations.FixedTimeEquals(provided, expectedBytes);
    }
}

public sealed record InboundEmailRequest(
    Guid UserId,
    Guid? EmailConnectionId,
    string MessageId,
    string EnvelopeFrom,
    string? FromDisplayName,
    string Subject,
    string PlainTextBody,
    DateTimeOffset ReceivedAt,
    bool PassedAuthentication);
