using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Setup;

/// <summary>
/// Readiness depends on the database being reachable; liveness does not, so a slow
/// database restarts nothing.
/// </summary>
public sealed class DatabaseHealthCheck(NexoDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database reachable.")
                : HealthCheckResult.Unhealthy("Database unreachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database check failed.", ex);
        }
    }
}
