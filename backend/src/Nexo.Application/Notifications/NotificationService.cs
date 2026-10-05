using System.Text.Json;
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
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string>? Data = null);

public sealed record RegisterDeviceRequest(
    string ExpoPushToken,
    string Platform,
    string? DeviceName,
    string? AppVersion);

/// <summary>
/// Entregable 17: one toggle per spec category (Movimientos, Ingresos, Insights,
/// Seguridad, Recordatorios), plus the two device-level switches that predate
/// this entregable and are not part of any category (PushEnabled turns push off
/// entirely; ShowAmountsInPreview is Entregable 18's "ocultar montos en previews").
/// PULSO FASE 3 adds a sixth category (NotifyOnPulses) and the "no molestar"
/// window (QuietHoursStartHour/QuietHoursEndHour, both null or both 0-23 --
/// see <see cref="Nexo.Domain.Notifications.Device.UpdatePreferences"/>), which
/// suppresses push for every category during that local-time window without
/// touching any of the toggles above.
/// </summary>
public sealed record NotificationPreferencesDto(
    bool PushEnabled,
    bool ShowAmountsInPreview,
    bool NotifyOnMovements,
    bool NotifyOnIncome,
    bool NotifyOnInsights,
    bool NotifyOnSecurity,
    bool NotifyOnReminders,
    bool NotifyOnPulses = true,
    int? QuietHoursStartHour = null,
    int? QuietHoursEndHour = null);

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken);

    Task<NotificationPreferencesDto> RegisterDeviceAsync(Guid userId, RegisterDeviceRequest request, CancellationToken cancellationToken);

    Task<NotificationPreferencesDto> UpdatePreferencesAsync(Guid userId, string expoPushToken, NotificationPreferencesDto preferences, CancellationToken cancellationToken);

    /// <summary>
    /// Entregable 18: called on logout so a shared device stops receiving a
    /// former session's push after the person signs out of it. Idempotent by
    /// design at the call site (best-effort, errors swallowed) but still 404s
    /// here like every other per-user lookup, for the same reason the others do.
    /// </summary>
    Task UnregisterDeviceAsync(Guid userId, string expoPushToken, CancellationToken cancellationToken);
}

