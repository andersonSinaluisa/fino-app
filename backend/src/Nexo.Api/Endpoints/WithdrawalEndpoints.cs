using Nexo.Application.Abstractions;
using Nexo.Application.Withdrawals;

namespace Nexo.Api.Endpoints;

/// <summary>
/// §4, §9 y §10: la superficie HTTP de la conciliación de retiros.
///
/// Vive bajo /transfers a propósito y no bajo una ruta propia: un retiro conciliado
/// ES una transferencia interna (§1), y agrupar las rutas evita sugerir que hay dos
/// sistemas de conciliación cuando solo hay uno.
/// </summary>
public static class WithdrawalEndpoints
{
    public static IEndpointRouteBuilder MapWithdrawalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/transfers/withdrawals")
            .WithTags("Withdrawals")
            .RequireAuthorization();

        group.MapGet("/candidates", async (
                IWithdrawalService withdrawals,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await withdrawals.ListCandidatesAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithSummary("Movimientos bancarios que parecen retiros de efectivo.")
            .WithDescription(
                "Ordenados del más reciente al más antiguo. Excluye lo ya conciliado y lo que la " +
                "persona ya clasificó a mano: sobre eso Fino no vuelve a opinar.");

        group.MapPost("/{id:guid}/confirm", async (
                Guid id,
                ConfirmWithdrawalRequest request,
                IWithdrawalService withdrawals,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await withdrawals.ConfirmAsync(currentUser.RequireUserId(), id, request, cancellationToken)))
            .WithSummary("Sí: este dinero pasó a Efectivo.")
            .WithDescription(
                "Sin cashTransactionId, Fino CREA la entrada de efectivo que falta y la vincula. " +
                "Con él, usa el ingreso que la persona ya había registrado a mano (§12). En ambos " +
                "casos el par queda marcado como transferencia interna, así que deja de contar como " +
                "gasto en todas las estadísticas. Crea la cuenta Efectivo si aún no existe (§11).");

        group.MapPost("/{id:guid}/reject", async (
                Guid id,
                IWithdrawalService withdrawals,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            {
                await withdrawals.RejectAsync(currentUser.RequireUserId(), id, cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("No: fue un gasto.")
            .WithDescription(
                "Conserva la categoría que ya tuviera y deja de proponerlo. No cambia la " +
                "clasificación del movimiento, solo registra que la persona ya lo revisó.");

        group.MapGet("/scan/{importId:guid}", async (
                Guid importId,
                IWithdrawalService withdrawals,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await withdrawals.ScanImportAsync(currentUser.RequireUserId(), importId, cancellationToken)))
            .WithSummary("Cuántos posibles retiros dejó una importación.")
            .WithDescription("§8: para avisar al terminar de importar sin detener nada.");

        return app;
    }
}
