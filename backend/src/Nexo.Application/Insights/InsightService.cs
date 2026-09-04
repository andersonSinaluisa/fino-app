using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;

namespace Nexo.Application.Insights;

public interface IInsightService
{
    Task<IReadOnlyList<InsightDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InsightDto>> RefreshAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class InsightService(INexoDbContext db, IInsightEngine engine) : IInsightService
{
    public async Task<IReadOnlyList<InsightDto>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Insights
            .AsNoTracking()
            .Where(i => i.UserId == userId)
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
                i.PeriodEnd))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InsightDto>> RefreshAsync(Guid userId, CancellationToken cancellationToken)
    {
        await engine.RecomputeAsync(userId, cancellationToken);
        return await ListAsync(userId, cancellationToken);
    }
}
