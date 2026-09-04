using Nexo.Domain.Notifications;

namespace Nexo.Application.Abstractions;

public sealed record PushMessage(
    string ExpoPushToken,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null);

public interface IPushSender
{
    Task<int> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken);
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
}
