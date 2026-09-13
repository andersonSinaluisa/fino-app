using Nexo.Application.Abstractions;
using Nexo.Application.Pulses;

namespace Nexo.Api.Endpoints;

/// <summary>
/// PULSO FASE 1: read-only access to whatever PulseEvaluationWorker has already
/// persisted, plus an on-demand evaluate for testing and for a manual
/// "actualizar" affordance -- the same pair TransactionEndpoints exposes for
/// insights (GET /insights, POST /insights/refresh). PULSO FASE 4 adds the one
/// write this group has: POST /{id}/feedback, the 👍/👎 on the detail screen.
/// </summary>
public static class PulseEndpoints
{
    public static IEndpointRouteBuilder MapPulseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pulses")
            .WithTags("Pulses")
            .RequireAuthorization();

        group.MapGet("", async (
                IPulseService pulses,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await pulses.ListAsync(currentUser.RequireUserId(), cancellationToken)));

        group.MapGet("/{id:guid}", async (
                Guid id,
                IPulseService pulses,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await pulses.GetAsync(currentUser.RequireUserId(), id, cancellationToken)));

        group.MapPost("/evaluate", async (
                IPulseService pulses,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await pulses.EvaluateAsync(currentUser.RequireUserId(), cancellationToken)));

        // PULSO FASE 4: 👍/👎 from the detail screen.
        group.MapPost("/{id:guid}/feedback", async (
                Guid id,
                PulseFeedbackRequest request,
                IPulseService pulses,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await pulses.SubmitFeedbackAsync(currentUser.RequireUserId(), id, request.Helpful, cancellationToken)));

        return app;
    }
}
