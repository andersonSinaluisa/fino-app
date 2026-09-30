using Nexo.Application.Abstractions;
using Nexo.Application.Budgets;

namespace Nexo.Api.Endpoints;

/// <summary>
/// Presupuestos y Comprometido. As everywhere else, the user always comes from
/// the token (<see cref="ICurrentUser"/>), never from the route or the body.
/// Dates are the user's LOCAL calendar dates (yyyy-MM-dd).
/// </summary>
public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var budgets = app.MapGroup("/api/v1/budgets")
            .WithTags("Budgets")
            .RequireAuthorization();

        budgets.MapGet("", async (
                DateOnly? date,
                IBudgetService service,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(currentUser.RequireUserId(), date, cancellationToken)))
            .WithSummary("Presupuestos que aplican en la fecha (hoy por defecto), con totales (presupuestado/gastado/restante/reservado) y el insight principal.");

        budgets.MapGet("/{id:guid}", async (
                Guid id,
                DateOnly? date,
                IBudgetService service,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(currentUser.RequireUserId(), id, date, cancellationToken)))
            .WithSummary("Detalle: progreso del período, insights e historial de los últimos períodos.");

        budgets.MapGet("/{id:guid}/movements", async (
                Guid id,
                DateOnly? date,
                IBudgetService service,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetMovementsAsync(currentUser.RequireUserId(), id, date, cancellationToken)))
            .WithSummary("Exactamente los movimientos que suman al gastado del período (gastos y reembolsos).");

        budgets.MapPost("", async (
                CreateBudgetRequest request,
                IBudgetService service,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateAsync(currentUser.RequireUserId(), request, cancellationToken)))
            .WithSummary("Crea un presupuesto. 409 si la categoría ya tiene otro activo en fechas que se solapan.");

        budgets.MapPost("/preview", async (
                BudgetPreviewRequest request,
                ICommittedMoneyService committed,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await committed.PreviewBudgetAsync(currentUser.RequireUserId(), request, cancellationToken)))
            .WithSummary("¿Cómo quedarían Comprometido y Disponible si guardo este presupuesto? Mismo motor que el cálculo real.");

        budgets.MapPut("/{id:guid}", async (
                Guid id,
                UpdateBudgetRequest request,
                IBudgetService service,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(currentUser.RequireUserId(), id, request, cancellationToken)))
            .WithSummary("Edita monto, categoría, nombre, reserva, prioridad, fin o pausa. El nuevo monto aplica desde el período actual; los anteriores conservan el suyo.");

        budgets.MapDelete("/{id:guid}", async (
                Guid id,
                IBudgetService service,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            {
                await service.DeleteAsync(currentUser.RequireUserId(), id, cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Elimina el presupuesto. Nunca toca los movimientos.");

        app.MapGroup("/api/v1/finance")
            .WithTags("Finance")
            .RequireAuthorization()
            .MapGet("/committed", async (
                ICommittedMoneyService committed,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await committed.GetAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithSummary("Tu dinero, Comprometido (con su desglose por origen) y Disponible. Única fuente de verdad de estas cifras.");

        return app;
    }
}
