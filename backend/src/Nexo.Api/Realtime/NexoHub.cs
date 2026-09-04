using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Nexo.Application.Abstractions;

namespace Nexo.Api.Realtime;

/// <summary>
/// Push channel for "something changed, refresh". It deliberately carries no
/// financial data: the client receives a signal and re-fetches over the
/// authenticated REST API, so the socket never becomes a second, weaker way to
/// read someone's movements.
/// </summary>
[Authorize]
public sealed class NexoHub : Hub
{
}

public sealed class SignalRRealtimeNotifier(IHubContext<NexoHub> hub) : IRealtimeNotifier
{
    public Task TransactionsChangedAsync(Guid userId, int newCount, CancellationToken cancellationToken) =>
        hub.Clients.User(userId.ToString())
            .SendAsync("transactionsChanged", new { newCount }, cancellationToken);

    public Task ImportProgressAsync(Guid userId, Guid importId, string status, CancellationToken cancellationToken) =>
        hub.Clients.User(userId.ToString())
            .SendAsync("importProgress", new { importId, status }, cancellationToken);
}
