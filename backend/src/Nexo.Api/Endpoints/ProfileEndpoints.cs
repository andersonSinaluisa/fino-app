using System.Text;
using Nexo.Application.Abstractions;
using Nexo.Application.EmailIngestion;
using Nexo.Application.Notifications;
using Nexo.Application.Privacy;

namespace Nexo.Api.Endpoints;

public static class ProfileEndpoints
{
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
