using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nexo.Application.Abstractions;

namespace Nexo.Infrastructure.Push;

/// <summary>
/// Sends through Expo's push service, which is what an Expo-managed React Native
/// app uses on both Android and iOS. No credentials are required for the MVP;
/// production should add the Expo access token as an Authorization header.
/// </summary>
public sealed class ExpoPushSender(HttpClient http, ILogger<ExpoPushSender> logger) : IPushSender
{
    private const int BatchSize = 100;

    /// <summary>A token Expo will never accept again -- the app was uninstalled, or the token was revoked.</summary>
    private const string DeviceNotRegistered = "DeviceNotRegistered";

    private sealed record ExpoMessage(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("data")] IReadOnlyDictionary<string, string>? Data,
        [property: JsonPropertyName("sound")] string Sound = "default",
        [property: JsonPropertyName("priority")] string Priority = "high");

    private sealed record ExpoTicket(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("details")] ExpoTicketDetails? Details);

    private sealed record ExpoTicketDetails([property: JsonPropertyName("error")] string? Error);

    private sealed record ExpoResponse([property: JsonPropertyName("data")] IReadOnlyList<ExpoTicket>? Data);

    public async Task<PushSendResult> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return new PushSendResult(0, []);
        }

        var sent = 0;
        var invalidTokens = new List<string>();

        foreach (var batch in messages.Chunk(BatchSize))
        {
            var payload = batch
                .Select(m => new ExpoMessage(m.ExpoPushToken, m.Title, m.Body, m.Data))
                .ToArray();

            using var response = await http.PostAsJsonAsync("/--/api/v2/push/send", payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Never log the tokens or the bodies: both are personal data.
                logger.LogWarning(
                    "Expo push rejected a batch of {Count} messages with status {StatusCode}.",
                    payload.Length,
                    (int)response.StatusCode);
                continue;
            }

            // Expo answers per-message: the batch itself can be a 200 while
            // individual tickets still failed ("DeviceNotRegistered" chief among
            // them). One ExpoResponse.Data entry per submitted message, same order.
            var body = await response.Content.ReadFromJsonAsync<ExpoResponse>(cancellationToken: cancellationToken);
            var tickets = body?.Data;

            if (tickets is null || tickets.Count != batch.Length)
            {
                // Malformed or unexpected shape: assume the whole batch went
                // through rather than silently losing count, and never guess at
                // which token to retire without a ticket to back it up.
                sent += payload.Length;
                continue;
            }

            for (var i = 0; i < tickets.Count; i++)
            {
                var ticket = tickets[i];
                if (string.Equals(ticket.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    sent++;
                }
                else if (string.Equals(ticket.Details?.Error, DeviceNotRegistered, StringComparison.Ordinal))
                {
                    invalidTokens.Add(batch[i].ExpoPushToken);
                }
                else
                {
                    logger.LogWarning(
                        "Expo push ticket failed with error {Error}.",
                        ticket.Details?.Error ?? "(unknown)");
                }
            }
        }

        return new PushSendResult(sent, invalidTokens);
    }
}

/// <summary>Used in development and tests so nothing leaves the machine.</summary>
public sealed class NoOpPushSender(ILogger<NoOpPushSender> logger) : IPushSender
{
    public Task<PushSendResult> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        logger.LogInformation("Push disabled: {Count} notification(s) were not delivered.", messages.Count);
        return Task.FromResult(new PushSendResult(0, []));
    }
}
