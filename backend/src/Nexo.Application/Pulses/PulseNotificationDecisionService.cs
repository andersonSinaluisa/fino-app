using Nexo.Application.Abstractions;
using Nexo.Application.Notifications;
using Nexo.Domain.Common;
using Nexo.Domain.Notifications;
using Nexo.Domain.Pulses;

namespace Nexo.Application.Pulses;

public interface IPulseNotificationDecisionService
{
    /// <summary>
    /// Looks at whatever <see cref="IPulseEngine.EvaluateAsync"/> just created for
    /// one user this pass and decides what, if anything, is worth a push. Never
    /// re-evaluates or re-detects anything -- <paramref name="newPulses"/> is
    /// already the ground truth; this only decides who gets told and how.
    /// </summary>
    Task NotifyAsync(Guid userId, IReadOnlyList<FinancialPulse> newPulses, CancellationToken cancellationToken);
}

/// <summary>
/// PULSO FASE 3: the "NotificationDecisionService" named (but deliberately not
/// built yet) in <see cref="PulseEngine"/> and <see cref="Nexo.Workers.PulseEvaluationWorker"/>'s
/// remarks. Two policies live here, both explicitly out of scope for the
/// detection engine itself:
/// <list type="bullet">
/// <item><b>Severity gate:</b> a <see cref="PulseSeverity.Neutral"/> pulse (today
/// only AccountOutdated) describes a standing condition that is already sitting
/// on Home and in "Actividad de FINO" the moment it's computed -- pushing about
/// it would be noise about something quietly true yesterday too. Positive,
/// Attention and Risk pulses are all genuinely new information about one recent
/// event, which is what a push is for.</item>
/// <item><b>Grouping:</b> PulseEngine's own rules already cap themselves to one
/// pulse each, but several different rules can each fire in the same evaluation
/// pass (an unusual expense and a category spike in the same run, say). Sending
/// one push per pulse in that case is exactly the "manda varios" PULSO's spec
/// rules out, so multiple notify-worthy pulses from one pass become a single
/// grouped push instead -- its deep link still points at the single most
/// relevant one (<see cref="FinancialPulse.RelevanceScore"/>), so tapping it
/// never lands somewhere less useful than the in-app history would.</item>
/// </list>
/// </summary>
public sealed class PulseNotificationDecisionService(INotificationDispatcher dispatcher, IClock clock)
    : IPulseNotificationDecisionService
{
    public async Task NotifyAsync(Guid userId, IReadOnlyList<FinancialPulse> newPulses, CancellationToken cancellationToken)
    {
        var notifyWorthy = newPulses
            .Where(p => p.Severity != PulseSeverity.Neutral)
            .OrderByDescending(p => p.RelevanceScore)
            .ToList();

        if (notifyWorthy.Count == 0)
        {
            return;
        }

        var top = notifyWorthy[0];

        var (title, body) = notifyWorthy.Count == 1
            ? (top.Title, top.Body)
            : (
                $"{notifyWorthy.Count} novedades en tus finanzas",
                string.Join(" · ", notifyWorthy.Select(p => p.Title))
            );

        await dispatcher.DispatchAsync(
            Notification.Create(
                userId,
                NotificationType.PulseReady,
                title,
                body,
                clock.UtcNow,
                payload: $"{{\"pulseId\":\"{top.Id}\"}}"),
            cancellationToken);
    }
}
