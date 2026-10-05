using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Application.Reminders;
using Nexo.Domain.Users;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Workers;

/// <summary>
/// Recordatorios: evaluates <see cref="IReminderService"/> for every active user
/// every <see cref="WorkerOptions.ReminderIntervalMinutes"/>. The service decides
/// whether it is the right local hour and dedups every reminder, so running more
/// often than needed (or on two instances) never sends anything twice. Each user
/// gets a fresh scope, so a failed save for one never leaks into the next.
/// </summary>
public sealed class ReminderWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<ReminderWorker> logger) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Reminder worker is disabled.");
            return;
        }

        var period = TimeSpan.FromMinutes(Math.Clamp(_options.ReminderIntervalMinutes, 5, 55));

        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(period);
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
                logger.LogError(ex, "Reminder pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        List<Guid> userIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            userIds = await db.Users
                .IgnoreQueryFilters()
                .Where(u => u.Status == UserStatus.Active)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);
        }

        var sent = 0;
        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reminders = scope.ServiceProvider.GetRequiredService<IReminderService>();
                sent += await reminders.RunAsync(userId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not evaluate reminders for user {UserId}.", userId);
            }
        }

        if (sent > 0)
        {
            logger.LogInformation("Reminders sent: {Sent} across {Users} users.", sent, userIds.Count);
        }
    }
}
