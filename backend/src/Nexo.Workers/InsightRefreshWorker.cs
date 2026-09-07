using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Application.Insights;
using Nexo.Domain.Users;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Workers;

public sealed class WorkerOptions
{
    public const string SectionName = "Nexo:Workers";

    public bool Enabled { get; set; } = true;

    public int InsightRefreshHours { get; set; } = 6;

    /// <summary>Days between "delete my account" and the irreversible erase.</summary>
    public int AccountDeletionGraceDays { get; set; } = 7;
}

/// <summary>
/// Recomputes insights for every active user, on a timer -- not only those who
/// had movement since the last pass. This also keeps month-scoped insights
/// (Entregable 16's ValidUntil) from lingering into a new month for someone who
/// simply stopped importing: a next pass always comes along and replaces the set,
/// on top of the defensive ValidUntil filter both readers already apply.
///
/// ADR-004: workers run in-process inside the API host. A separate deployable and a
/// queue would be the right call once ingestion is continuous; for an MVP whose
/// heaviest job is a per-user aggregate over a few hundred rows, a hosted service
/// is the honest amount of infrastructure.
/// </summary>
public sealed class InsightRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<InsightRefreshWorker> logger) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Insight refresh worker is disabled.");
            return;
        }

        var period = TimeSpan.FromHours(Math.Max(1, _options.InsightRefreshHours));

        // Let the host finish starting before the first pass.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ConfigureAwait(false);

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
                // A failed pass must not kill the worker: insights are derived data.
                logger.LogError(ex, "Insight refresh pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<IInsightEngine>();

        var userIds = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Status == UserStatus.Active)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var refreshed = 0;
        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await engine.RecomputeAsync(userId, cancellationToken);
                refreshed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not refresh insights for user {UserId}.", userId);
            }
        }

        logger.LogInformation("Insights refreshed for {Count}/{Total} users.", refreshed, userIds.Count);
    }
}
