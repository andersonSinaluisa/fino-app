using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Common;
using Nexo.Domain.Notifications;

namespace Nexo.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    bool IsRead,
    DateTimeOffset CreatedAt);

public sealed record RegisterDeviceRequest(
    string ExpoPushToken,
    string Platform,
    string? DeviceName,
    string? AppVersion);

public sealed record NotificationPreferencesDto(
    bool PushEnabled,
    bool ShowAmountsInPreview,
    bool NotifyOnNewTransaction,
    bool NotifyOnImportFinished,
    bool NotifyOnWeeklySummary);

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken);

    Task<NotificationPreferencesDto> RegisterDeviceAsync(Guid userId, RegisterDeviceRequest request, CancellationToken cancellationToken);

    Task<NotificationPreferencesDto> UpdatePreferencesAsync(Guid userId, string expoPushToken, NotificationPreferencesDto preferences, CancellationToken cancellationToken);
}

public sealed class NotificationService(INexoDbContext db, IClock clock) : INotificationService
{
    public async Task<IReadOnlyList<NotificationDto>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new NotificationDto(
                n.Id,
                n.Type.ToString(),
                n.Title,
                n.Body,
                n.ReadAt != null,
                n.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Notification", notificationId);

        notification.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<NotificationPreferencesDto> RegisterDeviceAsync(
        Guid userId,
        RegisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DevicePlatform>(request.Platform, ignoreCase: true, out var platform))
        {
            throw ValidationException.For(nameof(request.Platform), "Plataforma no válida.");
        }

        var now = clock.UtcNow;
        var device = await db.Devices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.ExpoPushToken == request.ExpoPushToken, cancellationToken);

        if (device is null)
        {
            device = Device.Register(userId, request.ExpoPushToken, platform, now, request.DeviceName, request.AppVersion);
            db.Devices.Add(device);
        }
        else
        {
            device.Touch(request.AppVersion, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Map(device);
    }

    public async Task<NotificationPreferencesDto> UpdatePreferencesAsync(
        Guid userId,
        string expoPushToken,
        NotificationPreferencesDto preferences,
        CancellationToken cancellationToken)
    {
        var device = await db.Devices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.ExpoPushToken == expoPushToken, cancellationToken)
            ?? throw new NotFoundException("Device", expoPushToken);

        device.UpdatePreferences(
            preferences.PushEnabled,
            preferences.ShowAmountsInPreview,
            preferences.NotifyOnNewTransaction,
            preferences.NotifyOnImportFinished,
            preferences.NotifyOnWeeklySummary,
            clock.UtcNow);

        await db.SaveChangesAsync(cancellationToken);
        return Map(device);
    }

    private static NotificationPreferencesDto Map(Device device) => new(
        device.PushEnabled,
        device.ShowAmountsInPreview,
        device.NotifyOnNewTransaction,
        device.NotifyOnImportFinished,
        device.NotifyOnWeeklySummary);
}

/// <summary>
/// Persists the notification and pushes it to the user's devices, honouring the
/// per-device preview preference: when previews are off, the push carries a title
/// only and the amount stays inside the app.
/// </summary>
public sealed class NotificationDispatcher(
    INexoDbContext db,
    IPushSender push,
    IClock clock,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    public async Task DispatchAsync(Notification notification, CancellationToken cancellationToken)
    {
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);

        var devices = await db.Devices
            .AsNoTracking()
            .Where(d => d.UserId == notification.UserId && d.PushEnabled)
            .ToListAsync(cancellationToken);

        var messages = new List<PushMessage>();
        foreach (var device in devices)
        {
            if (!ShouldNotify(device, notification.Type))
            {
                continue;
            }

            var body = device.ShowAmountsInPreview
                ? notification.Body
                : "Abre Nexo para ver el detalle.";

            messages.Add(new PushMessage(
                device.ExpoPushToken,
                notification.Title,
                body,
                new Dictionary<string, string> { ["notificationId"] = notification.Id.ToString() }));
        }

        if (messages.Count == 0)
        {
            return;
        }

        try
        {
            var sent = await push.SendAsync(messages, cancellationToken);
            if (sent > 0)
            {
                notification.MarkPushed(clock.UtcNow);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A push failure must never fail the operation that produced the movement.
            logger.LogWarning(ex, "Push delivery failed for notification {NotificationId}.", notification.Id);
        }
    }

    private static bool ShouldNotify(Device device, NotificationType type) => type switch
    {
        NotificationType.TransactionDetected => device.NotifyOnNewTransaction,
        NotificationType.ImportCompleted => device.NotifyOnImportFinished,
        NotificationType.WeeklySummary => device.NotifyOnWeeklySummary,
        _ => true,
    };
}
