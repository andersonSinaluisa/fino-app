using Nexo.Application.Abstractions;
using Nexo.Application.CreditCards;

namespace Nexo.Api.Endpoints;

/// <summary>
/// Tarjetas de crédito. {id} is the card's ACCOUNT id -- the same id its movements
/// use, so the card's movements are simply GET /transactions?accountId={id} (no
/// duplicate endpoint). The user always comes from the token, never from the
/// route or the body. Dates are the user's local calendar dates (yyyy-MM-dd).
/// </summary>
public static class CreditCardEndpoints
{
    public static IEndpointRouteBuilder MapCreditCardEndpoints(this IEndpointRouteBuilder app)
    {
        var cards = app.MapGroup("/api/v1/credit-cards")
            .WithTags("CreditCards")
            .RequireAuthorization();

        cards.MapGet("", async (bool? includeArchived, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(user.RequireUserId(), includeArchived ?? false, ct)))
            .WithSummary("Tarjetas con deuda actual, próximo pago, cupo disponible y lo que aportan a Comprometido.");

        cards.MapGet("/{id:guid}", async (Guid id, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.GetAsync(user.RequireUserId(), id, ct)))
            .WithSummary("Resumen de la tarjeta: ciclo actual, último estado, este mes, cuotas activas y últimos movimientos.");

        cards.MapPost("", async (CreateCreditCardRequest request, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(user.RequireUserId(), request, ct);
                return Results.Created($"/api/v1/credit-cards/{created.Card.Id}", created);
            })
            .WithSummary("Crea una tarjeta. Nunca recibe el número completo, el CVV ni el PIN: solo los últimos 4 dígitos.");

        cards.MapPut("/{id:guid}", async (Guid id, UpdateCreditCardRequest request, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.UpdateAsync(user.RequireUserId(), id, request, ct)))
            .WithSummary("Edita cupo, corte, pago y reserva automática (o completa una tarjeta creada antes de este módulo).");

        cards.MapPost("/{id:guid}/debt", async (Guid id, SetCreditCardDebtRequest request, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.SetDebtAsync(user.RequireUserId(), id, request, ct)))
            .WithSummary("Registra la deuda que muestra el banco hoy; desde ahí la deuda vuelve a ser verificada.");

        cards.MapDelete("/{id:guid}", async (Guid id, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
            {
                await service.ArchiveAsync(user.RequireUserId(), id, ct);
                return Results.NoContent();
            })
            .WithSummary("Archiva la tarjeta. Movimientos, estados, cuotas e historial se conservan.");

        cards.MapPost("/{id:guid}/restore", async (Guid id, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.RestoreAsync(user.RequireUserId(), id, ct)))
            .WithSummary("Vuelve a activar una tarjeta archivada.");

        cards.MapGet("/{id:guid}/statements", async (Guid id, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.ListStatementsAsync(user.RequireUserId(), id, ct)))
            .WithSummary("Ciclo abierto y estados cerrados: total, mínimo, pagado, pendiente y estado (derivados de los movimientos).");

        cards.MapPut("/{id:guid}/statements", async (Guid id, DeclareStatementRequest request, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.DeclareStatementAsync(user.RequireUserId(), id, request, ct)))
            .WithSummary("Registra las cifras oficiales de un estado (total a pagar, pago mínimo, fecha máxima).");

        cards.MapDelete("/{id:guid}/statements/{closingDate}", async (Guid id, DateOnly closingDate, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.RemoveDeclaredStatementAsync(user.RequireUserId(), id, closingDate, ct)))
            .WithSummary("Quita las cifras oficiales; el estado vuelve a calcularse desde los movimientos.");

        cards.MapGet("/{id:guid}/installments", async (Guid id, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.ListInstallmentPlansAsync(user.RequireUserId(), id, ct)))
            .WithSummary("Compras diferidas: cuota actual, próxima cuota y saldo pendiente de cada una.");

        cards.MapPost("/{id:guid}/installments", async (Guid id, CreateInstallmentPlanRequest request, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.CreateInstallmentPlanAsync(user.RequireUserId(), id, request, ct)))
            .WithSummary("Difiere una compra de la tarjeta en cuotas. La compra sigue siendo un solo gasto.");

        cards.MapPost("/{id:guid}/installments/{planId:guid}/cancel", async (Guid id, Guid planId, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.CancelInstallmentPlanAsync(user.RequireUserId(), id, planId, ct)))
            .WithSummary("Precancela un diferido: lo que falta se vuelve exigible desde hoy.");

        cards.MapDelete("/{id:guid}/installments/{planId:guid}", async (Guid id, Guid planId, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
            {
                await service.DeleteInstallmentPlanAsync(user.RequireUserId(), id, planId, ct);
                return Results.NoContent();
            })
            .WithSummary("Quita el plan de cuotas (la compra no era diferida). La compra no cambia.");

        cards.MapPost("/{id:guid}/payments", async (Guid id, RegisterCardPaymentRequest request, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.RegisterPaymentAsync(user.RequireUserId(), id, request, ct)))
            .WithSummary("Registra un pago a la tarjeta desde una cuenta: dos movimientos vinculados que no son ni ingreso ni gasto.");

        cards.MapGet("/{id:guid}/payments/suggestions", async (Guid id, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.ListPaymentSuggestionsAsync(user.RequireUserId(), id, ct)))
            .WithSummary("Débitos de tus cuentas que parecen pagos a esta tarjeta. Solo sugerencias: nada se vincula sin confirmar.");

        cards.MapPost("/{id:guid}/payments/link", async (Guid id, LinkCardPaymentRequest request, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.LinkPaymentAsync(user.RequireUserId(), id, request, ct)))
            .WithSummary("Confirma que un débito del banco es un pago a esta tarjeta (deja de contar como gasto).");

        cards.MapPost("/{id:guid}/movements/{transactionId:guid}/type", async (Guid id, Guid transactionId, ReclassifyCardMovementRequest request, ICreditCardService service, ICurrentUser user, CancellationToken ct) =>
                Results.Ok(await service.ReclassifyMovementAsync(user.RequireUserId(), id, transactionId, request, ct)))
            .WithSummary("Cambia el tipo de un movimiento de la tarjeta (compra, devolución, pago, interés, comisión, avance, ajuste).");

        return app;
    }
}
