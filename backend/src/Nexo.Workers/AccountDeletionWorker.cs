using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Domain.Common;
using Nexo.Domain.Users;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Workers;

/// <summary>
/// Completes "delete my Nexo account" after the grace period. Sessions and mailbox
/// grants are cut immediately when the request is made; this is the irreversible
/// part, so it waits, and it deletes rather than anonymises.
/// </summary>
public sealed class AccountDeletionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<AccountDeletionWorker> logger) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
        do
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Account deletion pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var cutoff = clock.UtcNow.AddDays(-Math.Max(1, _options.AccountDeletionGraceDays));

        var due = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Status == UserStatus.PendingDeletion
                        && u.DeletionRequestedAt != null
                        && u.DeletionRequestedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return;
        }

        // Every user-owned table cascades from users, so removing the aggregate root
        // is enough — and the cascade is defined in the model, not in this worker.
        db.Users.RemoveRange(due);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Completed deletion for {Count} account(s).", due.Count);
    }
}
