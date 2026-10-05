using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Domain.Notifications;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Push;

namespace Nexo.Workers;

/// <summary>
/// Cada 15 minutos revisa los recibos de Expo de los pushes enviados hace al
/// menos 15 minutos y borra los dispositivos cuyo token ya no existe
/// (DeviceNotRegistered). Así un teléfono donde desinstalaron Fino deja de
/// recibir intentos de envío para siempre.
/// </summary>
public sealed class PushReceiptWorker(
    IServiceScopeFactory scopeFactory,
    ExpoReceiptQueue queue,
    IOptions<WorkerOptions> options,
    ILogger<PushReceiptWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
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
                logger.LogError(ex, "Push receipt pass failed.");
            }
        }
    }

    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        var ready = queue.DequeueReady(DateTimeOffset.UtcNow - Interval, max: 5000);
        if (ready.Count == 0)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var checker = scope.ServiceProvider.GetService<ExpoReceiptChecker>();
        if (checker is null)
        {
            return; // push desactivado
        }

        var dead = (await checker.FindDeadTokensAsync(ready, cancellationToken)).ToHashSet(StringComparer.Ordinal);
        if (dead.Count == 0)
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var devices = await db.Set<Device>()
            .IgnoreQueryFilters()
            .Where(d => dead.Contains(d.ExpoPushToken))
            .ToListAsync(cancellationToken);

        db.RemoveRange(devices);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Removed {Count} device(s) with unregistered push tokens.", devices.Count);
    }
}
