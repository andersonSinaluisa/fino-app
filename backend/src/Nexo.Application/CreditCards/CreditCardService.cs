using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Accounts;
using Nexo.Application.Budgets;
using Nexo.Application.Common;
using Nexo.Application.Transactions;
using Nexo.Domain.Accounts;
using Nexo.Domain.Audit;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.CreditCards;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;

namespace Nexo.Application.CreditCards;

public interface ICreditCardService
{
    Task<CreditCardListDto> ListAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken);

    Task<CreditCardDetailDto> GetAsync(Guid userId, Guid cardId, CancellationToken cancellationToken);

    Task<CreditCardDetailDto> CreateAsync(Guid userId, CreateCreditCardRequest request, CancellationToken cancellationToken);

    Task<CreditCardDetailDto> UpdateAsync(Guid userId, Guid cardId, UpdateCreditCardRequest request, CancellationToken cancellationToken);

    Task<CreditCardDetailDto> SetDebtAsync(Guid userId, Guid cardId, SetCreditCardDebtRequest request, CancellationToken cancellationToken);

    /// <summary>Archive, never delete: movements, statements, installments and history stay.</summary>
    Task ArchiveAsync(Guid userId, Guid cardId, CancellationToken cancellationToken);

    Task<CreditCardDetailDto> RestoreAsync(Guid userId, Guid cardId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CreditCardStatementDto>> ListStatementsAsync(Guid userId, Guid cardId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CreditCardStatementDto>> DeclareStatementAsync(Guid userId, Guid cardId, DeclareStatementRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<CreditCardStatementDto>> RemoveDeclaredStatementAsync(Guid userId, Guid cardId, DateOnly closingDate, CancellationToken cancellationToken);

    Task<IReadOnlyList<InstallmentPlanDto>> ListInstallmentPlansAsync(Guid userId, Guid cardId, CancellationToken cancellationToken);

    Task<InstallmentPlanDto> CreateInstallmentPlanAsync(Guid userId, Guid cardId, CreateInstallmentPlanRequest request, CancellationToken cancellationToken);

    Task<InstallmentPlanDto> CancelInstallmentPlanAsync(Guid userId, Guid cardId, Guid planId, CancellationToken cancellationToken);

    Task DeleteInstallmentPlanAsync(Guid userId, Guid cardId, Guid planId, CancellationToken cancellationToken);

    Task<CardPaymentResultDto> RegisterPaymentAsync(Guid userId, Guid cardId, RegisterCardPaymentRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<CardPaymentSuggestionDto>> ListPaymentSuggestionsAsync(Guid userId, Guid cardId, CancellationToken cancellationToken);

    Task<CardPaymentResultDto> LinkPaymentAsync(Guid userId, Guid cardId, LinkCardPaymentRequest request, CancellationToken cancellationToken);

    Task<TransactionDetailDto> ReclassifyMovementAsync(Guid userId, Guid cardId, Guid transactionId, ReclassifyCardMovementRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Tarjetas de crédito: the card screens and the card-specific write operations.
/// Every figure is read from <see cref="CreditCardLedger"/> (and so from
/// <see cref="CreditCardCalculator"/>); movements are the account's ordinary
/// movements, so nothing about statistics, budgets, splits or deduplication is
/// re-implemented here.
/// </summary>
public sealed class CreditCardService(
    INexoDbContext db,
    IClock clock,
    CreditCardLedger ledger,
    IAccountService accounts,
    ITransactionService transactions,
    ICardMovementClassifier classifier) : ICreditCardService
{
    /// <summary>How many days apart a bank debit and a card payment may be and still be suggested as the same payment.</summary>
    public const int PaymentMatchWindowDays = 5;

    /// <summary>How far back the payment suggestions look.</summary>
    public const int PaymentSuggestionLookbackDays = 90;

    private const decimal MaximumAmount = 1_000_000m;

    // ------------------------------------------------------------------ reads

    public async Task<CreditCardListDto> ListAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken)
    {
        var context = await ledger.OpenAsync(userId, cancellationToken);
        var states = await ledger.LoadAsync(context, includeArchived, cancellationToken);
        var cards = states.Select(s => MapSummary(s, context)).ToList();
        var active = cards.Where(c => !c.IsArchived).ToList();

        return new CreditCardListDto(
            cards,
            MoneyMath.Round(active.Sum(c => c.CurrentDebt)),
            MoneyMath.Round(active.Sum(c => c.NextPayment?.Amount ?? 0m)),
            MoneyMath.Round(active.Sum(c => c.CommittedContribution)),
            context.User.PreferredCurrency);
    }

    public async Task<CreditCardDetailDto> GetAsync(Guid userId, Guid cardId, CancellationToken cancellationToken)
    {
        var context = await ledger.OpenAsync(userId, cancellationToken);
        var state = await ledger.RequireAsync(context, cardId, cancellationToken);
        return await MapDetailAsync(state, context, cancellationToken);
    }

    public async Task<IReadOnlyList<CreditCardStatementDto>> ListStatementsAsync(Guid userId, Guid cardId, CancellationToken cancellationToken)
    {
        var context = await ledger.OpenAsync(userId, cancellationToken);
        var state = await ledger.RequireAsync(context, cardId, cancellationToken);
        return MapStatements(state);
    }

    public async Task<IReadOnlyList<InstallmentPlanDto>> ListInstallmentPlansAsync(Guid userId, Guid cardId, CancellationToken cancellationToken)
    {
        var context = await ledger.OpenAsync(userId, cancellationToken);
        var state = await ledger.RequireAsync(context, cardId, cancellationToken);
        return await MapPlansAsync(state, context, cancellationToken);
    }

    // ------------------------------------------------------------------ card lifecycle

    public async Task<CreditCardDetailDto> CreateAsync(Guid userId, CreateCreditCardRequest request, CancellationToken cancellationToken)
    {
        var lastFour = ValidateLastFour(request.LastFour);
        var network = ParseNetwork(request.Network);
        decimal? openingBalance = null;
        if (request.CurrentDebt is { } debt)
        {
            openingBalance = -ValidateNonNegative(debt, nameof(request.CurrentDebt), "La deuda actual no puede ser negativa.");
        }

        var code = (request.ProviderCode ?? string.Empty).Trim().ToUpperInvariant();
        var provider = await db.Providers.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code && p.IsActive, cancellationToken)
                       ?? throw new NotFoundException("Provider", code);

        if (provider.Code == ProviderCodes.Cash)
        {
            throw ValidationException.For(nameof(request.ProviderCode), "Elige el banco o la institución que emitió la tarjeta.");
        }

        // Validate the terms BEFORE anything is written: a card account without
        // terms must never be left behind by a rejected request.
        BillingCalendar.EnsureValidDays(request.ClosingDay, request.PaymentDueDay);
        ValidateLimit(request.CreditLimit);

        // The card is an account like any other: created through the same path, so
        // onboarding milestones, audit and display order work exactly the same.
        var created = await accounts.CreateAsync(
            userId,
            new CreateAccountRequest(
                provider.Code,
                request.Name,
                AccountKinds.LiabilityType.ToString(),
                provider.Supports(ConnectionMode.ManualImport) ? nameof(ConnectionMode.ManualImport) : provider.DefaultMode.ToString(),
                lastFour,
                openingBalance,
                request.Currency),
            cancellationToken);

        var account = await RequireAccountAsync(userId, created.Id, cancellationToken);
        var now = clock.UtcNow;
        var card = CreditCard.Configure(account, network, request.CreditLimit, request.ClosingDay, request.PaymentDueDay, request.AutoReserve, now);
        db.CreditCards.Add(card);
        Audit(userId, AuditActions.CreditCardConfigured, account.Id, now);
        await db.SaveChangesAsync(cancellationToken);

        Track("created");
        if (card.AutoReserve)
        {
            Track("auto_reserve_enabled");
        }

        return await GetAsync(userId, account.Id, cancellationToken);
    }

    public async Task<CreditCardDetailDto> UpdateAsync(Guid userId, Guid cardId, UpdateCreditCardRequest request, CancellationToken cancellationToken)
    {
        var account = await RequireCardAccountAsync(userId, cardId, cancellationToken);
        var network = ParseNetwork(request.Network);
        var now = clock.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            account.Rename(request.Name, now);
        }

        if (request.LastFour is not null)
        {
            account.ChangeMask(ValidateLastFour(request.LastFour), now);
        }

        var card = await db.CreditCards.FirstOrDefaultAsync(c => c.UserId == userId && c.FinancialAccountId == account.Id, cancellationToken);
        var hadAutoReserve = card?.AutoReserve ?? false;

        if (card is null)
        {
            // A card account created before this module: this completes its setup.
            card = CreditCard.Configure(account, network, request.CreditLimit, request.ClosingDay, request.PaymentDueDay, request.AutoReserve, now);
            db.CreditCards.Add(card);
        }
        else
        {
            var calendarChanged = card.ClosingDay != request.ClosingDay || card.PaymentDueDay != request.PaymentDueDay;
            card.Update(network, request.CreditLimit, request.ClosingDay, request.PaymentDueDay, request.AutoReserve, now);

            if (calendarChanged)
            {
                var context = await ledger.OpenAsync(userId, cancellationToken);
                var plans = await db.InstallmentPlans
                    .Include(p => p.Installments)
                    .Where(p => p.UserId == userId && p.CreditCardId == card.Id && p.Status == InstallmentPlanStatus.Active)
                    .ToListAsync(cancellationToken);
                foreach (var plan in plans)
                {
                    plan.Reschedule(card.ClosingDay, card.PaymentDueDay, context.Today, now);
                }
            }
        }

        Audit(userId, AuditActions.CreditCardConfigured, account.Id, now);
        await db.SaveChangesAsync(cancellationToken);

        Track("updated");
        if (card.AutoReserve && !hadAutoReserve)
        {
            Track("auto_reserve_enabled");
        }

        return await GetAsync(userId, account.Id, cancellationToken);
    }

    public async Task<CreditCardDetailDto> SetDebtAsync(Guid userId, Guid cardId, SetCreditCardDebtRequest request, CancellationToken cancellationToken)
    {
        await RequireCardAccountAsync(userId, cardId, cancellationToken);
        var debt = ValidateNonNegative(request.CurrentDebt, nameof(request.CurrentDebt), "La deuda no puede ser negativa.");

        // A card's balance is negative when money is owed: "debo $750" is a balance of −750.
        await accounts.SetVerifiedBalanceAsync(userId, cardId, new SetVerifiedBalanceRequest(-debt, request.AsOf), cancellationToken);
        Audit(userId, AuditActions.CreditCardDebtSet, cardId, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(userId, cardId, cancellationToken);
    }

    public async Task ArchiveAsync(Guid userId, Guid cardId, CancellationToken cancellationToken)
    {
        await RequireCardAccountAsync(userId, cardId, cancellationToken);
        await accounts.ArchiveAsync(userId, cardId, cancellationToken);
        Track("archived");
    }

    public async Task<CreditCardDetailDto> RestoreAsync(Guid userId, Guid cardId, CancellationToken cancellationToken)
    {
        var account = await RequireCardAccountAsync(userId, cardId, cancellationToken);
        var now = clock.UtcNow;
        account.Unarchive(now);
        Audit(userId, AuditActions.CreditCardRestored, account.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(userId, cardId, cancellationToken);
    }

    // ------------------------------------------------------------------ statements

    public async Task<IReadOnlyList<CreditCardStatementDto>> DeclareStatementAsync(
        Guid userId,
        Guid cardId,
        DeclareStatementRequest request,
        CancellationToken cancellationToken)
    {
        var account = await RequireCardAccountAsync(userId, cardId, cancellationToken);
        var card = await RequireCardTermsAsync(userId, account, cancellationToken);
        var context = await ledger.OpenAsync(userId, cancellationToken);

        if (request.ClosingDate > context.Today)
        {
            throw ValidationException.For(nameof(request.ClosingDate), "Un estado de cuenta solo existe después de su fecha de corte.");
        }

        await UpsertDeclaredStatementAsync(
            userId,
            card,
            request.ClosingDate,
            request.DueDate,
            request.StatementBalance,
            request.MinimumPayment,
            request.PeriodStart,
            StatementSource.Manual,
            importId: null,
            cancellationToken);

        Audit(userId, AuditActions.CreditCardStatementDeclared, account.Id, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        Track("statement_declared");

        return await ListStatementsAsync(userId, cardId, cancellationToken);
    }

    public async Task<IReadOnlyList<CreditCardStatementDto>> RemoveDeclaredStatementAsync(
        Guid userId,
        Guid cardId,
        DateOnly closingDate,
        CancellationToken cancellationToken)
    {
        var account = await RequireCardAccountAsync(userId, cardId, cancellationToken);
        var card = await RequireCardTermsAsync(userId, account, cancellationToken);

        var statement = await db.CreditCardStatements
            .FirstOrDefaultAsync(s => s.UserId == userId && s.CreditCardId == card.Id && s.ClosingDate == closingDate, cancellationToken)
            ?? throw new NotFoundException("CreditCardStatement", closingDate);

        db.CreditCardStatements.Remove(statement);
        Audit(userId, AuditActions.CreditCardStatementRemoved, account.Id, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        return await ListStatementsAsync(userId, cardId, cancellationToken);
    }

    /// <summary>
    /// Shared with the statement import: one declared statement per closing. A new
    /// declaration within a week of an existing one is the same statement corrected
    /// (the bank moved the corte, or the person fixed a typo), never a second one.
    /// </summary>
    internal async Task UpsertDeclaredStatementAsync(
        Guid userId,
        CreditCard card,
        DateOnly closingDate,
        DateOnly dueDate,
        decimal balance,
        decimal? minimum,
        DateOnly? periodStart,
        StatementSource source,
        Guid? importId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var from = closingDate.AddDays(-CreditCardCalculator.DeclaredClosingToleranceDays);
        var to = closingDate.AddDays(CreditCardCalculator.DeclaredClosingToleranceDays);

        var existing = await db.CreditCardStatements
            .Where(s => s.UserId == userId && s.CreditCardId == card.Id && s.ClosingDate >= from && s.ClosingDate <= to)
            .ToListAsync(cancellationToken);

        var target = existing.OrderBy(s => Math.Abs(s.ClosingDate.DayNumber - closingDate.DayNumber)).FirstOrDefault();
        try
        {
            if (target is null)
            {
                db.CreditCardStatements.Add(CreditCardStatement.Declare(card, closingDate, dueDate, balance, minimum, source, now, periodStart, importId));
            }
            else
            {
                target.Revise(closingDate, dueDate, balance, minimum, source, now, periodStart, importId);
            }
        }
        catch (DomainException ex)
        {
            throw ValidationException.For(nameof(DeclareStatementRequest.StatementBalance), ex.Message);
        }
    }

    // ------------------------------------------------------------------ installments

    public async Task<InstallmentPlanDto> CreateInstallmentPlanAsync(
        Guid userId,
        Guid cardId,
        CreateInstallmentPlanRequest request,
        CancellationToken cancellationToken)
    {
        var account = await RequireCardAccountAsync(userId, cardId, cancellationToken);
        var card = await RequireCardTermsAsync(userId, account, cancellationToken);
        var context = await ledger.OpenAsync(userId, cancellationToken);

        var purchase = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId && t.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Transaction", request.TransactionId);

        if (purchase.FinancialAccountId != account.Id)
        {
            throw new NotFoundException("Transaction", request.TransactionId);
        }

        if (await db.InstallmentPlans.AnyAsync(p => p.UserId == userId && p.TransactionId == purchase.Id, cancellationToken))
        {
            throw new ConflictException("Esta compra ya está diferida en cuotas.");
        }

        // A purchase that reached the card before it had a type is a purchase by default.
        if (purchase.CardMovementType is null)
        {
            await classifier.ApplyAsync(account, purchase, null, clock.UtcNow, cancellationToken);
        }

        var purchaseDate = DateOnly.FromDateTime(context.Dates.ToLocalDate(purchase.TransactionDate));
        var firstClosing = request.FirstClosingDate ?? BillingCalendar.ClosingOnOrAfter(purchaseDate, card.ClosingDay);
        var now = clock.UtcNow;

        var plan = InstallmentPlan.Create(card, purchase, purchaseDate, request.NumberOfInstallments, firstClosing, request.InterestRate, now);
        db.InstallmentPlans.Add(plan);
        Audit(userId, AuditActions.InstallmentPlanCreated, plan.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        Track("installment_created");

        return await RequirePlanDtoAsync(userId, cardId, plan.Id, cancellationToken);
    }

    public async Task<InstallmentPlanDto> CancelInstallmentPlanAsync(Guid userId, Guid cardId, Guid planId, CancellationToken cancellationToken)
    {
        var plan = await RequirePlanAsync(userId, cardId, planId, cancellationToken);
        var context = await ledger.OpenAsync(userId, cancellationToken);
        var now = clock.UtcNow;

        plan.Cancel(context.Today, now);
        Audit(userId, AuditActions.InstallmentPlanCancelled, plan.Id, now);
        await db.SaveChangesAsync(cancellationToken);

        return await RequirePlanDtoAsync(userId, cardId, plan.Id, cancellationToken);
    }

    public async Task DeleteInstallmentPlanAsync(Guid userId, Guid cardId, Guid planId, CancellationToken cancellationToken)
    {
        var plan = await RequirePlanAsync(userId, cardId, planId, cancellationToken);
        db.InstallmentPlans.Remove(plan);
        Audit(userId, AuditActions.InstallmentPlanDeleted, plan.Id, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    // ------------------------------------------------------------------ payments

    public async Task<CardPaymentResultDto> RegisterPaymentAsync(
        Guid userId,
        Guid cardId,
        RegisterCardPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var amount = ValidateAmount(request.Amount);

        // Double tap / retry after a lost response: the payment already exists.
        if (request.ClientRequestId is { } clientRequestId)
        {
            var already = await db.Transactions
                .AsNoTracking()
                .Where(t => t.UserId == userId && t.ClientRequestId == clientRequestId)
                .Select(t => new { t.Id, t.InternalTransferLinkId, t.FinancialAccountId })
                .FirstOrDefaultAsync(cancellationToken);

            if (already is not null)
            {
                if (already.FinancialAccountId != cardId)
                {
                    throw new ConflictException("Ese identificador de solicitud ya se usó para otro movimiento.");
                }

                return await PaymentResultAsync(userId, cardId, already.Id, already.InternalTransferLinkId, cancellationToken);
            }
        }

        var card = await RequireCardAccountAsync(userId, cardId, cancellationToken);
        if (card.IsArchived)
        {
            throw new ConflictException("Esta tarjeta está archivada. Restáurala para registrar pagos.");
        }

        var now = clock.UtcNow;
        var paidAt = NormalizeDate(request.PaidAt, now);

        FinancialAccount? source = null;
        if (request.SourceAccountId is { } sourceId)
        {
            source = await RequireAccountAsync(userId, sourceId, cancellationToken);
            EnsureCanPayFrom(source, card);
        }

        var cardLeg = Transaction.Create(
            userId,
            card.Id,
            card.ProviderCode,
            paidAt,
            amount,
            TransactionDirection.Income,
            source is null ? "Pago de tarjeta" : $"Pago desde {source.Alias}",
            TransactionSource.Manual,
            now,
            card.Currency,
            accountMask: card.Mask,
            clientRequestId: request.ClientRequestId);
        await classifier.ApplyAsync(card, cardLeg, CreditCardMovementType.Payment, now, cancellationToken);
        cardLeg.SetNote(request.Note, now);
        db.Transactions.Add(cardLeg);
        card.ApplyMovement(cardLeg.SignedAmount, cardLeg.TransactionDate, now);

        Transaction? bankLeg = null;
        if (source is not null)
        {
            bankLeg = await CreateBankLegAsync(userId, source, card, paidAt, amount, now, cancellationToken);
            bankLeg.SetNote(request.Note, now);
            bankLeg.MarkAsInternalTransfer(cardLeg.Id, now);
            cardLeg.MarkAsInternalTransfer(bankLeg.Id, now);
        }

        Audit(userId, AuditActions.CreditCardPaymentRegistered, card.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        Track("payment_registered");

        return await PaymentResultAsync(userId, cardId, cardLeg.Id, bankLeg?.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<CardPaymentSuggestionDto>> ListPaymentSuggestionsAsync(Guid userId, Guid cardId, CancellationToken cancellationToken)
    {
        var card = await RequireCardAccountAsync(userId, cardId, cancellationToken);
        var terms = await db.CreditCards.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId && c.FinancialAccountId == card.Id, cancellationToken);
        var since = clock.UtcNow.AddDays(-PaymentSuggestionLookbackDays);

        var moneyAccounts = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.AccountType != AccountKinds.LiabilityType && !a.IsArchived)
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        if (moneyAccounts.Count == 0)
        {
            return [];
        }

        var bankDebits = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TransactionDate >= since
                        && t.Direction == TransactionDirection.Expense
                        && !t.IsInternalTransfer
                        && t.Currency == card.Currency
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .WhereIdIn(t => t.FinancialAccountId, moneyAccounts.Keys.ToArray())
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync(cancellationToken);

        var unpairedPayments = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.FinancialAccountId == card.Id
                        && t.TransactionDate >= since
                        && t.CardMovementType == CreditCardMovementType.Payment
                        && t.InternalTransferLinkId == null
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .ToListAsync(cancellationToken);

        var claimed = new HashSet<Guid>();
        var suggestions = new List<CardPaymentSuggestionDto>();
        foreach (var debit in bankDebits)
        {
            var match = unpairedPayments
                .Where(p => !claimed.Contains(p.Id)
                            && p.Amount == debit.Amount
                            && Math.Abs((p.TransactionDate - debit.TransactionDate).TotalDays) <= PaymentMatchWindowDays)
                .OrderBy(p => Math.Abs((p.TransactionDate - debit.TransactionDate).TotalDays))
                .FirstOrDefault();

            string? reason = null;
            if (match is not null)
            {
                claimed.Add(match.Id);
                reason = "matches_card_payment";
            }
            else if (CreditCardMovementRules.LooksLikeCardPayment(debit.Description, card.Mask, terms?.Network))
            {
                reason = "looks_like_card_payment";
            }

            if (reason is null)
            {
                continue;
            }

            var bank = moneyAccounts[debit.FinancialAccountId];
            suggestions.Add(new CardPaymentSuggestionDto(
                debit.Id,
                bank.Id,
                bank.Alias,
                debit.TransactionDate,
                debit.Amount,
                debit.Description,
                match?.Id,
                reason));
        }

        return suggestions;
    }

    public async Task<CardPaymentResultDto> LinkPaymentAsync(Guid userId, Guid cardId, LinkCardPaymentRequest request, CancellationToken cancellationToken)
    {
        var card = await RequireCardAccountAsync(userId, cardId, cancellationToken);
        var bankLeg = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == request.BankTransactionId && t.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Transaction", request.BankTransactionId);
        var bank = await RequireAccountAsync(userId, bankLeg.FinancialAccountId, cancellationToken);

        EnsureCanPayFrom(bank, card);
        if (bankLeg.Direction != TransactionDirection.Expense || !bankLeg.CountsTowardsBalance)
        {
            throw new ConflictException("Ese movimiento no es un débito de tu cuenta.");
        }

        if (bankLeg.InternalTransferLinkId is not null)
        {
            throw new ConflictException("Ese movimiento ya está vinculado con otra de tus cuentas.");
        }

        if (bankLeg.IsSplit)
        {
            throw new ConflictException("Ese movimiento está dividido en categorías. Quita la división antes de marcarlo como pago de tarjeta.");
        }

        var now = clock.UtcNow;
        Transaction cardLeg;
        if (request.CardTransactionId is { } cardTransactionId)
        {
            cardLeg = await db.Transactions
                .FirstOrDefaultAsync(t => t.Id == cardTransactionId && t.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("Transaction", cardTransactionId);

            if (cardLeg.FinancialAccountId != card.Id || cardLeg.CardMovementType != CreditCardMovementType.Payment)
            {
                throw new ConflictException("Ese movimiento no es un pago de esta tarjeta.");
            }

            if (cardLeg.InternalTransferLinkId is not null)
            {
                throw new ConflictException("Ese pago ya está vinculado con un débito de tu banco.");
            }

            if (cardLeg.Amount != bankLeg.Amount)
            {
                throw new ConflictException("El pago de la tarjeta y el débito del banco deben ser por el mismo monto.");
            }
        }
        else
        {
            cardLeg = Transaction.Create(
                userId,
                card.Id,
                card.ProviderCode,
                bankLeg.TransactionDate,
                bankLeg.Amount,
                TransactionDirection.Income,
                $"Pago desde {bank.Alias}",
                TransactionSource.Manual,
                now,
                card.Currency,
                accountMask: card.Mask);
            await classifier.ApplyAsync(card, cardLeg, CreditCardMovementType.Payment, now, cancellationToken);
            db.Transactions.Add(cardLeg);
            card.ApplyMovement(cardLeg.SignedAmount, cardLeg.TransactionDate, now);
        }

        bankLeg.MarkAsInternalTransfer(cardLeg.Id, now);
        cardLeg.MarkAsInternalTransfer(bankLeg.Id, now);

        Audit(userId, AuditActions.CreditCardPaymentLinked, card.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        Track("payment_linked");

        return await PaymentResultAsync(userId, cardId, cardLeg.Id, bankLeg.Id, cancellationToken);
    }

    public async Task<TransactionDetailDto> ReclassifyMovementAsync(
        Guid userId,
        Guid cardId,
        Guid transactionId,
        ReclassifyCardMovementRequest request,
        CancellationToken cancellationToken)
    {
        var card = await RequireCardAccountAsync(userId, cardId, cancellationToken);
        var transaction = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Transaction", transactionId);

        if (transaction.FinancialAccountId != card.Id)
        {
            throw new NotFoundException("Transaction", transactionId);
        }

        if (!Enum.TryParse<CreditCardMovementType>(request.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type))
        {
            throw ValidationException.For(nameof(request.Type), "Tipo de movimiento de tarjeta no válido.");
        }

        var now = clock.UtcNow;
        await classifier.ApplyAsync(card, transaction, type, now, cancellationToken);

        // A purchase that stops being one can no longer be deferred.
        if (type != CreditCardMovementType.Purchase)
        {
            var plan = await db.InstallmentPlans.FirstOrDefaultAsync(p => p.UserId == userId && p.TransactionId == transaction.Id, cancellationToken);
            if (plan is not null)
            {
                db.InstallmentPlans.Remove(plan);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await transactions.GetAsync(userId, transaction.Id, cancellationToken);
    }

    // ------------------------------------------------------------------ mapping

    internal static CreditCardSummaryDto MapSummary(CardState state, CreditCardLedger.Context context)
    {
        var account = state.Account;
        context.Providers.TryGetValue(account.ProviderCode, out var provider);
        var snapshot = state.Snapshot;
        var card = state.Card;

        CreditCardNextPaymentDto? next = null;
        if (snapshot?.NextPayment is { } payment)
        {
            next = new CreditCardNextPaymentDto(
                payment.Amount,
                payment.DueDate,
                payment.MinimumPayment,
                payment.FromClosedStatement ? "statement" : "current_cycle",
                payment.IsOverdue,
                payment.DueDate.DayNumber - context.Today.DayNumber);
        }

        return new CreditCardSummaryDto(
            account.Id,
            account.Alias,
            account.ProviderCode,
            provider?.Name ?? account.ProviderCode,
            provider?.BrandColor ?? "#1D1D1B",
            account.Mask,
            account.Currency,
            card?.Network.ToString(),
            account.IsArchived,
            card is null,
            card?.CreditLimit,
            card?.ClosingDay,
            card?.PaymentDueDay,
            card?.AutoReserve ?? false,
            state.CurrentDebt,
            snapshot?.CreditBalance ?? Math.Max(MoneyMath.Round(account.EstimatedBalance), 0m),
            snapshot?.AvailableCredit,
            snapshot?.UtilizationPercent,
            snapshot?.IsOverLimit ?? false,
            snapshot?.DeferredDebt ?? 0m,
            next,
            account.IsArchived ? 0m : snapshot?.CommittedContribution ?? 0m,
            account.BalanceKind.ToString(),
            account.LastSyncedAt);
    }

    private async Task<CreditCardDetailDto> MapDetailAsync(CardState state, CreditCardLedger.Context context, CancellationToken cancellationToken)
    {
        var snapshot = state.Snapshot;
        CreditCardCycleDto? cycle = null;
        if (snapshot is not null)
        {
            var open = snapshot.Statements[0];
            cycle = new CreditCardCycleDto(open.PeriodStart, open.ClosingDate, open.DueDate, open.StatementBalance, open.Charges, open.Credits);
        }

        var monthStart = new DateOnly(context.Today.Year, context.Today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var activity = CreditCardCalculator.Summarize(state.Movements, monthStart, monthEnd);

        var recent = await transactions.QueryAsync(
            context.User.Id,
            new TransactionFilter { AccountId = state.Account.Id, Page = new PageRequest { Page = 1, PageSize = 5 } },
            cancellationToken);

        var plans = await MapPlansAsync(state, context, cancellationToken);

        return new CreditCardDetailDto(
            MapSummary(state, context),
            cycle,
            snapshot?.LastStatement is { } last ? MapStatement(last, state, isCurrent: false) : null,
            new CreditCardActivityDto(
                BudgetLedger.MonthLabel(monthStart),
                activity.Purchases,
                activity.Refunds,
                activity.Payments,
                activity.Interest,
                activity.Fees,
                activity.CashAdvances,
                activity.Adjustments),
            plans
                .Where(p => p.Status == nameof(InstallmentPlanStatus.Active))
                .OrderBy(p => p.NextInstallmentClosingDate)
                .ToList(),
            recent.Items);
    }

    private static List<CreditCardStatementDto> MapStatements(CardState state)
    {
        if (state.Snapshot is null)
        {
            return [];
        }

        return state.Snapshot.Statements
            .Select((s, index) => MapStatement(s, state, isCurrent: index == 0))
            .ToList();
    }

    private static CreditCardStatementDto MapStatement(StatementView view, CardState state, bool isCurrent)
    {
        var declared = view.IsDeclared
            ? state.Declared.FirstOrDefault(d => d.ClosingDate == view.ClosingDate)
            : null;

        return new CreditCardStatementDto(
            view.PeriodStart,
            view.ClosingDate,
            view.DueDate,
            view.StatementBalance,
            view.MinimumPayment,
            view.AmountPaid,
            view.Pending,
            view.Status.ToString(),
            view.IsDeclared,
            declared?.Source.ToString(),
            view.Charges,
            view.Credits,
            isCurrent);
    }

    private async Task<IReadOnlyList<InstallmentPlanDto>> MapPlansAsync(CardState state, CreditCardLedger.Context context, CancellationToken cancellationToken)
    {
        if (state.Plans.Count == 0)
        {
            return [];
        }

        var userId = context.User.Id;
        var purchaseIds = state.Plans.Select(p => p.TransactionId).ToArray();
        var purchases = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .WhereIdIn(t => t.Id, purchaseIds)
            .Select(t => new { t.Id, t.Description, t.Merchant, t.MerchantCorrected, t.CategoryId })
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var categoryIds = purchases.Values.Where(p => p.CategoryId is not null).Select(p => p.CategoryId!.Value).Distinct().ToArray();
        var categoryNames = categoryIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Categories
                .AsNoTracking()
                .Where(c => c.UserId == null || c.UserId == userId)
                .WhereIdIn(c => c.Id, categoryIds)
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return state.Plans
            .Select(plan =>
            {
                purchases.TryGetValue(plan.TransactionId, out var purchase);
                var description = purchase is null
                    ? "Compra diferida"
                    : (string.IsNullOrWhiteSpace(purchase.MerchantCorrected) ? purchase.Merchant : purchase.MerchantCorrected) ?? purchase.Description;
                var categoryName = purchase?.CategoryId is { } categoryId && categoryNames.TryGetValue(categoryId, out var name) ? name : null;
                return MapPlan(plan, context.Today, description, categoryName);
            })
            .OrderBy(p => p.Status == nameof(InstallmentPlanStatus.Active) ? 0 : 1)
            .ThenBy(p => p.NextInstallmentClosingDate)
            .ToList();
    }

    internal static InstallmentPlanDto MapPlan(InstallmentPlan plan, DateOnly today, string description, string? categoryName)
    {
        var installments = plan.Installments.OrderBy(i => i.Number).ToList();
        var statuses = installments.Select(i => (Installment: i, Status: i.StatusOn(today, plan.CancelledOn))).ToList();
        var billed = statuses.Count(s => s.Status == InstallmentStatus.Billed);
        var next = statuses.FirstOrDefault(s => s.Status == InstallmentStatus.Upcoming).Installment;
        var outstanding = MoneyMath.Round(statuses.Where(s => s.Status == InstallmentStatus.Upcoming).Sum(s => s.Installment.Amount));

        var status = plan.Status == InstallmentPlanStatus.Cancelled
            ? "Cancelled"
            : billed == installments.Count ? "Completed" : "Active";

        return new InstallmentPlanDto(
            plan.Id,
            plan.TransactionId,
            description,
            categoryName,
            plan.StartDate,
            plan.OriginalAmount,
            plan.NumberOfInstallments,
            plan.InstallmentAmount,
            plan.InterestRate,
            billed,
            next?.Number,
            next?.Amount,
            next?.ClosingDate,
            next?.DueDate,
            outstanding,
            status,
            statuses.Select(s => new InstallmentDto(s.Installment.Number, s.Installment.Amount, s.Installment.ClosingDate, s.Installment.DueDate, s.Status.ToString())).ToList());
    }

    // ------------------------------------------------------------------ helpers

    private async Task<CardPaymentResultDto> PaymentResultAsync(Guid userId, Guid cardId, Guid cardTransactionId, Guid? bankTransactionId, CancellationToken cancellationToken)
    {
        var context = await ledger.OpenAsync(userId, cancellationToken);
        var state = await ledger.RequireAsync(context, cardId, cancellationToken);
        return new CardPaymentResultDto(cardTransactionId, bankTransactionId, MapSummary(state, context));
    }

    private async Task<InstallmentPlanDto> RequirePlanDtoAsync(Guid userId, Guid cardId, Guid planId, CancellationToken cancellationToken)
    {
        var plans = await ListInstallmentPlansAsync(userId, cardId, cancellationToken);
        return plans.FirstOrDefault(p => p.Id == planId) ?? throw new NotFoundException("InstallmentPlan", planId);
    }

    private async Task<InstallmentPlan> RequirePlanAsync(Guid userId, Guid cardId, Guid planId, CancellationToken cancellationToken)
    {
        var account = await RequireCardAccountAsync(userId, cardId, cancellationToken);
        var card = await RequireCardTermsAsync(userId, account, cancellationToken);
        return await db.InstallmentPlans
                   .Include(p => p.Installments)
                   .FirstOrDefaultAsync(p => p.Id == planId && p.UserId == userId && p.CreditCardId == card.Id, cancellationToken)
               ?? throw new NotFoundException("InstallmentPlan", planId);
    }

    private async Task<Transaction> CreateBankLegAsync(
        Guid userId,
        FinancialAccount source,
        FinancialAccount card,
        DateTimeOffset paidAt,
        decimal amount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var bankLeg = Transaction.Create(
            userId,
            source.Id,
            source.ProviderCode,
            paidAt,
            amount,
            TransactionDirection.Expense,
            $"Pago tarjeta {card.Alias}",
            TransactionSource.Manual,
            now,
            source.Currency,
            accountMask: source.Mask);

        // Readable in every list ("Transferencias"), and excluded from spending by the link anyway.
        var transfers = await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null && c.Code == CategoryCodes.Transfers)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (transfers is { } transfersId)
        {
            bankLeg.ApplyAutomaticCategory(transfersId, now, CategorySource.SystemRule);
        }

        db.Transactions.Add(bankLeg);
        source.ApplyMovement(bankLeg.SignedAmount, bankLeg.TransactionDate, now);
        return bankLeg;
    }

    private static void EnsureCanPayFrom(FinancialAccount source, FinancialAccount card)
    {
        if (source.Id == card.Id || source.IsLiability)
        {
            throw ValidationException.For(nameof(RegisterCardPaymentRequest.SourceAccountId), "Elige la cuenta de dinero desde la que pagaste la tarjeta.");
        }

        if (source.IsArchived)
        {
            throw ValidationException.For(nameof(RegisterCardPaymentRequest.SourceAccountId), "Esa cuenta está archivada.");
        }

        if (!string.Equals(source.Currency, card.Currency, StringComparison.Ordinal))
        {
            // Fino does not convert currencies: a cross-currency payment would need a rate it does not have.
            throw ValidationException.For(nameof(RegisterCardPaymentRequest.SourceAccountId), "La cuenta y la tarjeta deben estar en la misma moneda.");
        }
    }

    private async Task<FinancialAccount> RequireAccountAsync(Guid userId, Guid accountId, CancellationToken cancellationToken) =>
        await db.FinancialAccounts.FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("FinancialAccount", accountId);

    private async Task<FinancialAccount> RequireCardAccountAsync(Guid userId, Guid cardId, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, cardId, cancellationToken);
        return account.IsLiability ? account : throw new NotFoundException("CreditCard", cardId);
    }

    private async Task<CreditCard> RequireCardTermsAsync(Guid userId, FinancialAccount account, CancellationToken cancellationToken) =>
        await db.CreditCards.FirstOrDefaultAsync(c => c.UserId == userId && c.FinancialAccountId == account.Id, cancellationToken)
        ?? throw new ConflictException("Completa los datos de la tarjeta (cupo, día de corte y día de pago) primero.");

    private static string? ValidateLastFour(string? lastFour)
    {
        if (string.IsNullOrWhiteSpace(lastFour))
        {
            return null;
        }

        var digits = lastFour.Trim();
        if (digits.Length != 4 || !digits.All(char.IsAsciiDigit))
        {
            // Never accept (and so never store) more than the last four digits.
            throw ValidationException.For(nameof(CreateCreditCardRequest.LastFour), "Escribe solo los últimos 4 dígitos de la tarjeta.");
        }

        return digits;
    }

    private static void ValidateLimit(decimal limit)
    {
        if (limit <= 0m || decimal.Round(limit, 2) != limit || limit > MaximumAmount)
        {
            throw ValidationException.For(nameof(CreateCreditCardRequest.CreditLimit), "El cupo debe ser mayor que cero y tener como máximo dos decimales.");
        }
    }

    private static CardNetwork ParseNetwork(string? network)
    {
        if (string.IsNullOrWhiteSpace(network))
        {
            return CardNetwork.Other;
        }

        return Enum.TryParse<CardNetwork>(network, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw ValidationException.For(nameof(CreateCreditCardRequest.Network), "Marca de tarjeta no válida.");
    }

    private static decimal ValidateAmount(decimal amount)
    {
        var rounded = MoneyMath.Round(amount);
        if (amount <= 0m || rounded == 0m || rounded > MaximumAmount)
        {
            throw ValidationException.For(nameof(RegisterCardPaymentRequest.Amount), "El monto debe ser mayor que cero.");
        }

        return rounded;
    }

    private static decimal ValidateNonNegative(decimal amount, string field, string message)
    {
        var rounded = MoneyMath.Round(amount);
        return rounded < 0m || rounded > MaximumAmount ? throw ValidationException.For(field, message) : rounded;
    }

    private static DateTimeOffset NormalizeDate(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value is not { } date)
        {
            return now;
        }

        var utc = date.ToUniversalTime();
        return utc > now ? now : utc;
    }

    private void Audit(Guid userId, string action, Guid entityId, DateTimeOffset now) =>
        db.AuditLog.Add(AuditLogEntry.Record(userId, action, "CreditCard", now, entityId.ToString()));

    private static void Track(string action) =>
        NexoTelemetry.CreditCardEvents.Add(1, new KeyValuePair<string, object?>("action", action));
}