public sealed class NotificationService(INexoDbContext db, IClock clock) : INotificationService
{
    public async Task<IReadOnlyList<NotificationDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new { n.Id, n.Type, n.Title, n.Body, IsRead = n.ReadAt != null, n.CreatedAt, n.Payload })
            .ToListAsync(cancellationToken);

        // The payload carries the same deep-link keys the push does (cardId,
        // budgetId, ...), so tapping a row in the history opens the same screen.
        return rows
            .Select(n => new NotificationDto(n.Id, n.Type.ToString(), n.Title, n.Body, n.IsRead, n.CreatedAt, NotificationPayload.Parse(n.Payload)))
            .ToList();
    }

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
            preferences.NotifyOnMovements,
            preferences.NotifyOnIncome,
            preferences.NotifyOnInsights,
            preferences.NotifyOnSecurity,
            preferences.NotifyOnReminders,
            preferences.NotifyOnPulses,
            preferences.QuietHoursStartHour,
            preferences.QuietHoursEndHour,
            clock.UtcNow);

        await db.SaveChangesAsync(cancellationToken);
        return Map(device);
    }

    public async Task UnregisterDeviceAsync(Guid userId, string expoPushToken, CancellationToken cancellationToken)
    {
        var device = await db.Devices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.ExpoPushToken == expoPushToken, cancellationToken)
            ?? throw new NotFoundException("Device", expoPushToken);

        db.Devices.Remove(device);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static NotificationPreferencesDto Map(Device device) => new(
        device.PushEnabled,
        device.ShowAmountsInPreview,
        device.NotifyOnMovements,
        device.NotifyOnIncome,
        device.NotifyOnInsights,
        device.NotifyOnSecurity,
        device.NotifyOnReminders,
        device.NotifyOnPulses,
        device.QuietHoursStartHour,
        device.QuietHoursEndHour);
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
    public async Task<PushTestResult> SendTestAsync(Guid userId, CancellationToken cancellationToken)
    {
        var devices = await db.Devices
            .AsNoTracking()
            .Where(d => d.UserId == userId && d.PushEnabled)
            .ToListAsync(cancellationToken);

        if (devices.Count == 0)
        {
            return new PushTestResult(0, 0);
        }

        var messages = devices
            .Select(d => new PushMessage(
                d.ExpoPushToken,
                "Fino",
                "Las notificaciones funcionan en este dispositivo.",
                new Dictionary<string, string> { ["type"] = "test" }))
            .ToList();

        var result = await push.SendAsync(messages, cancellationToken);

        if (result.InvalidTokens.Count > 0)
        {
            var stale = await db.Devices
                .Where(d => d.UserId == userId && result.InvalidTokens.Contains(d.ExpoPushToken))
                .ToListAsync(cancellationToken);
            db.Devices.RemoveRange(stale);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new PushTestResult(devices.Count, result.SentCount);
    }

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

            // PULSO FASE 3 ("no molestar"): this only skips the push for this
            // device. The notification row above is already saved regardless, so
            // the person still sees it in-app the next time they open Nexo --
            // quiet hours mute the buzz, never the record.
            if (IsWithinQuietHours(device, clock.UtcNow))
            {
                continue;
            }

            var body = device.ShowAmountsInPreview
                ? notification.Body
                : "Abre Fino para ver el detalle.";

            messages.Add(new PushMessage(device.ExpoPushToken, notification.Title, body, BuildPushData(notification)));
        }

        if (messages.Count == 0)
        {
            return;
        }

        try
        {
            var result = await push.SendAsync(messages, cancellationToken);
            var changed = false;

            if (result.SentCount > 0)
            {
                notification.MarkPushed(clock.UtcNow);
                changed = true;
            }

            // Entregable 18: a token Expo reports as DeviceNotRegistered will
            // never succeed again -- keeping the row would mean paying for a
            // doomed push on every future notification for the life of the user.
            if (result.InvalidTokens.Count > 0)
            {
                var stale = devices.Where(d => result.InvalidTokens.Contains(d.ExpoPushToken)).Select(d => d.Id).ToHashSet();
                if (stale.Count > 0)
                {
                    var tracked = await db.Devices.Where(d => stale.Contains(d.Id)).ToListAsync(cancellationToken);
                    db.Devices.RemoveRange(tracked);
                    changed = true;
                }
            }

            if (changed)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A push failure must never fail the operation that produced the movement.
            logger.LogWarning(ex, "Push delivery failed for notification {NotificationId}.", notification.Id);
        }
    }

    /// <summary>
    /// Entregable 18: `notificationId` always rides along so the app can mark it
    /// read on tap; when the notification also carries a small payload (today
    /// only `{"transactionId": "..."}`, set by EmailIngestionPipeline) its keys
    /// come along too, so the app can deep-link straight to that movement
    /// instead of just opening the notification list. A payload that fails to
    /// parse is dropped silently -- a broken deep link is not worth losing the
    /// push over.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildPushData(Notification notification)
    {
        var data = new Dictionary<string, string> { ["notificationId"] = notification.Id.ToString() };

        if (NotificationPayload.Parse(notification.Payload) is { } parsed)
        {
            foreach (var (key, value) in parsed)
            {
                data[key] = value;
            }
        }

        return data;
    }

    /// <summary>
    /// Entregable 17: every case is listed explicitly and the compiler warns
    /// (CS8509) if a future <see cref="NotificationType"/> value is added without
    /// updating this map -- a silent "always push" default (the old behaviour)
    /// is exactly how SecurityAlert went ungated for so long.
    /// </summary>
    private static bool ShouldNotify(Device device, NotificationType type) => type switch
    {
        NotificationType.ExpenseDetected => device.NotifyOnMovements,
        NotificationType.ImportCompleted => device.NotifyOnMovements,
        NotificationType.IncomeDetected => device.NotifyOnIncome,
        NotificationType.InsightReady => device.NotifyOnInsights,
        NotificationType.SecurityAlert => device.NotifyOnSecurity,
        NotificationType.WeeklySummary => device.NotifyOnReminders,
        NotificationType.AccountNeedsUpdate => device.NotifyOnReminders,
        NotificationType.PulseReady => device.NotifyOnPulses,
        NotificationType.CardPaymentDue => device.NotifyOnReminders,
        NotificationType.StatementAvailable => device.NotifyOnReminders,
        NotificationType.BudgetThreshold => device.NotifyOnReminders,
    };

    /// <summary>
    /// Ecuador has one timezone and no DST, and every Nexo user is there today
    /// (see <c>StatementDateInterpreter.Ecuador()</c>, the same fixed-offset
    /// fallback this mirrors) -- so "no molestar" hours are interpreted as
    /// Ecuador wall-clock time rather than adding a per-device timezone nobody
    /// has asked for yet. Both hours null (the default) means quiet hours are
    /// off; <see cref="Domain.Notifications.Device.UpdatePreferences"/> is what
    /// guarantees they are never set one without the other.
    /// </summary>
    private static readonly TimeZoneInfo EcuadorTimeZone = ResolveEcuadorTimeZone();

    private static bool IsWithinQuietHours(Device device, DateTimeOffset now)
    {
        if (device.QuietHoursStartHour is not { } start || device.QuietHoursEndHour is not { } end)
        {
            return false;
        }

        var localHour = TimeZoneInfo.ConvertTime(now, EcuadorTimeZone).Hour;

        // A window like 22-7 crosses midnight; 9-17 does not. Equal bounds is
        // rejected by UpdatePreferences, so this is never an always-on window.
        return start <= end
            ? localHour >= start && localHour < end
            : localHour >= start || localHour < end;
    }

    private static TimeZoneInfo ResolveEcuadorTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Guayaquil");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("nexo-ec", TimeSpan.FromHours(-5), "Ecuador", "Ecuador");
        }
    }
}

/// <summary>Deep-link payload helpers shared by push data and the history list.</summary>
public static class NotificationPayload
{
    public static IReadOnlyDictionary<string, string>? Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            // Malformed payload: keep the notification, drop the deep link.
            return JsonSerializer.Deserialize<Dictionary<string, string>>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Build(params (string Key, string Value)[] entries) =>
        JsonSerializer.Serialize(entries.ToDictionary(e => e.Key, e => e.Value));
}
