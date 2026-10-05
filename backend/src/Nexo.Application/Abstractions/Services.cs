using Nexo.Domain.Notifications;

namespace Nexo.Application.Abstractions;

public sealed record PushMessage(
    string ExpoPushToken,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// Entregable 18 ("Push end-to-end"): a batch send can partially fail per
/// token -- an app uninstall or a stale token makes Expo answer that specific
/// ticket with "DeviceNotRegistered" even though the HTTP call itself was a
/// 200. Surfacing those tokens is what lets NotificationDispatcher retire the
/// Device row instead of paying for a doomed push forever.
/// </summary>
public sealed record PushSendResult(int SentCount, IReadOnlyList<string> InvalidTokens);

public interface IPushSender
{
    Task<PushSendResult> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken);
}

/// <summary>Real-time fan-out to connected clients (SignalR in the API host).</summary>
public interface IRealtimeNotifier
{
    Task TransactionsChangedAsync(Guid userId, int newCount, CancellationToken cancellationToken);

    Task ImportProgressAsync(Guid userId, Guid importId, string status, CancellationToken cancellationToken);
}

public interface INotificationDispatcher
{
    Task DispatchAsync(Notification notification, CancellationToken cancellationToken);

    /// <summary>
    /// "Enviar notificación de prueba" (Perfil → Notificaciones): un push a cada
    /// dispositivo de la persona con las notificaciones activadas, sin guardar
    /// nada en su historial. Ignora categorías y horario silencioso a propósito:
    /// lo pidió ella misma, ahora.
    /// </summary>
    Task<PushTestResult> SendTestAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Devices: dispositivos con push activado; Sent: los que Expo aceptó.</summary>
public sealed record PushTestResult(int Devices, int Sent);
