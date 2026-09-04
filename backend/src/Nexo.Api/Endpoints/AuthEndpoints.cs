using Nexo.Api.Setup;
using Nexo.Application.Abstractions;
using Nexo.Application.Auth;

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

        return app;
    }
}
