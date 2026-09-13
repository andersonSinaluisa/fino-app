using Nexo.Domain.Notifications;
using Xunit;

namespace Nexo.Domain.Tests;

/// <summary>
/// Entregable 17 ("Notificaciones"): <see cref="Notification"/> is the in-app
/// record (always written, regardless of push); <see cref="Device"/> carries the
/// five independent preference toggles the spec asks for (Movimientos, Ingresos,
/// Insights, Seguridad, Recordatorios) plus the two device-level switches that
/// predate this entregable (PushEnabled, ShowAmountsInPreview). PULSO FASE 3
/// adds a sixth category (Pulso) and the "no molestar" quiet-hours window.
/// </summary>
public class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    [Fact]
    public void Creating_a_notification_sets_its_fields_and_leaves_it_unread_and_unpushed()
    {
        var notification = Notification.Create(
            User,
            NotificationType.ExpenseDetected,
            "Compra detectada",
            "Gastaste $12.00 en KFC.",
            Now,
            payload: "{\"transactionId\":\"abc\"}");

        Assert.Equal(User, notification.UserId);
        Assert.Equal(NotificationType.ExpenseDetected, notification.Type);
        Assert.Equal("Compra detectada", notification.Title);
        Assert.Null(notification.ReadAt);
        Assert.Null(notification.PushSentAt);
    }

    [Fact]
    public void Marking_a_notification_read_twice_keeps_the_first_timestamp()
    {
        var notification = Notification.Create(User, NotificationType.SecurityAlert, "Alerta", "Cuerpo", Now);

        notification.MarkRead(Now);
        notification.MarkRead(Now.AddHours(3));

        Assert.Equal(Now, notification.ReadAt);
    }

    [Fact]
    public void Marking_a_notification_pushed_records_the_instant()
    {
        var notification = Notification.Create(User, NotificationType.IncomeDetected, "Ingreso", "Cuerpo", Now);

        notification.MarkPushed(Now.AddSeconds(2));

        Assert.Equal(Now.AddSeconds(2), notification.PushSentAt);
    }

    [Fact]
    public void A_new_device_defaults_every_preference_to_enabled()
    {
        var device = Device.Register(User, "ExponentPushToken[abc]", DevicePlatform.Android, Now);

        Assert.True(device.PushEnabled);
        Assert.True(device.ShowAmountsInPreview);
        Assert.True(device.NotifyOnMovements);
        Assert.True(device.NotifyOnIncome);
        Assert.True(device.NotifyOnInsights);
        Assert.True(device.NotifyOnSecurity);
        Assert.True(device.NotifyOnReminders);
        // PULSO FASE 3: Pulso is the sixth category, on by default like the
        // other five; quiet hours are off by default (both null) since nobody
        // has chosen a window yet.
        Assert.True(device.NotifyOnPulses);
        Assert.Null(device.QuietHoursStartHour);
        Assert.Null(device.QuietHoursEndHour);
        Assert.Equal(Now, device.LastSeenAt);
    }

    [Fact]
    public void Updating_preferences_can_turn_off_categories_independently()
    {
        var device = Device.Register(User, "ExponentPushToken[abc]", DevicePlatform.Ios, Now);

        // Only Seguridad stays on -- proves the six categories are independent,
        // not one shared "notifications on/off" switch.
        device.UpdatePreferences(
            pushEnabled: true,
            showAmountsInPreview: true,
            notifyOnMovements: false,
            notifyOnIncome: false,
            notifyOnInsights: false,
            notifyOnSecurity: true,
            notifyOnReminders: false,
            notifyOnPulses: false,
            quietHoursStartHour: null,
            quietHoursEndHour: null,
            now: Now.AddMinutes(5));

        Assert.False(device.NotifyOnMovements);
        Assert.False(device.NotifyOnIncome);
        Assert.False(device.NotifyOnInsights);
        Assert.True(device.NotifyOnSecurity);
        Assert.False(device.NotifyOnReminders);
        Assert.False(device.NotifyOnPulses);
    }

    [Fact]
    public void Touching_a_device_updates_last_seen_without_resetting_preferences()
    {
        var device = Device.Register(User, "ExponentPushToken[abc]", DevicePlatform.Web, Now);
        device.UpdatePreferences(true, false, true, false, true, false, true, true, null, null, Now.AddMinutes(1));

        device.Touch("2.3.0", Now.AddDays(1));

        Assert.Equal(Now.AddDays(1), device.LastSeenAt);
        Assert.Equal("2.3.0", device.AppVersion);
        // Re-registering (upsert-by-token in NotificationService) must not silently
        // reset what the person already chose.
        Assert.False(device.ShowAmountsInPreview);
        Assert.False(device.NotifyOnIncome);
    }

    [Fact]
    public void Setting_a_quiet_hours_window_persists_start_and_end()
    {
        var device = Device.Register(User, "ExponentPushToken[abc]", DevicePlatform.Android, Now);

        device.UpdatePreferences(true, true, true, true, true, true, true, true, 22, 7, Now.AddMinutes(5));

        Assert.Equal(22, device.QuietHoursStartHour);
        Assert.Equal(7, device.QuietHoursEndHour);
    }

    [Theory]
    [InlineData(22, null)]
    [InlineData(null, 7)]
    public void A_quiet_hours_window_needs_both_a_start_and_an_end(int? start, int? end)
    {
        var device = Device.Register(User, "ExponentPushToken[abc]", DevicePlatform.Android, Now);

        Assert.Throws<Nexo.Domain.Common.DomainException>(() =>
            device.UpdatePreferences(true, true, true, true, true, true, true, true, start, end, Now.AddMinutes(5)));
    }

    [Theory]
    [InlineData(-1, 7)]
    [InlineData(22, 24)]
    public void A_quiet_hours_hour_outside_0_to_23_is_rejected(int? start, int? end)
    {
        var device = Device.Register(User, "ExponentPushToken[abc]", DevicePlatform.Android, Now);

        Assert.Throws<Nexo.Domain.Common.DomainException>(() =>
            device.UpdatePreferences(true, true, true, true, true, true, true, true, start, end, Now.AddMinutes(5)));
    }

    [Fact]
    public void A_quiet_hours_window_cannot_start_and_end_at_the_same_hour()
    {
        var device = Device.Register(User, "ExponentPushToken[abc]", DevicePlatform.Android, Now);

        Assert.Throws<Nexo.Domain.Common.DomainException>(() =>
            device.UpdatePreferences(true, true, true, true, true, true, true, true, 9, 9, Now.AddMinutes(5)));
    }
}
