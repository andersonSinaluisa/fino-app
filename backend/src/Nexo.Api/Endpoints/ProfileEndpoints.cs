using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Nexo.Application.Abstractions;
using Nexo.Application.EmailIngestion;
using Nexo.Application.Notifications;
using Nexo.Application.Privacy;
using Nexo.Application.Reminders;

namespace Nexo.Api.Endpoints;

public static class ProfileEndpoints
{
    private const string ExportLinkPurpose = "Nexo.Privacy.ExportLink.v1";
    private static readonly TimeSpan ExportLinkLifetime = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var notifications = app.MapGroup("/api/v1/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        notifications.MapGet("", async (
            INotificationService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(currentUser.RequireUserId(), cancellationToken)));

        notifications.MapPost("/{id:guid}/read", async (
            Guid id,
            INotificationService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            await service.MarkReadAsync(currentUser.RequireUserId(), id, cancellationToken);
            return Results.NoContent();
        });

        notifications.MapPost("/test", async (
            INotificationDispatcher dispatcher,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.SendTestAsync(currentUser.RequireUserId(), cancellationToken)))
        .RequireRateLimiting(RateLimitPolicies.Authentication)
        .WithSummary("Envía una notificación de prueba a mis dispositivos.");

        // Recordatorios: manual run for testing, only when Nexo__Reminders__ManualRunEnabled=true
        // (404 otherwise). Only ever touches the caller's own reminders. ?force=true skips
        // the hour, the daily limit, "opened today" and once-only, without blocking the
        // real reminder later.
        notifications.MapPost("/reminders/run", async (
            bool? force,
            IReminderService reminders,
            IOptions<ReminderOptions> options,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            options.Value.ManualRunEnabled
                ? Results.Ok(await reminders.RunWithReportAsync(currentUser.RequireUserId(), force ?? false, cancellationToken))
                : Results.NotFound())
        .RequireRateLimiting(RateLimitPolicies.Authentication)
        .WithSummary("Evalúa mis recordatorios ahora (solo para pruebas, requiere flag).");

        notifications.MapPost("/devices", async (
            RegisterDeviceRequest request,
            INotificationService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.RegisterDeviceAsync(currentUser.RequireUserId(), request, cancellationToken)));

        notifications.MapPut("/devices/{token}/preferences", async (
            string token,
            NotificationPreferencesDto preferences,
            INotificationService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdatePreferencesAsync(
                currentUser.RequireUserId(),
                token,
                preferences,
                cancellationToken)));

        notifications.MapDelete("/devices/{token}", async (
            string token,
            INotificationService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            await service.UnregisterDeviceAsync(currentUser.RequireUserId(), token, cancellationToken);
            return Results.NoContent();
        })
        .WithSummary("Deja de enviar push a este dispositivo (por ejemplo, al cerrar sesión).");

        var email = app.MapGroup("/api/v1/email-connections")
            .WithTags("EmailConnections")
            .RequireAuthorization();

        email.MapGet("", async (
            IEmailConnectionService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(currentUser.RequireUserId(), cancellationToken)));

        email.MapPost("", async (
            StartEmailConnectionRequest request,
            IEmailConnectionService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.StartAsync(currentUser.RequireUserId(), request, cancellationToken)))
            .WithSummary("Inicia la autorización de un buzón. Falla explícitamente si el entorno no tiene credenciales.");

        email.MapDelete("/{id:guid}", async (
            Guid id,
            IEmailConnectionService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            await service.RevokeAsync(currentUser.RequireUserId(), id, cancellationToken);
            return Results.NoContent();
        })
        .WithSummary("Desconecta el correo y borra el secreto asociado.");

        var privacy = app.MapGroup("/api/v1/privacy")
            .WithTags("Privacy")
            .RequireAuthorization();

        privacy.MapGet("/export", async (
            IPrivacyService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var json = await service.ExportAsync(currentUser.RequireUserId(), cancellationToken);
            return Results.File(
                Encoding.UTF8.GetBytes(json),
                "application/json",
                $"nexo-export-{DateTime.UtcNow:yyyyMMdd}.json");
        })
        .WithSummary("Exportar mis datos.");

        // LOPDP arts. 13 y 17 (acceso y portabilidad): la app no puede abrir
        // /export en el navegador con su token, así que pide un enlace de un
        // solo propósito que vence en 5 minutos y lo abre ahí.
        privacy.MapPost("/export-link", (
            ICurrentUser currentUser,
            IDataProtectionProvider protection) =>
        {
            // El vencimiento lo controla el reloj real del protector, no IClock.
            var expiresAt = DateTimeOffset.UtcNow.Add(ExportLinkLifetime);
            var token = protection.CreateProtector(ExportLinkPurpose).ToTimeLimitedDataProtector()
                .Protect(currentUser.RequireUserId().ToString(), ExportLinkLifetime);
            return Results.Ok(new
            {
                path = $"/api/v1/privacy/export/download?token={Uri.EscapeDataString(token)}",
                expiresAt,
            });
        })
        .WithSummary("Enlace temporal (5 min) para descargar mis datos desde el navegador.");

        app.MapGet("/api/v1/privacy/export/download", async (
            string token,
            HttpContext http,
            IDataProtectionProvider protection,
            IPrivacyService service,
            CancellationToken cancellationToken) =>
        {
            Guid userId;
            try
            {
                var value = protection.CreateProtector(ExportLinkPurpose).ToTimeLimitedDataProtector().Unprotect(token);
                if (!Guid.TryParse(value, out userId))
                {
                    return Results.BadRequest();
                }
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return Results.Content(
                    "<!doctype html><meta charset=utf-8><p style=\"font:16px sans-serif;padding:24px\">Este enlace venció o no es válido. Vuelve a pedir la exportación desde la app.</p>",
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // El filtro por usuario de la base lee el usuario de la petición:
            // aquí lo da el enlace firmado, no un token de sesión.
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "export-link"));
            var json = await service.ExportAsync(userId, cancellationToken);
            return Results.File(Encoding.UTF8.GetBytes(json), "application/json", $"fino-mis-datos-{DateTime.UtcNow:yyyyMMdd}.json");
        })
        .AllowAnonymous()
        .ExcludeFromDescription();

        privacy.MapPost("/transactions/delete", async (
            DeleteTransactionsRequest request,
            IPrivacyService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.DeleteTransactionsAsync(
                currentUser.RequireUserId(),
                request,
                cancellationToken)))
            .WithSummary("Eliminar mis movimientos.");

        privacy.MapDelete("/accounts/{id:guid}", async (
            Guid id,
            IPrivacyService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.DeleteFinancialAccountAsync(
                currentUser.RequireUserId(),
                id,
                cancellationToken)))
            .WithSummary("Eliminar una cuenta financiera y todo su historial.");

        privacy.MapPost("/delete-account", async (
            IPrivacyService service,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            await service.RequestAccountDeletionAsync(currentUser.RequireUserId(), cancellationToken);
            return Results.Accepted();
        })
        .WithSummary("Eliminar mi cuenta Nexo (con período de gracia).");

        return app;
    }
}
