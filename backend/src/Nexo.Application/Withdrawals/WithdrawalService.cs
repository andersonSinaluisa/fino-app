using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Application.QuickEntry;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Withdrawals;

/// <summary>
/// §1 y §28: retiros de efectivo como ESPECIALIZACIÓN de las transferencias
/// internas, no como un sistema paralelo.
///
/// La diferencia con <c>InternalTransferService</c> es una sola, y explica todo lo
/// que hay aquí: aquel motor empareja dos movimientos que YA EXISTEN, y un retiro
/// normalmente tiene una sola pata. El banco dice "-$100" y no hay ningún "+$100"
/// en ninguna parte, porque el efectivo no lo reporta nadie. Así que cuando falta,
/// Fino la construye; cuando ya está (porque la persona registró el ingreso a mano),
/// se usa la que hay.
///
/// A partir de ahí no hay nada especial: las dos patas se marcan con el MISMO
/// <c>Transaction.MarkAsInternalTransfer</c> que usa cualquier transferencia. Eso es
/// lo que hace que §6, §20, §21 y §27 salgan gratis -- <c>AnalyticsService</c>,
/// <c>InsightEngine</c>, <c>PulseEngine</c> y el resumen de Home ya filtran
/// <c>!IsInternalTransfer</c> en todas sus métricas de gasto. No se tocó ni una
/// estadística para esta funcionalidad.
///
/// TAMPOCO HAY ESQUEMA NUEVO. El estado de revisión (§23) se deriva de datos que ya
/// existen: "confirmado" es <c>IsInternalTransfer</c>, "rechazado" es
/// <c>CategoryManuallySet</c> (la persona clasificó el movimiento a mano, así que
/// Fino deja de opinar), y "sin revisar" es todo lo demás que el detector marque.
/// </summary>
public interface IWithdrawalService
{
    /// <summary>§9: los retiros pendientes de revisar, más reciente primero.</summary>
    Task<IReadOnlyList<WithdrawalCandidateDto>> ListCandidatesAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>§5: "sí, pasó a Efectivo".</summary>
    Task<WithdrawalConfirmationDto> ConfirmAsync(
        Guid userId,
        Guid transactionId,
        ConfirmWithdrawalRequest request,
        CancellationToken cancellationToken);

    /// <summary>§10: "no, fue un gasto". Deja de proponerse sin perder la clasificación.</summary>
    Task RejectAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken);

    /// <summary>§8: cuántos retiros dejó una importación, para avisar sin interrumpir.</summary>
    Task<WithdrawalScanDto> ScanImportAsync(Guid userId, Guid importId, CancellationToken cancellationToken);
}

