using Nexo.Application.Abstractions;
using Nexo.Application.Transfers;

namespace Nexo.Api.Endpoints;

/// <summary>Entregable 13: "¿Esto fue una transferencia entre tus cuentas?".</summary>
public static class InternalTransferEndpoints
{
    public static IEndpointRouteBuilder MapInternalTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/transfers")
            .WithTags("InternalTransfers")
            .RequireAuthorization();

        group.MapGet("/candidates", async (
                IInternalTransferService transfers,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await transfers.ListCandidatesAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithSummary("Pares de movimientos que probablemente son una transferencia entre las cuentas del usuario.");

        group.MapPost("/confirm", async (
                ConfirmInternalTransferRequest request,
                IInternalTransferService transfers,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            {
                await transfers.ConfirmAsync(currentUser.RequireUserId(), request, cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Confirma que dos movimientos son una transferencia interna: deja de contar como gasto o ingreso.");

        group.MapPost("/{transactionId:guid}/clear", async (
                Guid transactionId,
                IInternalTransferService transfers,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            {
                await transfers.ClearAsync(currentUser.RequireUserId(), transactionId, cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Deshace una transferencia confirmada, en ambos lados.");

        return app;
    }
}
