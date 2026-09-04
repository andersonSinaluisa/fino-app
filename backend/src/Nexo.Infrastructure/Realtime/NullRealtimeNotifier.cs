using Nexo.Application.Abstractions;

namespace Nexo.Infrastructure.Realtime;

/// <summary>
/// Default implementation for hosts without SignalR (workers, tests).
/// The API host replaces it with the hub-backed notifier.
/// </summary>
public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task TransactionsChangedAsync(Guid userId, int newCount, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ImportProgressAsync(Guid userId, Guid importId, string status, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
