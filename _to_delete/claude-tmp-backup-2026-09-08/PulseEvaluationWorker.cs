using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Application.Pulses;
using Nexo.Domain.Users;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Workers;

/// <summary>
/// PULSO: runs PulseEngine for every active user, on a timer -- the same shape
/// as <see cref="InsightRefreshWorker"/>, deliberately, since both are
/// "recompute something derived from this user's transactions on a schedule".
/// Unlike that worker, a failed or skipped pass here never loses anything:
/// pulses are additive and dedup means a later pass just picks up whatever the
/// missed one would have found. FASE 3: each pass also runs whatever it created
/// through <see cref="IPulseNotificationDecisionService"/>, the same one
/// <c>PulseService.EvaluateAsync</c> uses for the manual "actualizar" affordance,
/// so a pulse notifies the same way regardless of what triggered its evaluation.
/// </summary>
public sealed class PulseEvaluationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<PulseEvaluationWorker> logger) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Pulse evaluation worker is disabled.");
            return;
        }

        var period = TimeSpan.FromHours(Math.Max(1, _options.PulseEvaluationHours));

        // Let the host finish starting before the first pass.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

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
                // A failed pass must not kill the worker: the next one picks up
                // whatever this one missed, same as InsightRefreshWorker.
                logger.LogError(ex, "Pulse evaluation pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<IPulseEngine>();
        var notifier = scope.ServiceProvider.GetRequiredService<IPulseNotificationDecisionService>();

        var userIds = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Status == UserStatus.Active)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var created = 0;
        var evaluated = 0;
        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var pulses = await engine.EvaluateAsync(userId, cancellationToken);
                created += pulses.Count;
                evaluated++;

                if (pulses.Count > 0)
                {
                    await notifier.NotifyAsync(userId, pulses, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not evaluate pulses for user {UserId}.", userId);
            }
        }

        logger.LogInformation(
            "Pulse evaluation ran for {Evaluated}/{Total} users, {Created} new pulses.",
            evaluated,
            userIds.Count,
            created);
    }
}
