using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Common;
using Nexo.Domain.Pulses;

namespace Nexo.Application.Pulses;

public interface IPulseService
{
    /// <summary>Most relevant first, most recent first within a tie -- the same ordering a future "show at most N" UI would want.</summary>
    Task<IReadOnlyList<PulseDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// One pulse by id -- the detail screen's data source. Fetched by id
    /// rather than only ever read out of the list response so a future push
    /// notification can deep-link straight here (FASE 3) without depending on
    /// GET /pulses having already populated the client's cache.
    /// </summary>
    Task<PulseDto> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the engine on demand, notifies about whatever qualifies (see
    /// <see cref="IPulseNotificationDecisionService"/>), and returns the resulting
    /// list. PulseEvaluationWorker does the same two steps itself on its own
    /// timer rather than calling this; this exists for a manual "actualizar"
    /// affordance and for tests, the same role IInsightService.RefreshAsync plays
    /// for insights.
    /// </summary>
    Task<IReadOnlyList<PulseDto>> EvaluateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// PULSO FASE 4: records the 👍/👎 a person leaves on the detail screen.
    /// Idempotent per pulse -- calling it again with a different value just
    /// replaces the previous feedback, since changing your mind is fine.
    /// </summary>
    Task<PulseDto> SubmitFeedbackAsync(Guid userId, Guid id, bool helpful, CancellationToken cancellationToken);
}

public sealed class PulseService(INexoDbContext db, IPulseEngine engine, IPulseNotificationDecisionService notifier, IClock clock) : IPulseService
{
    public async Task<IReadOnlyList<PulseDto>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Pulses
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.RelevanceScore)
            .ThenByDescending(p => p.CreatedAt)
            .Select(ProjectToDto)
            .ToListAsync(cancellationToken);

    public async Task<PulseDto> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await db.Pulses
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Id == id)
            .Select(ProjectToDto)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Pulse", id);

    public async Task<IReadOnlyList<PulseDto>> EvaluateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var newPulses = await engine.EvaluateAsync(userId, cancellationToken);
        // PULSO FASE 3: the manual "actualizar" affordance notifies exactly like a
        // background pass would -- same decision service, same severity gate and
        // grouping, so nothing about whether a push goes out depends on who
        // triggered the evaluation.
        await notifier.NotifyAsync(userId, newPulses, cancellationToken);
        return await ListAsync(userId, cancellationToken);
    }

    public async Task<PulseDto> SubmitFeedbackAsync(Guid userId, Guid id, bool helpful, CancellationToken cancellationToken)
    {
        var pulse = await db.Pulses.FirstOrDefaultAsync(p => p.UserId == userId && p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Pulse", id);

        pulse.RecordFeedback(helpful, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(userId, id, cancellationToken);
    }

    private static readonly System.Linq.Expressions.Expression<Func<FinancialPulse, PulseDto>> ProjectToDto = p =>
        new PulseDto(
            p.Id,
            p.Type.ToString(),
            p.Severity.ToString(),
            p.Title,
            p.Body,
            p.Explanation,
            p.Value,
            p.ComparisonValue,
            p.PercentChange,
            p.ReferenceId,
            p.RelevanceScore,
            p.PeriodStart,
            p.PeriodEnd,
            p.OccurredAt,
            p.CreatedAt,
            p.FeedbackHelpful);
}
