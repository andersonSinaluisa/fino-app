using Nexo.Application.Abstractions;
using Nexo.Application.Onboarding;

namespace Nexo.Api.Endpoints;

/// <summary>
/// Onboarding funcional (rediseño post-login): read/write access to the five
/// milestones on <see cref="Nexo.Application.Auth.OnboardingStatusDto"/>. Register
/// and login already return the current snapshot inline (AuthResult.User.Onboarding)
/// so the app never needs an extra round trip right after signing in -- GET here
/// exists for whenever the cached session value might be stale (e.g. after the
/// access token was silently refreshed) rather than for the common case.
/// </summary>
public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/onboarding")
            .WithTags("Onboarding")
            .RequireAuthorization();

        group.MapGet("", async (
                IOnboardingService onboarding,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await onboarding.GetAsync(currentUser.RequireUserId(), cancellationToken)));

        group.MapPost("/start", async (
                IOnboardingService onboarding,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await onboarding.StartAsync(currentUser.RequireUserId(), cancellationToken)));

        group.MapPost("/tutorial-completed", async (
                IOnboardingService onboarding,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await onboarding.CompleteTutorialAsync(currentUser.RequireUserId(), cancellationToken)));

        group.MapPost("/skip", async (
                IOnboardingService onboarding,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await onboarding.SkipAsync(currentUser.RequireUserId(), cancellationToken)));

        return app;
    }
}
