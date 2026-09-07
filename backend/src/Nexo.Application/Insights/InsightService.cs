using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Domain.Common;

namespace Nexo.Application.Insights;

public interface IInsightService
{
    Task<IReadOnlyList<InsightDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InsightDto>> RefreshAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class InsightService(INexoDbContext db, IInsightEngine engine, IClock clock) : IInsightService
{
    public async Task<IReadOnlyList<InsightDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // Entregable 16 ("Insights v1"): "no generar insights irrelevantes"
        // applies here too -- ValidUntil is a defense on top of RecomputeAsync
        // always replacing the set, in case a scheduled refresh was missed.
        return await db.Insights
            .AsNoTracking()
            .Where(i => i.UserId == userId && i.ValidUntil > now)
            .OrderBy(i => i.DisplayOrder)
            .Select(i => new InsightDto(
                i.Code,
                i.Title,
                i.Body,
                i.Value,
                i.ComparisonValue,
                i.PercentChange,
                i.Severity.ToString(),
                i.ReferenceId,
                i.PeriodStart,
                i.PeriodEnd,
                i.ValidUntil))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InsightDto>> RefreshAsync(Guid userId, CancellationToken cancellationToken)
    {
        await engine.RecomputeAsync(userId, cancellationToken);
        return await ListAsync(userId, cancellationToken);
    }
}
