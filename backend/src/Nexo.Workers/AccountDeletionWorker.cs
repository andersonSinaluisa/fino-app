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
/// part, so it waits, and it deletes rather than anonymises -- with one deliberate
/// exception, the audit log (see the Entregable 22 note in RunPassAsync).
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

        // Entregable 22 ("Privacidad completa"): AuditLogEntry deliberately does
        // not implement IUserOwned (a failed login against an email that never
        // existed has no user to attach to) and has no FK to users at all, so the
        // cascade below never touches it -- without this, a departing user's own
        // audit trail would be silently orphaned rather than either deleted or
        // kept on purpose. The decision here: keep the security signal (an entry
        // saying "auth.token_reuse_detected" still matters after the account is
        // gone) but detach it from an id that no longer resolves to anyone, the
        // same shape a failed login against a nonexistent account already has.
        foreach (var user in due)
        {
            await db.AuditLog
                .Where(a => a.UserId == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.UserId, (Guid?)null), cancellationToken);
        }

        // Every user-owned table cascades from users, so removing the aggregate root
        // is enough for everything else — and the cascade is defined in the model,
        // not in this worker.
        db.Users.RemoveRange(due);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Completed deletion for {Count} account(s).", due.Count);
    }
}
