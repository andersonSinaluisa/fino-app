using Nexo.Domain.Common;

namespace Nexo.Domain.Notifications;

public enum NotificationType
{
    TransactionDetected = 0,
    ImportCompleted = 1,
    AccountNeedsUpdate = 2,
    WeeklySummary = 3,
    SecurityAlert = 4,
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

    public static Notification Create(
        Guid userId,
        NotificationType type,
        string title,
        string body,
        DateTimeOffset now,
        string? payload = null)
    {
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            Title = DomainException.RequireText(title, nameof(title), 120),
            Body = DomainException.RequireText(body, nameof(body), 300),
            Payload = payload,
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
