using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Nexo.Infrastructure.Push;

/// <summary>
/// Expo acepta un push con un "ticket" y recién minutos después sabe si
/// Apple/Google lo entregaron. Un token muerto (app desinstalada) muchas
/// veces solo aparece en ese recibo, no en el ticket. Esta cola guarda en
/// memoria los tickets aceptados para revisarlos más tarde; si la API se
/// reinicia se pierden, lo cual solo retrasa la limpieza de un token muerto
/// hasta el próximo envío.
/// </summary>
public sealed class ExpoReceiptQueue
{
    private readonly ConcurrentQueue<(string TicketId, string Token, DateTimeOffset SentAt)> _pending = new();

    /// <summary>Tope para no crecer sin límite si el worker no corre.</summary>
    private const int MaxPending = 20_000;

    public void Enqueue(string ticketId, string token, DateTimeOffset sentAt)
    {
        if (_pending.Count < MaxPending)
        {
            _pending.Enqueue((ticketId, token, sentAt));
        }
    }

    /// <summary>Saca los tickets enviados antes de <paramref name="olderThan"/> (los recibos tardan ~15 min).</summary>
    public IReadOnlyList<(string TicketId, string Token)> DequeueReady(DateTimeOffset olderThan, int max)
    {
        var ready = new List<(string, string)>();
        while (ready.Count < max && _pending.TryPeek(out var head) && head.SentAt <= olderThan)
        {
            if (_pending.TryDequeue(out var item))
            {
                ready.Add((item.TicketId, item.Token));
            }
        }

        return ready;
    }
}

/// <summary>Consulta los recibos de Expo y devuelve los tokens que ya no existen.</summary>
public sealed class ExpoReceiptChecker(HttpClient http, ILogger<ExpoReceiptChecker> logger)
{
    private sealed record Receipt(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("details")] ReceiptDetails? Details);

    private sealed record ReceiptDetails([property: JsonPropertyName("error")] string? Error);

    private sealed record ReceiptsResponse([property: JsonPropertyName("data")] Dictionary<string, Receipt>? Data);

    public async Task<IReadOnlyList<string>> FindDeadTokensAsync(
        IReadOnlyList<(string TicketId, string Token)> tickets,
        CancellationToken cancellationToken)
    {
        var dead = new List<string>();
        // Expo acepta hasta 1000 ids por consulta.
        foreach (var batch in tickets.Chunk(1000))
        {
            using var response = await http.PostAsJsonAsync(
                "/--/api/v2/push/getReceipts",
                new { ids = batch.Select(t => t.TicketId).ToArray() },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Expo receipts request failed with status {StatusCode}.", (int)response.StatusCode);
                continue;
            }

            var body = await response.Content.ReadFromJsonAsync<ReceiptsResponse>(cancellationToken: cancellationToken);
            if (body?.Data is null)
            {
                continue;
            }

            foreach (var (ticketId, token) in batch)
            {
                if (!body.Data.TryGetValue(ticketId, out var receipt)
                    || string.Equals(receipt.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(receipt.Details?.Error, "DeviceNotRegistered", StringComparison.Ordinal))
                {
                    dead.Add(token);
                }
                else
                {
                    // Nunca se registran tokens ni contenidos: son datos personales.
                    logger.LogWarning("Expo push receipt error {Error}.", receipt.Details?.Error ?? "(unknown)");
                }
            }
        }

        return dead;
    }
}
