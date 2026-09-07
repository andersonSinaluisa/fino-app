using Nexo.Api.Setup;
using Nexo.Application.Abstractions;
using Nexo.Application.Audit;
using Nexo.Application.Auth;
using Nexo.Application.Common;

namespace Nexo.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Auth")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapPost("/register", async (
            RegisterRequest request,
            IAuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await auth.RegisterAsync(request, http.ToRequestContext(), cancellationToken);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithSummary("Crea una cuenta Nexo.");

        group.MapPost("/login", async (
            LoginRequest request,
            IAuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await auth.LoginAsync(request, http.ToRequestContext(), cancellationToken);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithSummary("Inicia sesión con correo y contraseña.");

        group.MapPost("/refresh", async (
            RefreshRequest request,
            IAuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await auth.RefreshAsync(request, http.ToRequestContext(), cancellationToken);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithSummary("Rota el refresh token y emite un nuevo access token.");

        group.MapPost("/logout", async (
            RefreshRequest request,
            IAuthService auth,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            await auth.LogoutAsync(request.RefreshToken, http.ToRequestContext(), cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithSummary("Revoca la sesión actual.");

        group.MapPost("/logout-all", async (
            IAuthService auth,
            ICurrentUser currentUser,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            await auth.LogoutAllAsync(currentUser.RequireUserId(), http.ToRequestContext(), cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithSummary("Cierra la sesión en todos los dispositivos.");

        group.MapGet("/sessions", async (
            IAuthService auth,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await auth.ListSessionsAsync(currentUser.RequireUserId(), currentUser.SessionId, cancellationToken)))
        .RequireAuthorization()
        .WithSummary("Lista las sesiones activas (dispositivos con sesión abierta).");

        group.MapDelete("/sessions/{id:guid}", async (
            Guid id,
            IAuthService auth,
            ICurrentUser currentUser,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            await auth.RevokeSessionAsync(currentUser.RequireUserId(), id, currentUser.SessionId, http.ToRequestContext(), cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithSummary("Cierra la sesión de otro dispositivo.");

        group.MapGet("/activity", async (
            int? page,
            int? pageSize,
            IAuditActivityService activity,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await activity.ListAsync(
                currentUser.RequireUserId(),
                new PageRequest { Page = page ?? 1, PageSize = pageSize ?? 30 },
                cancellationToken)))
        .RequireAuthorization()
        .WithSummary("Historial de actividad de la cuenta (inicios de sesión, cambios, exportaciones...), más reciente primero.");

        return app;
    }
}
