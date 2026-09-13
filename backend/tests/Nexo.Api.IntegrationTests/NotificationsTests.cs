using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Abstractions;
using Nexo.Domain.Common;
using Nexo.Domain.Notifications;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 17 ("Notificaciones"): the in-app list/read endpoints, device
/// registration and the five independent preference categories (Movimientos,
/// Ingresos, Insights, Seguridad, Recordatorios). Entregable 18 ("Push
/// end-to-end") adds: the deep-link data riding along on the push payload,
/// automatic cleanup of tokens Expo reports as unregistered, and unregistering
/// a device outright (used on logout). Nothing in the product today produces a
/// notification except <c>EmailIngestionPipeline</c> (which needs a configured
/// forwarding relay -- out of scope here, see docs/email-ingestion.md), so
/// these tests dispatch through the real <see cref="INotificationDispatcher"/>
/// directly -- the same service and the same ShouldNotify gate that pipeline
/// calls, just triggered without a real inbound email.
/// </summary>
public class NotificationsTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    [Fact]
    public async Task A_new_user_has_no_notifications()
    {
        var user = await factory.RegisterUserAsync();

        var notifications = await user.GetJsonAsync("/api/v1/notifications");

        Assert.Equal(0, notifications.GetArrayLength());
    }

    [Fact]
    public async Task Dispatching_a_notification_makes_it_show_up_unread_and_marking_it_read_flips_that()
    {
        var user = await factory.RegisterUserAsync();
        await DispatchAsync(factory, user.Id, NotificationType.ExpenseDetected, "Compra detectada", "Gastaste $12.00 en KFC.");

        var listed = await user.GetJsonAsync("/api/v1/notifications");
        Assert.Equal(1, listed.GetArrayLength());
        var item = listed[0];
        Assert.Equal("ExpenseDetected", item.GetProperty("type").GetString());
        Assert.False(item.GetProperty("isRead").GetBoolean());

        var id = item.GetProperty("id").GetGuid();
        var readResponse = await user.Client.PostAsync($"/api/v1/notifications/{id}/read", content: null);
        await readResponse.EnsureOkAsync();
        Assert.Equal(HttpStatusCode.NoContent, readResponse.StatusCode);

        var afterRead = await user.GetJsonAsync("/api/v1/notifications");
        Assert.True(afterRead[0].GetProperty("isRead").GetBoolean());

        // Idempotent: marking an already-read notification read again is not an error.
        var again = await user.Client.PostAsync($"/api/v1/notifications/{id}/read", content: null);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task Marking_someone_elses_notification_as_read_is_a_404_not_a_leak()
    {
        var owner = await factory.RegisterUserAsync();
        await DispatchAsync(factory, owner.Id, NotificationType.SecurityAlert, "Alerta", "Cuerpo");
        var ownerNotifications = await owner.GetJsonAsync("/api/v1/notifications");
        var notificationId = ownerNotifications[0].GetProperty("id").GetGuid();

        var intruder = await factory.RegisterUserAsync();
        var response = await intruder.Client.PostAsync($"/api/v1/notifications/{notificationId}/read", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Registering_a_device_defaults_every_preference_category_to_enabled()
    {
        var user = await factory.RegisterUserAsync();

        var response = await user.Client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = "ExponentPushToken[test-device-1]",
            platform = "Android",
            deviceName = "Pixel de prueba",
            appVersion = "1.0.0",
        });
        await response.EnsureOkAsync();

        var prefs = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(prefs.GetProperty("pushEnabled").GetBoolean());
        Assert.True(prefs.GetProperty("showAmountsInPreview").GetBoolean());
        Assert.True(prefs.GetProperty("notifyOnMovements").GetBoolean());
        Assert.True(prefs.GetProperty("notifyOnIncome").GetBoolean());
        Assert.True(prefs.GetProperty("notifyOnInsights").GetBoolean());
        Assert.True(prefs.GetProperty("notifyOnSecurity").GetBoolean());
        Assert.True(prefs.GetProperty("notifyOnReminders").GetBoolean());
        // PULSO FASE 3: Pulso defaults on like the other five; quiet hours off.
        Assert.True(prefs.GetProperty("notifyOnPulses").GetBoolean());
        Assert.Equal(JsonValueKind.Null, prefs.GetProperty("quietHoursStartHour").ValueKind);
        Assert.Equal(JsonValueKind.Null, prefs.GetProperty("quietHoursEndHour").ValueKind);
    }

    [Fact]
    public async Task Updating_preferences_persists_and_an_unknown_token_is_a_404()
    {
        var user = await factory.RegisterUserAsync();
        const string token = "ExponentPushToken[test-device-2]";

        await (await user.Client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = token,
            platform = "Ios",
            deviceName = (string?)null,
            appVersion = (string?)null,
        })).EnsureOkAsync();

        var updateResponse = await user.Client.PutAsJsonAsync(
            $"/api/v1/notifications/devices/{Uri.EscapeDataString(token)}/preferences",
            new
            {
                pushEnabled = true,
                showAmountsInPreview = false,
                notifyOnMovements = false,
                notifyOnIncome = true,
                notifyOnInsights = false,
                notifyOnSecurity = true,
                notifyOnReminders = false,
                notifyOnPulses = false,
                quietHoursStartHour = 22,
                quietHoursEndHour = 7,
            });
        await updateResponse.EnsureOkAsync();

        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(updated.GetProperty("showAmountsInPreview").GetBoolean());
        Assert.False(updated.GetProperty("notifyOnMovements").GetBoolean());
        Assert.True(updated.GetProperty("notifyOnIncome").GetBoolean());
        Assert.False(updated.GetProperty("notifyOnInsights").GetBoolean());
        Assert.True(updated.GetProperty("notifyOnSecurity").GetBoolean());
        Assert.False(updated.GetProperty("notifyOnReminders").GetBoolean());
        Assert.False(updated.GetProperty("notifyOnPulses").GetBoolean());
        Assert.Equal(22, updated.GetProperty("quietHoursStartHour").GetInt32());
        Assert.Equal(7, updated.GetProperty("quietHoursEndHour").GetInt32());

        var notFound = await user.Client.PutAsJsonAsync(
            "/api/v1/notifications/devices/ExponentPushToken%5Bnever-registered%5D/preferences",
            new
            {
                pushEnabled = true,
                showAmountsInPreview = true,
                notifyOnMovements = true,
                notifyOnIncome = true,
                notifyOnInsights = true,
                notifyOnSecurity = true,
                notifyOnReminders = true,
            });
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task An_invalid_quiet_hours_window_is_rejected_with_422()
    {
        var user = await factory.RegisterUserAsync();
        const string token = "ExponentPushToken[bad-quiet-hours]";

        await (await user.Client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = token,
            platform = "Android",
            deviceName = (string?)null,
            appVersion = (string?)null,
        })).EnsureOkAsync();

        // Only a start hour, no end -- Device.UpdatePreferences requires both or neither.
        var response = await user.Client.PutAsJsonAsync(
            $"/api/v1/notifications/devices/{Uri.EscapeDataString(token)}/preferences",
            new
            {
                pushEnabled = true,
                showAmountsInPreview = true,
                notifyOnMovements = true,
                notifyOnIncome = true,
                notifyOnInsights = true,
                notifyOnSecurity = true,
                notifyOnReminders = true,
                notifyOnPulses = true,
                quietHoursStartHour = 22,
                quietHoursEndHour = (int?)null,
            });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }

    [Fact]
    public async Task A_disabled_category_is_never_pushed_while_an_enabled_one_still_is()
    {
        var spy = new SpyPushSender();
        await using var gated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(spy);
            }));

        var (userId, client) = await RegisterAsync(gated);
        const string token = "ExponentPushToken[gating-device]";

        await (await client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = token,
            platform = "Android",
            deviceName = (string?)null,
            appVersion = (string?)null,
        })).EnsureOkAsync();

        // Seguridad off, Movimientos on -- proves the categories gate independently.
        await (await client.PutAsJsonAsync(
            $"/api/v1/notifications/devices/{Uri.EscapeDataString(token)}/preferences",
            new
            {
                pushEnabled = true,
                showAmountsInPreview = true,
                notifyOnMovements = true,
                notifyOnIncome = true,
                notifyOnInsights = true,
                notifyOnSecurity = false,
                notifyOnReminders = true,
            })).EnsureOkAsync();

        using (var scope = gated.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

            await dispatcher.DispatchAsync(
                Notification.Create(userId, NotificationType.SecurityAlert, "Alerta", "Correo sospechoso rechazado.", NexoApiFactory.Now),
                CancellationToken.None);

            await dispatcher.DispatchAsync(
                Notification.Create(userId, NotificationType.ExpenseDetected, "Compra detectada", "Gastaste $12.00 en KFC.", NexoApiFactory.Now),
                CancellationToken.None);
        }

        Assert.Single(spy.Sent);
        Assert.Equal("Compra detectada", spy.Sent[0].Title);
    }

    [Fact]
    public async Task Turning_off_preview_redacts_the_push_body_but_the_in_app_notification_keeps_the_full_text()
    {
        var spy = new SpyPushSender();
        await using var gated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(spy);
            }));

        var (userId, client) = await RegisterAsync(gated);
        const string token = "ExponentPushToken[preview-device]";

        await (await client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = token,
            platform = "Ios",
            deviceName = (string?)null,
            appVersion = (string?)null,
        })).EnsureOkAsync();

        await (await client.PutAsJsonAsync(
            $"/api/v1/notifications/devices/{Uri.EscapeDataString(token)}/preferences",
            new
            {
                pushEnabled = true,
                showAmountsInPreview = false,
                notifyOnMovements = true,
                notifyOnIncome = true,
                notifyOnInsights = true,
                notifyOnSecurity = true,
                notifyOnReminders = true,
            })).EnsureOkAsync();

        const string fullBody = "Recibiste $500.00 en Banco Pichincha.";

        using (var scope = gated.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await dispatcher.DispatchAsync(
                Notification.Create(userId, NotificationType.IncomeDetected, "Ingreso detectado", fullBody, NexoApiFactory.Now),
                CancellationToken.None);
        }

        Assert.Single(spy.Sent);
        Assert.Equal("Abre Nexo para ver el detalle.", spy.Sent[0].Body);

        var inApp = await client.GetAsync("/api/v1/notifications");
        var listed = await inApp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(fullBody, listed[0].GetProperty("body").GetString());
    }

    [Fact]
    public async Task Turning_off_the_master_push_switch_silences_every_category()
    {
        var spy = new SpyPushSender();
        await using var gated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(spy);
            }));

        var (userId, client) = await RegisterAsync(gated);
        const string token = "ExponentPushToken[muted-device]";

        await (await client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = token,
            platform = "Android",
            deviceName = (string?)null,
            appVersion = (string?)null,
        })).EnsureOkAsync();

        // pushEnabled off, every category still on -- the master switch wins.
        await (await client.PutAsJsonAsync(
            $"/api/v1/notifications/devices/{Uri.EscapeDataString(token)}/preferences",
            new
            {
                pushEnabled = false,
                showAmountsInPreview = true,
                notifyOnMovements = true,
                notifyOnIncome = true,
                notifyOnInsights = true,
                notifyOnSecurity = true,
                notifyOnReminders = true,
            })).EnsureOkAsync();

        using (var scope = gated.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await dispatcher.DispatchAsync(
                Notification.Create(userId, NotificationType.ExpenseDetected, "Compra detectada", "Gastaste $12.00 en KFC.", NexoApiFactory.Now),
                CancellationToken.None);
        }

        Assert.Empty(spy.Sent);

        // The in-app record is unaffected -- pushEnabled only silences the phone,
        // never the history inside the app.
        var inApp = await client.GetAsync("/api/v1/notifications");
        var listed = await inApp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, listed.GetArrayLength());
    }

    /// <summary>
    /// PULSO FASE 3 ("no molestar"). NexoApiFactory's fixed clock is
    /// 2026-03-10 17:00 UTC -- noon in Ecuador (UTC-5, no DST) -- so a window
    /// that contains hour 12 must mute the push, while the in-app record still
    /// gets written exactly like the pushEnabled-off case above.
    /// </summary>
    [Fact]
    public async Task Quiet_hours_mute_the_push_but_not_the_in_app_notification()
    {
        var spy = new SpyPushSender();
        await using var gated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(spy);
            }));

        var (userId, client) = await RegisterAsync(gated);
        const string token = "ExponentPushToken[quiet-hours-device]";

        await (await client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = token,
            platform = "Android",
            deviceName = (string?)null,
            appVersion = (string?)null,
        })).EnsureOkAsync();

        // 09:00-17:00 local -- covers noon, the fixed clock's local hour.
        await (await client.PutAsJsonAsync(
            $"/api/v1/notifications/devices/{Uri.EscapeDataString(token)}/preferences",
            new
            {
                pushEnabled = true,
                showAmountsInPreview = true,
                notifyOnMovements = true,
                notifyOnIncome = true,
                notifyOnInsights = true,
                notifyOnSecurity = true,
                notifyOnReminders = true,
                notifyOnPulses = true,
                quietHoursStartHour = 9,
                quietHoursEndHour = 17,
            })).EnsureOkAsync();

        using (var scope = gated.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await dispatcher.DispatchAsync(
                Notification.Create(userId, NotificationType.ExpenseDetected, "Compra detectada", "Gastaste $12.00 en KFC.", NexoApiFactory.Now),
                CancellationToken.None);
        }

        Assert.Empty(spy.Sent);

        var inApp = await client.GetAsync("/api/v1/notifications");
        var listed = await inApp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, listed.GetArrayLength());
    }

    /// <summary>An overnight window (22-7) that does not contain the fixed clock's local noon must not affect the push -- also exercises the midnight-crossing branch.</summary>
    [Fact]
    public async Task A_quiet_hours_window_that_does_not_contain_the_current_hour_does_not_mute_the_push()
    {
        var spy = new SpyPushSender();
        await using var gated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(spy);
            }));

        var (userId, client) = await RegisterAsync(gated);
        const string token = "ExponentPushToken[quiet-hours-overnight-device]";

        await (await client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = token,
            platform = "Android",
            deviceName = (string?)null,
            appVersion = (string?)null,
        })).EnsureOkAsync();

        await (await client.PutAsJsonAsync(
            $"/api/v1/notifications/devices/{Uri.EscapeDataString(token)}/preferences",
            new
            {
                pushEnabled = true,
                showAmountsInPreview = true,
                notifyOnMovements = true,
                notifyOnIncome = true,
                notifyOnInsights = true,
                notifyOnSecurity = true,
                notifyOnReminders = true,
                notifyOnPulses = true,
                quietHoursStartHour = 22,
                quietHoursEndHour = 7,
            })).EnsureOkAsync();

        using (var scope = gated.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await dispatcher.DispatchAsync(
                Notification.Create(userId, NotificationType.ExpenseDetected, "Compra detectada", "Gastaste $12.00 en KFC.", NexoApiFactory.Now),
                CancellationToken.None);
        }

        Assert.Single(spy.Sent);
    }

    [Fact]
    public async Task The_push_payload_carries_the_notification_id_and_any_deep_link_payload()
    {
        var spy = new SpyPushSender();
        await using var gated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(spy);
            }));

        var (userId, client) = await RegisterAsync(gated);
        const string token = "ExponentPushToken[deeplink-device]";

        await (await client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = token,
            platform = "Android",
            deviceName = (string?)null,
            appVersion = (string?)null,
        })).EnsureOkAsync();

        var transactionId = Guid.CreateVersion7();

        using (var scope = gated.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await dispatcher.DispatchAsync(
                Notification.Create(
                    userId,
                    NotificationType.ExpenseDetected,
                    "Compra detectada",
                    "Gastaste $12.00 en KFC.",
                    NexoApiFactory.Now,
                    payload: $"{{\"transactionId\":\"{transactionId}\"}}"),
                CancellationToken.None);
        }

        Assert.Single(spy.Sent);
        var data = spy.Sent[0].Data;
        Assert.NotNull(data);
        Assert.True(data!.ContainsKey("notificationId"));
        Assert.Equal(transactionId.ToString(), data["transactionId"]);
    }

    [Fact]
    public async Task A_token_Expo_reports_as_unregistered_is_retired_and_stops_receiving_push()
    {
        var spy = new SpyPushSender();
        await using var gated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(spy);
            }));

        var (userId, client) = await RegisterAsync(gated);
        const string token = "ExponentPushToken[dead-device]";

        await (await client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = token,
            platform = "Android",
            deviceName = (string?)null,
            appVersion = (string?)null,
        })).EnsureOkAsync();

        spy.InvalidTokensToReport.Add(token);

        using (var scope = gated.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

            // First attempt: Expo answers "DeviceNotRegistered" for this token --
            // the row must be retired even though the push was still attempted.
            await dispatcher.DispatchAsync(
                Notification.Create(userId, NotificationType.SecurityAlert, "Alerta uno", "Cuerpo uno.", NexoApiFactory.Now),
                CancellationToken.None);
            Assert.Single(spy.Sent);

            // Second notification: the device is gone, so this must not even be attempted.
            await dispatcher.DispatchAsync(
                Notification.Create(userId, NotificationType.SecurityAlert, "Alerta dos", "Cuerpo dos.", NexoApiFactory.Now),
                CancellationToken.None);
        }

        Assert.Single(spy.Sent);

        // Confirmed independently of the spy: the preferences endpoint for that
        // token now 404s, because NotificationDispatcher deleted the Device row.
        var stillThere = await client.PutAsJsonAsync(
            $"/api/v1/notifications/devices/{Uri.EscapeDataString(token)}/preferences",
            new
            {
                pushEnabled = true,
                showAmountsInPreview = true,
                notifyOnMovements = true,
                notifyOnIncome = true,
                notifyOnInsights = true,
                notifyOnSecurity = true,
                notifyOnReminders = true,
            });
        Assert.Equal(HttpStatusCode.NotFound, stillThere.StatusCode);
    }

    [Fact]
    public async Task Unregistering_a_device_stops_further_push_and_a_second_unregister_is_a_404()
    {
        var spy = new SpyPushSender();
        await using var gated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(spy);
            }));

        var (userId, client) = await RegisterAsync(gated);
        const string token = "ExponentPushToken[logout-device]";

        await (await client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = token,
            platform = "Ios",
            deviceName = (string?)null,
            appVersion = (string?)null,
        })).EnsureOkAsync();

        var unregister = await client.DeleteAsync($"/api/v1/notifications/devices/{Uri.EscapeDataString(token)}");
        Assert.Equal(HttpStatusCode.NoContent, unregister.StatusCode);

        using (var scope = gated.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await dispatcher.DispatchAsync(
                Notification.Create(userId, NotificationType.IncomeDetected, "Ingreso", "Cuerpo.", NexoApiFactory.Now),
                CancellationToken.None);
        }

        Assert.Empty(spy.Sent);

        var again = await client.DeleteAsync($"/api/v1/notifications/devices/{Uri.EscapeDataString(token)}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    /// <summary>
    /// Dispatches straight through the real <see cref="INotificationDispatcher"/>
    /// registered in <paramref name="factory"/> -- the exact path
    /// EmailIngestionPipeline uses, minus the email itself.
    /// </summary>
    private static async Task DispatchAsync(
        NexoApiFactory factory,
        Guid userId,
        NotificationType type,
        string title,
        string body)
    {
        using var scope = factory.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        await dispatcher.DispatchAsync(Notification.Create(userId, type, title, body, clock.UtcNow), CancellationToken.None);
    }

    private static async Task<(Guid Id, HttpClient Client)> RegisterAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"user-{Guid.CreateVersion7():N}@nexo.test",
            password = "NexoIntegration2026!",
            displayName = "Usuario de prueba",
        });
        await response.EnsureOkAsync();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = payload.GetProperty("accessToken").GetString()!;
        var userId = payload.GetProperty("user").GetProperty("id").GetGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return (userId, client);
    }

    /// <summary>
    /// Records what would have gone out over push, without a network call. Any
    /// token listed in <see cref="InvalidTokensToReport"/> is reported back as
    /// "not sent, and retire this token" -- simulating Expo answering
    /// DeviceNotRegistered for it -- so tests can exercise
    /// NotificationDispatcher's cleanup without a real Expo response.
    /// </summary>
    private sealed class SpyPushSender : IPushSender
    {
        public List<PushMessage> Sent { get; } = [];

        public List<string> InvalidTokensToReport { get; } = [];

        public Task<PushSendResult> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
        {
            Sent.AddRange(messages);

            var invalid = messages.Select(m => m.ExpoPushToken).Where(InvalidTokensToReport.Contains).ToList();
            var sentCount = messages.Count - invalid.Count;

            return Task.FromResult(new PushSendResult(sentCount, invalid));
        }
    }
}