public sealed class WithdrawalService(
    INexoDbContext db,
    IClock clock,
    ICashAccountProvisioner cashAccounts) : IWithdrawalService
{
    /// <summary>Hasta dónde mira hacia atrás la bandeja. Mismo criterio que InternalTransferService.</summary>
    private const int LookbackDays = 180;

    /// <summary>
    /// §13: "mismo día o ±1 día". Es la ventana para emparejar un retiro con un
    /// ingreso de efectivo que la persona ya había apuntado. Deliberadamente más
    /// estrecha que los 3 días de las transferencias entre bancos: una transferencia
    /// puede tardar en acreditarse, pero el efectivo del cajero está en la mano al
    /// instante -- lo único que puede desfasarse es cuándo la persona lo apuntó.
    /// </summary>
    private const int CashMatchWindowDays = 1;

    /// <summary>§19: a partir de aquí se ofrece (no se activa) la conciliación automática.</summary>
    public const int ConfirmationsBeforeSuggestingAutomation = 3;

    /// <summary>§5 y §22: lo que se lee en la lista de movimientos para la pata de efectivo.</summary>
    private const string CashLegDescription = "Retiro en efectivo";

    public async Task<IReadOnlyList<WithdrawalCandidateDto>> ListCandidatesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var since = clock.UtcNow.AddDays(-LookbackDays);

        var movements = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.Direction == TransactionDirection.Expense
                        && !t.IsInternalTransfer
                        // §23 "rejected": la persona ya dijo que era un gasto y lo
                        // categorizó a mano. Volver a preguntarlo sería no escuchar.
                        && !t.CategoryManuallySet
                        && t.TransactionDate >= since
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync(cancellationToken);

        // El detector es puro, así que corre en memoria sobre lo ya traído en vez de
        // intentar expresar decenas de LIKE en SQL.
        var detected = movements
            .Select(t => (Transaction: t, Signal: WithdrawalDetector.Detect(
                t.Description,
                t.Direction,
                t.Amount,
                t.ExternalReference)))
            .Where(pair => pair.Signal.IsCandidate)
            .ToList();

        if (detected.Count == 0)
        {
            return [];
        }

        var cashAccount = await cashAccounts.FindAsync(userId, cancellationToken);

        // `var` no puede inferir el tipo de una expresión de colección vacía en un
        // condicional, así que el tipo va explícito.
        List<Transaction> cashIncome = cashAccount is null
            ? []
            : await LoadUnlinkedCashIncomeAsync(userId, cashAccount.Id, since, cancellationToken);

        var aliases = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Id, a.Alias })
            .ToDictionaryAsync(a => a.Id, a => a.Alias, cancellationToken);

        var learned = await LoadLearnedPatternsAsync(userId, cancellationToken);

        var claimed = new HashSet<Guid>();
        var candidates = new List<WithdrawalCandidateDto>(detected.Count);

        foreach (var (transaction, signal) in detected)
        {
            var matches = cashIncome
                .Where(income => !claimed.Contains(income.Id)
                                 && income.Amount == transaction.Amount
                                 && income.Currency == transaction.Currency
                                 && Math.Abs((income.TransactionDate - transaction.TransactionDate).TotalDays)
                                    <= CashMatchWindowDays)
                .OrderBy(income => Math.Abs((income.TransactionDate - transaction.TransactionDate).TotalDays))
                .ToList();

            // §13: con más de un candidato NO se elige. Se dice que los hay y decide
            // la persona; adivinar aquí podría vincular el retiro del martes con el
            // efectivo del miércoles y dejar los dos reales descuadrados.
            var suggestion = matches.Count == 1 ? matches[0] : null;

            if (suggestion is not null)
            {
                claimed.Add(suggestion.Id);
            }

            var pattern = WithdrawalDetector.SuggestPattern(transaction.Description);
            var canLearn = pattern is not null && !TextNormalizer.IsTooGenericRulePattern(pattern);

            candidates.Add(new WithdrawalCandidateDto(
                transaction.Id,
                transaction.FinancialAccountId,
                aliases.GetValueOrDefault(transaction.FinancialAccountId, "Cuenta"),
                transaction.ProviderCode,
                transaction.TransactionDate,
                transaction.Description,
                transaction.Amount,
                transaction.Currency,
                signal.Confidence.ToString(),
                signal.MatchedTerm,
                suggestion?.Id,
                suggestion?.TransactionDate,
                matches.Count,
                canLearn,
                canLearn ? learned.GetValueOrDefault((pattern!, transaction.ProviderCode), 0) : 0));
        }

        return candidates;
    }

    public async Task<WithdrawalConfirmationDto> ConfirmAsync(
        Guid userId,
        Guid transactionId,
        ConfirmWithdrawalRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var bank = await RequireTransactionAsync(userId, transactionId, cancellationToken);

        if (bank.Direction != TransactionDirection.Expense)
        {
            throw new ConflictException("Un retiro tiene que ser un movimiento que saca dinero de la cuenta.");
        }

        if (bank.IsInternalTransfer)
        {
            throw new ConflictException("Este movimiento ya está conciliado como transferencia.");
        }

        // §11: si todavía no hay cuenta Efectivo, se crea aquí mismo y se sigue. La
        // persona no tiene que salir, crearla y volver a empezar.
        var existingCash = await cashAccounts.FindAsync(userId, cancellationToken);
        var cashAccount = await cashAccounts.GetOrCreateAsync(userId, cancellationToken);
        var cashAccountCreated = existingCash is null;

        if (cashAccount.Id == bank.FinancialAccountId)
        {
            throw new ConflictException("Un retiro no puede tener la cuenta de efectivo en los dos lados.");
        }

        Transaction cashLeg;
        bool matchedExisting;

        if (request.CashTransactionId is { } cashTransactionId)
        {
            // §12: la persona ya había apuntado este efectivo a mano.
            cashLeg = await RequireTransactionAsync(userId, cashTransactionId, cancellationToken);
            matchedExisting = true;

            if (cashLeg.FinancialAccountId != cashAccount.Id)
            {
                throw new ConflictException("El movimiento indicado no está en tu cuenta de efectivo.");
            }

            if (cashLeg.Direction != TransactionDirection.Income)
            {
                throw new ConflictException("La otra parte de un retiro tiene que ser un ingreso en efectivo.");
            }

            if (cashLeg.IsInternalTransfer)
            {
                throw new ConflictException("Ese ingreso en efectivo ya está conciliado con otro movimiento.");
            }

            if (cashLeg.Amount != bank.Amount || cashLeg.Currency != bank.Currency)
            {
                throw new ConflictException("Las dos partes de un retiro tienen que ser del mismo monto y moneda.");
            }
        }
        else
        {
            // El caso normal: no hay pata de efectivo, así que Fino la crea.
            //
            // Sólo se crea la ENTRADA de efectivo. La salida del banco ya existe y ya
            // bajó el saldo bancario cuando se importó; duplicarla haría que la
            // persona pareciera $100 más pobre (§5, "NO duplicar la salida bancaria").
            cashLeg = Transaction.Create(
                userId,
                cashAccount.Id,
                cashAccount.ProviderCode,
                bank.TransactionDate,
                bank.Amount,
                TransactionDirection.Income,
                CashLegDescription,
                // La persona confirmó a mano que este efectivo entró: no lo reportó
                // ninguna institución. Manual es la única fuente honesta.
                TransactionSource.Manual,
                now,
                bank.Currency,
                confidence: SourceConfidence.High,
                status: TransactionStatus.Posted);

            // Un retiro no es un ingreso que categorizar: es dinero cambiando de
            // sitio. Sin categoría, y marcado como decisión, para que ninguna regla
            // se lo lleve a "Ingresos" y ensucie los desgloses.
            cashLeg.ClearCategoryManually(now);

            db.Transactions.Add(cashLeg);
            cashAccount.ApplyMovement(cashLeg.SignedAmount, cashLeg.TransactionDate, now);
            matchedExisting = false;
        }

        // A partir de aquí es una transferencia interna como cualquier otra.
        bank.MarkAsInternalTransfer(cashLeg.Id, now);
        cashLeg.MarkAsInternalTransfer(bank.Id, now);

        var patternRemembered = request.RememberPattern
                                && await RememberPatternAsync(userId, bank, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new WithdrawalConfirmationDto(
            bank.Id,
            cashLeg.Id,
            cashAccount.Id,
            cashAccount.Alias,
            bank.Amount,
            bank.Currency,
            cashAccountCreated,
            matchedExisting,
            patternRemembered);
    }

    public async Task RejectAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var transaction = await RequireTransactionAsync(userId, transactionId, cancellationToken);

        if (transaction.IsInternalTransfer)
        {
            throw new ConflictException("Este movimiento está conciliado como transferencia. Deshaz la conciliación primero.");
        }

        // §10 "Fue un gasto": se marca como decidido a mano. Eso lo saca de la
        // bandeja para siempre SIN cambiar su categoría ni su clasificación -- si ya
        // tenía una categoría acertada, la conserva.
        if (transaction.IsSplit)
        {
            // Movimientos divididos: ya es una decisión manual (su división); nada que marcar.
        }
        else if (transaction.CategoryId is { } categoryId)
        {
            transaction.SetCategoryManually(categoryId, now);
        }
        else
        {
            transaction.ClearCategoryManually(now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WithdrawalScanDto> ScanImportAsync(
        Guid userId,
        Guid importId,
        CancellationToken cancellationToken)
    {
        var movements = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.ImportId == importId
                        && t.Direction == TransactionDirection.Expense
                        && !t.IsInternalTransfer
                        && !t.CategoryManuallySet)
            .Select(t => new { t.Description, t.Direction, t.Amount, t.ExternalReference })
            .ToListAsync(cancellationToken);

        var signals = movements
            .Select(t => WithdrawalDetector.Detect(t.Description, t.Direction, t.Amount, t.ExternalReference))
            .Where(signal => signal.IsCandidate)
            .ToList();

        return new WithdrawalScanDto(
            signals.Count,
            signals.Count(signal => signal.Confidence == WithdrawalConfidence.High));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<List<Transaction>> LoadUnlinkedCashIncomeAsync(
        Guid userId,
        Guid cashAccountId,
        DateTimeOffset since,
        CancellationToken cancellationToken) =>
        await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.FinancialAccountId == cashAccountId
                        && t.Direction == TransactionDirection.Income
                        && !t.IsInternalTransfer
                        && t.TransactionDate >= since
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// §14/§19: cuántas veces se confirmó ya cada patrón, por banco. Se deriva de las
    /// reglas aprendidas que apuntan a "Transferencias" -- no hay entidad nueva,
    /// porque <see cref="CategorizationRule"/> ya tiene patrón, banco, dueño y
    /// contador de usos.
    /// </summary>
    private async Task<Dictionary<(string Pattern, string ProviderCode), int>> LoadLearnedPatternsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var transfersCategoryId = await db.Categories
            .Where(c => c.UserId == null && c.Code == CategoryCodes.Transfers)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (transfersCategoryId is null)
        {
            return [];
        }

        var rules = await db.CategorizationRules
            .AsNoTracking()
            .Where(r => r.UserId == userId
                        && r.IsActive
                        && r.CategoryId == transfersCategoryId
                        && r.ProviderCode != null)
            .Select(r => new { r.Pattern, r.ProviderCode, r.TimesApplied })
            .ToListAsync(cancellationToken);

        var learned = new Dictionary<(string, string), int>();

        foreach (var rule in rules)
        {
            learned[(rule.Pattern, rule.ProviderCode!)] = rule.TimesApplied;
        }

        return learned;
    }

    /// <summary>
    /// §19: guarda (o refuerza) la regla de que este patrón, en este banco, es un
    /// retiro. Devuelve false cuando el texto del banco es demasiado genérico para
    /// anclar una regla -- "RETIRO" a secas se aplicaría a cualquier movimiento que
    /// diga "retiro" en cualquier cuenta, así que no se guarda en vez de crear una
    /// regla que haría daño.
    /// </summary>
    private async Task<bool> RememberPatternAsync(
        Guid userId,
        Transaction bank,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pattern = WithdrawalDetector.SuggestPattern(bank.Description);

        if (pattern is null || TextNormalizer.IsTooGenericRulePattern(pattern))
        {
            return false;
        }

        var transfersCategoryId = await db.Categories
            .Where(c => c.UserId == null && c.Code == CategoryCodes.Transfers)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (transfersCategoryId is null)
        {
            return false;
        }

        var existing = await db.CategorizationRules
            .FirstOrDefaultAsync(
                r => r.UserId == userId
                     && r.Pattern == pattern
                     && r.ProviderCode == bank.ProviderCode
                     && r.CategoryId == transfersCategoryId,
                cancellationToken);

        if (existing is not null)
        {
            // Ya existía: esto es una confirmación más, que es justo lo que §19 mide.
            existing.RegisterHit(now);
            return true;
        }

        db.CategorizationRules.Add(CategorizationRule.LearnedFromCorrection(
            userId,
            pattern,
            transfersCategoryId.Value,
            TransactionDirection.Expense,
            now,
            providerCode: bank.ProviderCode));

        return true;
    }

    private async Task<Transaction> RequireTransactionAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken) =>
        await db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("Transaction", transactionId);
}
