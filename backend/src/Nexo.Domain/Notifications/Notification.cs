using Nexo.Domain.Common;

namespace Nexo.Domain.Notifications;

/// <summary>
/// Entregable 17: each value belongs to exactly one of the five preference
/// categories the person can toggle independently on a <see cref="Device"/> --
/// Movimientos, Ingresos, Insights, Seguridad, Recordatorios. Stored as a
/// string (see NotificationConfiguration), so adding, renaming or reordering
/// values here is free -- there is no persisted data to migrate.
/// </summary>
public enum NotificationType
{
    /// <summary>Movimientos: a purchase/expense was detected.</summary>
    ExpenseDetected = 0,

    /// <summary>Ingresos: money coming in was detected.</summary>
    IncomeDetected = 1,

    /// <summary>Movimientos: a statement import finished processing.</summary>
    ImportCompleted = 2,

    /// <summary>Insights: a new rules-based insight is ready. Not dispatched yet (see InsightRefreshWorker).</summary>
    InsightReady = 3,

    /// <summary>Recordatorios: the periodic income/expense recap. Not dispatched yet.</summary>
    WeeklySummary = 4,

    /// <summary>Recordatorios: a manually-imported account has gone stale (Entregable 15's AccountStaleness).</summary>
    AccountNeedsUpdate = 5,

    /// <summary>Seguridad: a suspicious/unauthenticated bank email was rejected.</summary>
    SecurityAlert = 6,

    /// <summary>
    /// PULSO FASE 3, categoría propia "Pulso": a new <see cref="Nexo.Domain.Pulses.FinancialPulse"/>
    /// cleared <see cref="Nexo.Application.Pulses.PulseNotificationDecisionService"/>'s
    /// severity gate. Dispatched by that service, not by the detection engine itself --
    /// see its docs for why financial detection and notification policy stay separate.
    /// </summary>
    PulseReady = 7,

    /// <summary>Recordatorios: a credit card payment is due soon (3 days before, and on the day).</summary>
    CardPaymentDue = 8,

    /// <summary>Recordatorios: a new statement period closed and can be imported/reviewed.</summary>
    StatementAvailable = 9,

    /// <summary>Recordatorios: a budget crossed 80% or 100% of its amount.</summary>
    BudgetThreshold = 10,
}

/// <summary>
/// In-app notification record. The stored body is the full text; what actually
/// goes out over push is decided per device, honouring the preview preference.
/// </summary>
public sealed class Notification : Entity, IUserOwned
{
    private Notification()
    {
    }

    public Guid UserId { get; private set; }

    public NotificationType Type { get; private set; }

    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    /// <summary>Small JSON blob used by the client to deep-link. No amounts beyond what the body shows.</summary>
    public string? Payload { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public DateTimeOffset? PushSentAt { get; private set; }

    /// <summary>
    /// Optional idempotency key for scheduled reminders ("card-due:{id}:{date}:3").
    /// Unique per user, so a reminder can never be sent twice even if the worker
    /// runs again or two instances overlap.
    /// </summary>
    public string? DedupKey { get; private set; }

    public static Notification Create(
        Guid userId,
        NotificationType type,
        string title,
        string body,
        DateTimeOffset now,
        string? payload = null,
        string? dedupKey = null)
    {
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            Title = DomainException.RequireText(title, nameof(title), 120),
            Body = DomainException.RequireText(body, nameof(body), 300),
            Payload = payload,
            DedupKey = dedupKey is null ? null : DomainException.RequireText(dedupKey, nameof(dedupKey), 160),
        };
        notification.Stamp(now);
        return notification;
    }

    public void MarkRead(DateTimeOffset now)
    {
        ReadAt ??= now;
        Stamp(now);
    }

    public void MarkPushed(DateTimeOffset now)
    {
        PushSentAt = now;
        Stamp(now);
    }
}
