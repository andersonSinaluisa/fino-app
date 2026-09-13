using Nexo.Application.Abstractions;
using Nexo.Application.QuickEntry;

namespace Nexo.Api.Endpoints;

/// <summary>
/// Registro rápido de efectivo (§28): la superficie HTTP del caso de uso
/// <c>CreateQuickTransaction</c>. Es deliberadamente delgada -- no hay lógica de
/// negocio aquí, solo el mapeo de rutas -- porque el mismo caso de uso lo tienen
/// que poder invocar el bottom sheet, el formulario completo, un frecuente de un
/// toque, la voz, el widget, un App Shortcut de Android y un App Intent de iOS.
/// Todos hablan con estos endpoints; ninguno reimplementa la creación.
/// </summary>
public static class QuickEntryEndpoints
{
    public static IEndpointRouteBuilder MapQuickEntryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/quick-entry")
            .WithTags("QuickEntry")
            .RequireAuthorization();

        group.MapGet("/bootstrap", async (
                IQuickEntrySuggestionService suggestions,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await suggestions.GetBootstrapAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithSummary("Cuenta de efectivo, saldo y sugerencias, en una sola llamada.")
            .WithDescription(
                "§40: el sheet de registro rápido debe abrirse prácticamente al instante, " +
                "así que todo lo que necesita para pintarse viaja junto y no en tres peticiones.");

        group.MapPost("/transactions", async (
                CreateQuickTransactionRequest request,
                IQuickTransactionService quickEntry,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            {
                var created = await quickEntry.CreateAsync(
                    currentUser.RequireUserId(),
                    request,
                    cancellationToken);

                return Results.Created($"/api/v1/transactions/{created.Id}", created);
            })
            .WithSummary("Registra un movimiento escrito a mano.")
            .WithDescription(
                "Solo el monto es obligatorio. Sin cuenta se usa Efectivo (creándola la primera vez), " +
                "sin fecha se usa ahora, sin tipo se asume gasto y sin categoría decide el motor de " +
                "reglas. Mandar el mismo clientRequestId dos veces devuelve el movimiento ya creado " +
                "en lugar de crear otro (§36).");

        group.MapPut("/transactions/{id:guid}", async (
                Guid id,
                UpdateQuickTransactionRequest request,
                IQuickTransactionService quickEntry,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await quickEntry.UpdateAsync(currentUser.RequireUserId(), id, request, cancellationToken)))
            .WithSummary("Corrige un movimiento propio sin perder su historial.")
            .WithDescription(
                "409 si el movimiento vino de un banco: ese dato lo reportó la institución y Fino " +
                "no lo reescribe.");

        group.MapDelete("/transactions/{id:guid}", async (
                Guid id,
                IQuickTransactionService quickEntry,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            {
                await quickEntry.DeleteAsync(currentUser.RequireUserId(), id, cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Deshacer: elimina un movimiento recién registrado a mano.")
            .WithDescription(
                "§6: es lo que permite guardar sin pedir confirmación. Solo aplica a movimientos " +
                "manuales; los de un banco se ignoran, no se borran.");

        group.MapPost("/cash-balance", async (
                SetCashBalanceRequest request,
                IQuickTransactionService quickEntry,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await quickEntry.SetCashBalanceAsync(currentUser.RequireUserId(), request, cancellationToken)))
            .WithSummary("Declara o corrige cuánto efectivo tienes.")
            .WithDescription(
                "mode=Anchor ancla el saldo (§24, configuración inicial). Por defecto, mode=Adjustment " +
                "registra la diferencia como un movimiento de ajuste visible en vez de cambiar el " +
                "saldo en silencio (§25).");

        return app;
    }
}
