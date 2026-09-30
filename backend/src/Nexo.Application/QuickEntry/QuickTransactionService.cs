using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Categorization;
using Nexo.Application.Common;
using Nexo.Application.Transactions;
using Nexo.Domain.Accounts;
using Nexo.Domain.Audit;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Application.QuickEntry;

/// <summary>
/// Registro rápido de efectivo (§28): <c>CreateQuickTransaction</c>, el caso de
/// uso central, separado de la interfaz.
///
/// Es el ÚNICO punto de escritura de un movimiento hecho a mano, y lo comparten el
/// bottom sheet rápido, el formulario completo "Más detalles", los frecuentes de un
/// toque, el registro por voz, el widget y los App Intents / App Shortcuts. Añadir
/// un punto de entrada nuevo es llamar aquí, nunca reimplementar la creación.
///
/// La secuencia es deliberadamente la misma que ya usan las otras dos vías de
/// entrada de movimientos (<c>ImportService</c> y <c>EmailIngestionPipeline</c>):
/// resolver cuenta → sesión de categorización → <c>Transaction.Create</c> →
/// <c>ApplyMovement</c> → guardar. No hay modelo de datos paralelo para el
/// efectivo: sale un <see cref="Transaction"/> normal y se devuelve el mismo
/// <see cref="TransactionDetailDto"/> que devuelve cualquier otro movimiento.
/// </summary>
public interface IQuickTransactionService
{
    Task<TransactionDetailDto> CreateAsync(
        Guid userId,
        CreateQuickTransactionRequest request,
        CancellationToken cancellationToken);

    /// <summary>§22: corregir un movimiento propio sin perder su identidad ni su historial.</summary>
    Task<TransactionDetailDto> UpdateAsync(
        Guid userId,
        Guid transactionId,
        UpdateQuickTransactionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// §6 ("Deshacer"): borra de verdad un movimiento que la persona acaba de
    /// registrar a mano. Es la contrapartida que hace honesto guardar sin
    /// confirmación -- y por eso solo aplica a movimientos manuales: un movimiento
    /// que reportó un banco no es del usuario para borrarlo, se ignora
    /// (<c>Transaction.Ignore</c>), que es otra cosa.
    /// </summary>
    Task DeleteAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken);

    /// <summary>§24 y §25: declarar el efectivo que se tiene, o corregirlo.</summary>
    Task<Nexo.Application.Accounts.AccountDto> SetCashBalanceAsync(
        Guid userId,
        SetCashBalanceRequest request,
        CancellationToken cancellationToken);
}

