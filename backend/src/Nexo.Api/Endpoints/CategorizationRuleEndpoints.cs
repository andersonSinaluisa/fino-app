using Nexo.Application.Abstractions;
using Nexo.Application.Categorization;

namespace Nexo.Api.Endpoints;

/// <summary>
/// "Categorización personal": management of a user's own rules (the "Reglas de
/// categorización" screen) plus the impact-preview used before saving a new one
/// from a movement. Every handler resolves the user from the auth token via
/// <see cref="ICurrentUser"/> -- never from the request body/route -- so a UserId
/// cannot be spoofed by the client (point 14, "no aceptar UserId del cliente").
/// </summary>
public static class CategorizationRuleEndpoints
{
    public static IEndpointRouteBuilder MapCategorizationRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/categorization-rules")
            .WithTags("CategorizationRules")
            .RequireAuthorization();

        group.MapGet("", async (
                ICategorizationRuleService rules,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await rules.ListAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithSummary("Las reglas personales del usuario autenticado. Nunca las de otro usuario.");

        group.MapPost("", async (
                CreateCategorizationRuleRequest request,
                ICategorizationRuleService rules,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await rules.CreateAsync(currentUser.RequireUserId(), request, cancellationToken)))
            .WithSummary("Crea una regla directamente. 409 (rule_conflict) si el patrón ya pertenece a otra categoría.");

        group.MapPost("/preview", async (
                RulePreviewRequest request,
                ICategorizationRuleService rules,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await rules.PreviewAsync(currentUser.RequireUserId(), request, cancellationToken)))
            .WithSummary("Si creo esta regla a partir de este movimiento, ¿qué categoría y cuántos movimientos coincidirían?");

        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateCategorizationRuleRequest request,
                ICategorizationRuleService rules,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await rules.UpdateAsync(currentUser.RequireUserId(), id, request, cancellationToken)))
            .WithSummary("Cambia categoría y/o activa-desactiva. Recategoriza movimientos anteriores solo si se pide explícitamente.");

        group.MapDelete("/{id:guid}", async (
                Guid id,
                ICategorizationRuleService rules,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            {
                await rules.DeleteAsync(currentUser.RequireUserId(), id, cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Deja de aplicarse a movimientos futuros. Nunca modifica el historial ya categorizado.");

        return app;
    }
}
