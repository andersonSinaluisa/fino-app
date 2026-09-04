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

    private sealed record ExpoMessage(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("data")] IReadOnlyDictionary<string, string>? Data,
        [property: JsonPropertyName("sound")] string Sound = "default",
        [property: JsonPropertyName("priority")] string Priority = "high");

    public async Task<int> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return 0;
        }

        var sent = 0;

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

            sent += payload.Length;
        }

        return sent;
    }
}

/// <summary>Used in development and tests so nothing leaves the machine.</summary>
public sealed class NoOpPushSender(ILogger<NoOpPushSender> logger) : IPushSender
{
    public Task<int> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        logger.LogInformation("Push disabled: {Count} notification(s) were not delivered.", messages.Count);
        return Task.FromResult(0);
    }
}