public sealed class QuickTransactionService(
    INexoDbContext db,
    IClock clock,
    ICashAccountProvisioner cashAccounts,
    ICategorizationEngine categorization,
    ITransactionService transactions,
    Nexo.Application.Accounts.IAccountService accounts) : IQuickTransactionService
{
    public async Task<TransactionDetailDto> CreateAsync(
        Guid userId,
        CreateQuickTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var amount = ValidateAmount(request.Amount);
        var direction = ParseDirection(request.Direction);
        var now = clock.UtcNow;
        var occurredAt = NormalizeDate(request.OccurredAt, now);

        // §36 ("duplicados"): el doble toque se resuelve ANTES de tocar nada. Si
        // esta petición ya se procesó, se devuelve aquel movimiento tal cual; para
        // quien llama es indistinguible de haber creado uno, que es justo lo que
        // debe pasar cuando el primer intento sí llegó pero la respuesta se perdió.
        if (request.ClientRequestId is { } clientRequestId)
        {
            var already = await db.Transactions
                .AsNoTracking()
                .Where(t => t.UserId == userId && t.ClientRequestId == clientRequestId)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (already != Guid.Empty)
            {
                return await transactions.GetAsync(userId, already, cancellationToken);
            }
        }

        var account = await ResolveAccountAsync(userId, request.FinancialAccountId, cancellationToken);
        var description = NormalizeDescription(request.Description, direction);

        var (categoryId, categorySource, ruleId, session) = await ResolveCategoryAsync(
            userId,
            request.CategoryId,
            request.LeaveUncategorized,
            description,
            account.ProviderCode,
            amount,
            direction,
            cancellationToken);

        var transaction = Transaction.Create(
            userId,
            account.Id,
            account.ProviderCode,
            occurredAt,
            amount,
            direction,
            description,
            // Por fin se usa: hasta ahora TransactionSource.Manual estaba declarado
            // pero ningún camino del backend lo producía.
            TransactionSource.Manual,
            now,
            account.Currency,
            // Sin referencia externa: no hay banco que emita un número de documento
            // para un billete de cinco dólares. Inventar una sería mentir sobre la
            // procedencia del dato.
            externalReference: null,
            merchant: null,
            categoryId: categoryId,
            accountMask: account.Mask,
            // Lo escribió la persona que estaba ahí. No hay fuente más fiable de que
            // ese movimiento ocurrió: no es una notificación que un extracto pueda
            // contradecir después.
            confidence: SourceConfidence.High,
            status: TransactionStatus.Posted,
            categorySource: categorySource,
            categorizationRuleId: ruleId,
            clientRequestId: request.ClientRequestId);

        if (request.LeaveUncategorized)
        {
            transaction.ClearCategoryManually(now);
        }
        else if (request.CategoryId is not null)
        {
            // La persona eligió la categoría: queda marcada como manual para que
            // ninguna regla se la sobrescriba después.
            transaction.SetCategoryManually(request.CategoryId.Value, now);
        }

        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            transaction.SetNote(request.Note, now);
        }

        db.Transactions.Add(transaction);

        if (session is not null)
        {
            await categorization.RecordHitsAsync(session, now, cancellationToken);
        }

        account.ApplyMovement(transaction.SignedAmount, transaction.TransactionDate, now);
        account.MarkSynced(now);

        // Deliberadamente NO se despacha ninguna notificación (§31): quien acaba de
        // escribir el movimiento ya sabe que lo escribió, y una push por cada gasto
        // en efectivo sería exactamente el ruido que Pulso existe para evitar. Pulso
        // lo analizará luego como a cualquier otro movimiento.
        await db.SaveChangesAsync(cancellationToken);

        return await transactions.GetAsync(userId, transaction.Id, cancellationToken);
    }

    public async Task<TransactionDetailDto> UpdateAsync(
        Guid userId,
        Guid transactionId,
        UpdateQuickTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var amount = ValidateAmount(request.Amount);
        var direction = ParseDirection(request.Direction);
        var now = clock.UtcNow;

        var transaction = await RequireManualAsync(userId, transactionId, cancellationToken);
        var account = await db.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == transaction.FinancialAccountId && a.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("FinancialAccount", transaction.FinancialAccountId);

        transaction.UpdateManualDetails(
            amount,
            direction,
            NormalizeDescription(request.Description, direction),
            NormalizeDate(request.OccurredAt, now),
            now);

        // Movimientos divididos: la categoría de un movimiento dividido vive en su
        // división (PUT /transactions/{id}/splits). Editar monto/descripción/fecha
        // desde aquí no debe borrarla ni reemplazarla por una sola categoría.
        if (transaction.IsSplit)
        {
            // Nada que hacer con la categoría.
        }
        else if (request.LeaveUncategorized)
        {
            transaction.ClearCategoryManually(now);
        }
        else if (request.CategoryId is { } categoryId)
        {
            await RequireCategoryAsync(userId, categoryId, cancellationToken);
            transaction.SetCategoryManually(categoryId, now);
        }

        // El importe con signo pudo cambiar (5 de gasto → 5 de ingreso son 10 de
        // diferencia), así que el saldo no se puede "ajustar por la diferencia" a
        // ojo. Se reconstruye desde el ancla verificada con IAccountService
        // .RecalculateBalanceAsync -- el MISMO método que ya usan las importaciones
        // y el borrado de datos de privacidad, no una segunda implementación del
        // cálculo de saldo que pudiera desviarse de aquella.
        await db.SaveChangesAsync(cancellationToken);
        await accounts.RecalculateBalanceAsync(userId, account.Id, cancellationToken);

        return await transactions.GetAsync(userId, transaction.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var transaction = await RequireManualAsync(userId, transactionId, cancellationToken);

        var account = await db.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == transaction.FinancialAccountId && a.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("FinancialAccount", transaction.FinancialAccountId);

        // Un movimiento manual puede haber sido confirmado como una pata de una
        // transferencia interna. Borrarlo dejaría a la otra pata apuntando al vacío.
        if (transaction.InternalTransferLinkId is { } linkedId)
        {
            var linked = await db.Transactions
                .FirstOrDefaultAsync(t => t.Id == linkedId && t.UserId == userId, cancellationToken);
            linked?.ClearInternalTransfer(now);
        }

        db.Transactions.Remove(transaction);
        await db.SaveChangesAsync(cancellationToken);

        await accounts.RecalculateBalanceAsync(userId, account.Id, cancellationToken);
    }

    public async Task<Nexo.Application.Accounts.AccountDto> SetCashBalanceAsync(
        Guid userId,
        SetCashBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var balance = MoneyMath.Round(request.Balance);

        if (balance < 0m)
        {
            throw ValidationException.For(nameof(request.Balance), "El efectivo no puede ser negativo.");
        }

        var account = await cashAccounts.GetOrCreateAsync(userId, cancellationToken);
        var adjustment = !string.Equals(request.Mode, "Anchor", StringComparison.OrdinalIgnoreCase);

        if (adjustment)
        {
            // §25: "no modificar silenciosamente movimientos históricos. Mantener
            // trazabilidad." La diferencia entre lo que Fino estimaba y lo que la
            // persona tiene de verdad se registra como un movimiento propio y
            // visible, no como una corrección invisible del saldo. Si la diferencia
            // es cero no se inventa un movimiento de 0.
            var difference = MoneyMath.Round(balance - account.EstimatedBalance);
            if (difference != 0m)
            {
                var direction = difference > 0m ? TransactionDirection.Income : TransactionDirection.Expense;
                var transaction = Transaction.Create(
                    userId,
                    account.Id,
                    account.ProviderCode,
                    now,
                    MoneyMath.Abs(difference),
                    direction,
                    QuickEntryDefaults.AdjustmentDescription,
                    TransactionSource.Manual,
                    now,
                    account.Currency,
                    confidence: SourceConfidence.High,
                    status: TransactionStatus.Posted);

                // Un ajuste no es un gasto ni un ingreso que categorizar: es
                // contabilidad interna. Dejarlo sin categoría a propósito evita que
                // ensucie los desgloses por categoría y las estadísticas.
                transaction.ClearCategoryManually(now);
                transaction.SetNote(
                    $"Ajuste automático: Fino estimaba {account.EstimatedBalance:0.00} y declaraste {balance:0.00}.",
                    now);

                db.Transactions.Add(transaction);
                account.ApplyMovement(transaction.SignedAmount, transaction.TransactionDate, now);
            }
        }
        else
        {
            // §24, configuración inicial: todavía no hay historial que explicar, así
            // que anclar el saldo es la respuesta correcta -- y de paso el efectivo
            // pasa a estar "verificado" hasta el siguiente movimiento, exactamente
            // igual que un banco recién conciliado.
            account.SetVerifiedBalance(balance, now, now);
        }

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            adjustment ? AuditActions.CashBalanceAdjusted : AuditActions.CashBalanceAnchored,
            "FinancialAccount",
            now,
            account.Id.ToString()));

        await db.SaveChangesAsync(cancellationToken);

        return await accounts.GetAsync(userId, account.Id, cancellationToken);
    }

    // ---------------------------------------------------------------- helpers

    private static decimal ValidateAmount(decimal amount)
    {
        // §4: "no permitir valores negativos escritos directamente. El signo depende
        // de Gasto/Ingreso." Se rechaza en vez de convertirse en positivo en
        // silencio: un -5 que llega aquí significa que algún cliente entendió mal el
        // contrato, y taparlo produciría movimientos con el signo cambiado.
        if (amount < 0m)
        {
            throw ValidationException.For(
                nameof(CreateQuickTransactionRequest.Amount),
                "El monto va siempre en positivo; el signo lo decide gasto o ingreso.");
        }

        var rounded = MoneyMath.Round(amount);
        if (rounded == 0m)
        {
            throw ValidationException.For(
                nameof(CreateQuickTransactionRequest.Amount),
                "El monto debe ser mayor que cero.");
        }

        // Un tope alto pero real: protege de un keypad que se quedó pegado o de un
        // parser que confundió una fecha con un monto, sin estorbar a nadie que de
        // verdad mueva efectivo.
        if (rounded > 1_000_000m)
        {
            throw ValidationException.For(
                nameof(CreateQuickTransactionRequest.Amount),
                "El monto supera el máximo permitido.");
        }

        return rounded;
    }

    private static TransactionDirection ParseDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
        {
            // §3: gasto por defecto. Es lo que la gente registra el 90% de las veces.
            return TransactionDirection.Expense;
        }

        if (!Enum.TryParse<TransactionDirection>(direction, ignoreCase: true, out var parsed))
        {
            throw ValidationException.For(
                nameof(CreateQuickTransactionRequest.Direction),
                "Debe ser 'Income' o 'Expense'.");
        }

        return parsed;
    }

    private DateTimeOffset NormalizeDate(DateTimeOffset? occurredAt, DateTimeOffset now)
    {
        if (occurredAt is not { } value)
        {
            return now;
        }

        var utc = value.ToUniversalTime();

        // Una fecha futura casi siempre viene de un parser que se equivocó ("8 uber
        // ayer" con el reloj del teléfono mal) o de un huso horario mal aplicado.
        // Un movimiento en efectivo no puede haber pasado todavía, así que se acota
        // a ahora en vez de rechazarlo: bloquear el guardado por esto contradiría
        // "no bloquear por fecha" (§12).
        if (utc > now)
        {
            return now;
        }

        // Un margen amplio hacia atrás, solo para descartar fechas absurdas
        // (1970, 0001) que delatan un error de parseo.
        var floor = now.AddYears(-10);
        return utc < floor ? floor : utc;
    }

    private static string NormalizeDescription(string? description, TransactionDirection direction)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            // §41 ("copy"): cuando la persona no escribe nada el movimiento necesita
            // igualmente una descripción -- el modelo la exige y una lista de
            // movimientos en blanco no se puede leer. Un texto neutro y honesto,
            // nunca un comercio inventado.
            return direction == TransactionDirection.Income
                ? QuickEntryDefaults.IncomeDescription
                : QuickEntryDefaults.ExpenseDescription;
        }

        var trimmed = description.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400];
    }

    private async Task<FinancialAccount> ResolveAccountAsync(
        Guid userId,
        Guid? financialAccountId,
        CancellationToken cancellationToken)
    {
        if (financialAccountId is not { } accountId)
        {
            return await cashAccounts.GetOrCreateAsync(userId, cancellationToken);
        }

        return await db.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("FinancialAccount", accountId);
    }

    /// <summary>
    /// §10 ("categorización automática"): la prioridad es reglas aprendidas → reglas
    /// del sistema → sin categoría. Fíjese en lo que NO hay aquí: ningún diccionario
    /// de palabras clave propio del registro rápido. Cuando el cliente no manda
    /// categoría se usa el mismo <see cref="ICategorizationEngine"/> que categoriza
    /// importaciones y correos, así que una regla que la persona ya enseñó a Fino
    /// vale igual escribiendo "almuerzo" a mano que leyendo un extracto.
    /// </summary>
    private async Task<(Guid? CategoryId, CategorySource Source, Guid? RuleId, ICategorizationSession? Session)> ResolveCategoryAsync(
        Guid userId,
        Guid? requestedCategoryId,
        bool leaveUncategorized,
        string description,
        string providerCode,
        decimal amount,
        TransactionDirection direction,
        CancellationToken cancellationToken)
    {
        if (leaveUncategorized)
        {
            return (null, CategorySource.Uncategorized, null, null);
        }

        if (requestedCategoryId is { } categoryId)
        {
            await RequireCategoryAsync(userId, categoryId, cancellationToken);
            return (categoryId, CategorySource.Manual, null, null);
        }

        var session = await categorization.StartSessionAsync(userId, cancellationToken);
        var normalizedDescription = TextNormalizer.NormalizeForMatching(description);
        var normalizedMerchant = TextNormalizer.NormalizeForMatching(
            TextNormalizer.ExtractMerchant(description));

        var suggestion = session.Suggest(
            normalizedDescription,
            normalizedMerchant,
            providerCode,
            amount,
            direction);

        if (suggestion is null)
        {
            // Ninguna regla reconoció el texto. NO se fuerza la categoría genérica:
            // §43 dice "no obligar a categorizar", y un movimiento honestamente sin
            // categoría es mejor que uno metido en "Otros" como si alguien lo
            // hubiera decidido. La persona puede ponerle categoría después desde el
            // detalle, y esa corrección sí enseña una regla.
            return (null, CategorySource.Uncategorized, null, session);
        }

        var source = suggestion.RuleSource == "user_rule"
            ? CategorySource.UserRule
            : CategorySource.SystemRule;

        return (suggestion.CategoryId, source, suggestion.RuleId, session);
    }

    private async Task RequireCategoryAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken)
    {
        // Aislamiento estricto por usuario: una categoría ajena nunca se acepta,
        // aunque el id venga bien formado.
        var exists = await db.Categories
            .AnyAsync(c => c.Id == categoryId && (c.UserId == null || c.UserId == userId), cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Category", categoryId);
        }
    }

    private async Task<Transaction> RequireManualAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Transaction", transactionId);

        if (transaction.Source != TransactionSource.Manual)
        {
            throw new ConflictException(
                "Solo se puede editar o eliminar así un movimiento que registraste a mano. " +
                "Los movimientos que vienen de un banco se pueden ignorar, no borrar.");
        }

        return transaction;
    }

}
