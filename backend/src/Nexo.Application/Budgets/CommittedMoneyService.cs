using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Analytics;
using Nexo.Application.Common;
using Nexo.Application.CreditCards;
using Nexo.Domain.Accounts;
using Nexo.Domain.Budgets;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Budgets;

public interface ICommittedMoneyService
{
    /// <summary>Tu dinero → Comprometido → Disponible, with every source that feeds Comprometido.</summary>
    Task<CommittedMoneyDto> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// What Comprometido and Disponible WOULD be if this budget were saved -- run
    /// through the same <see cref="CommittedMoneyCalculator"/> as the real number,
    /// never a client-side formula.
    /// </summary>
    Task<BudgetPreviewDto> PreviewBudgetAsync(Guid userId, BudgetPreviewRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The only place that assembles Comprometido. It gathers candidates from every
/// source that exists in the domain today and hands them to
/// <see cref="CommittedMoneyCalculator"/>, which de-duplicates and totals them:
/// <list type="number">
/// <item><b>Presupuestos reservados</b>: the unspent part of every active budget
/// with ReserveFunds in its current window.</item>
/// <item><b>Próximos pagos</b>: recurring payments (same detector as Estadísticas)
/// seen last month and not yet this month, whose expected date is not already
/// long past.</item>
/// <item><b>Tarjetas</b>: the next payment of every card with auto-reserve on, as
/// computed by CreditCardCalculator (via <see cref="CreditCardLedger"/>) -- the pending
/// part of the last statement, or the projected one of the open cycle. Never the
/// whole debt: installments of later statements are future debt, not money to set
/// aside today.</item>
/// </list>
/// "Tu dinero" is the money in the person's accounts only: a card's debt is not
/// subtracted from it (that is what the card's Comprometido line is for) and its
/// available credit is never added to it.
/// </summary>
public sealed class CommittedMoneyService(INexoDbContext db, BudgetLedgerFactory ledgers, CreditCardLedger cards) : ICommittedMoneyService
{
    public const string ReservedBudgetType = "reserved_budget";
    public const string UpcomingPaymentType = "upcoming_payment";
    public const string CreditCardType = "credit_card";

    /// <summary>
    /// An expected payment that is more than this many days late is no longer
    /// treated as pending -- it was probably cancelled or paid another way, and
    /// holding money back for it forever would understate Disponible.
    /// </summary>
    public const int UpcomingPaymentGraceDays = 5;

    public async Task<CommittedMoneyDto> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var ledger = await ledgers.OpenAsync(userId, cancellationToken);
        var currentMoney = await CurrentMoneyAsync(userId, cancellationToken);
        var (candidates, upcoming) = await BuildCandidatesAsync(ledger, cancellationToken);
        var cardDueDates = await AddCardCandidatesAsync(userId, candidates, cancellationToken);

        var result = CommittedMoneyCalculator.Calculate(currentMoney, candidates);

        var budgetNames = ledger.Budgets.ToDictionary(b => b.Id.ToString("N"), b => b.Name, StringComparer.Ordinal);

        var sources = new List<CommittedSourceDto>();

        var reservedLines = result.Lines.Where(l => l.Source == CommitmentSource.ReservedBudget).ToList();
        if (reservedLines.Count > 0)
        {
            sources.Add(new CommittedSourceDto(
                ReservedBudgetType,
                "Presupuestos reservados",
                "Lo que aún no gastas de los presupuestos en los que elegiste reservar dinero.",
                result.TotalFor(CommitmentSource.ReservedBudget),
                reservedLines
                    .OrderByDescending(l => l.CountedAmount)
                    .Select(l => new CommittedItemDto(l.Label, l.CountedAmount, l.GrossAmount, l.ReferenceId, l.CategoryId, null, null))
                    .ToList()));
        }

        var upcomingLines = result.Lines.Where(l => l.Source == CommitmentSource.UpcomingPayment).ToList();
        if (upcomingLines.Count > 0)
        {
            sources.Add(new CommittedSourceDto(
                UpcomingPaymentType,
                "Próximos pagos",
                "Pagos que se repiten cada mes y todavía no aparecen este mes. Es una estimación a partir de tu historial.",
                result.TotalFor(CommitmentSource.UpcomingPayment),
                upcomingLines
                    .OrderByDescending(l => l.CountedAmount)
                    .Select(l => new CommittedItemDto(
                        l.Label,
                        l.CountedAmount,
                        l.GrossAmount,
                        null,
                        l.CategoryId,
                        l.CoveredByReferenceKey is { } key && budgetNames.TryGetValue(key, out var name) ? name : null,
                        upcoming.TryGetValue(l.ReferenceKey, out var expected) ? expected : null))
                    .ToList()));
        }

        var cardLines = result.Lines.Where(l => l.Source == CommitmentSource.CreditCard).ToList();
        if (cardLines.Count > 0)
        {
            sources.Add(new CommittedSourceDto(
                CreditCardType,
                "Tarjetas",
                "El próximo pago de las tarjetas en las que elegiste reservarlo. Solo lo que vence en el próximo estado, no toda la deuda.",
                result.TotalFor(CommitmentSource.CreditCard),
                cardLines
                    .OrderBy(l => cardDueDates.TryGetValue(l.ReferenceKey, out var due) ? due : DateOnly.MaxValue)
                    .Select(l => new CommittedItemDto(
                        l.Label,
                        l.CountedAmount,
                        l.GrossAmount,
                        null,
                        null,
                        null,
                        cardDueDates.TryGetValue(l.ReferenceKey, out var due) ? due : null,
                        l.ReferenceId))
                    .ToList()));
        }

        var lastDay = new DateOnly(ledger.Today.Year, ledger.Today.Month, DateTime.DaysInMonth(ledger.Today.Year, ledger.Today.Month));
        var daysRemaining = lastDay.DayNumber - ledger.Today.DayNumber + 1;

        return new CommittedMoneyDto(
            result.CurrentMoney,
            result.Committed,
            result.Available,
            result.Overcommitted,
            result.IsOvercommitted,
            ledger.User.PreferredCurrency,
            sources,
            daysRemaining,
            daysRemaining > 0 ? MoneyMath.Round(result.Available / daysRemaining) : null);
    }

    public async Task<BudgetPreviewDto> PreviewBudgetAsync(Guid userId, BudgetPreviewRequest request, CancellationToken cancellationToken)
    {
        if (MoneyMath.Round(request.Amount) <= 0m)
        {
            throw ValidationException.For(nameof(request.Amount), "El monto del presupuesto debe ser mayor que cero.");
        }

        var ledger = await ledgers.OpenAsync(userId, cancellationToken);
        BudgetService.ResolveCategory(ledger, request.CategoryId);

        if (request.BudgetId is { } existingId && ledger.Budgets.All(b => b.Id != existingId))
        {
            throw new NotFoundException("Budget", existingId);
        }

        var currentMoney = await CurrentMoneyAsync(userId, cancellationToken);
        var (candidates, _) = await BuildCandidatesAsync(ledger, cancellationToken);
        await AddCardCandidatesAsync(userId, candidates, cancellationToken);
        var before = CommittedMoneyCalculator.Calculate(currentMoney, candidates);

        // Editing: the budget's current reserve is REPLACED by the new one, never added twice.
        var after = candidates
            .Where(c => !(c.Source == CommitmentSource.ReservedBudget && request.BudgetId is { } id && c.ReferenceId == id))
            .ToList();

        var period = BudgetService.ParsePeriod(request.Period);
        var startDate = request.StartDate
                        ?? (request.BudgetId is { } editId ? ledger.Budgets.First(b => b.Id == editId).StartDate : BudgetService.DefaultStart(period, ledger.Today));
        var endDate = request.EndDate ?? (period == BudgetPeriod.Custom ? startDate : null);
        var isRecurring = period != BudgetPeriod.Custom && (request.IsRecurring ?? true);

        decimal alreadySpent = 0m;
        var window = BudgetSchedule.WindowContaining(period, isRecurring, startDate, endDate, ledger.Today);
        if (window is { } current)
        {
            var rows = await ledger.LoadRowsAsync(current.Start, current.End, cancellationToken);
            var match = ledger.Match(request.CategoryId, current, rows);
            alreadySpent = MoneyMath.Round(Math.Max(match.Expenses - match.Refunds, 0m));

            if (request.ReserveFunds)
            {
                after.Add(new CommitmentCandidate(
                    CommitmentSource.ReservedBudget,
                    request.BudgetId?.ToString("N") ?? "preview",
                    "Este presupuesto",
                    request.CategoryId,
                    BudgetCalculator.ReservedContribution(request.Amount, match.Expenses, match.Refunds),
                    request.BudgetId));
            }
        }

        var afterResult = CommittedMoneyCalculator.Calculate(currentMoney, after);

        return new BudgetPreviewDto(
            before.CurrentMoney,
            before.Committed,
            before.Available,
            MoneyMath.Round(afterResult.Committed - before.Committed),
            afterResult.Committed,
            afterResult.Available,
            afterResult.Overcommitted,
            alreadySpent);
    }

    /// <summary>
    /// "Tu dinero": the same figure Home shows as the total -- the sum of every
    /// non-archived account's current balance (see TransactionService.GetHomeSummaryAsync),
    /// credit cards excluded: their balance is debt and their available credit is not money.
    /// </summary>
    private async Task<decimal> CurrentMoneyAsync(Guid userId, CancellationToken cancellationToken)
    {
        var balances = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsArchived && a.AccountType != AccountKinds.LiabilityType)
            .Select(a => a.EstimatedBalance)
            .ToListAsync(cancellationToken);

        return MoneyMath.Round(balances.Sum());
    }

    /// <summary>Tarjetas: one candidate per active card whose next payment is reserved. Returns each one's due date by key.</summary>
    private async Task<Dictionary<string, DateOnly>> AddCardCandidatesAsync(
        Guid userId,
        List<CommitmentCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var dueDates = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        var context = await cards.OpenAsync(userId, cancellationToken);
        foreach (var state in await cards.LoadAsync(context, includeArchived: false, cancellationToken))
        {
            if (state.Snapshot is not { CommittedContribution: > 0m, NextPayment: { } next } snapshot)
            {
                continue;
            }

            var key = "card:" + state.Account.Id.ToString("N");
            dueDates[key] = next.DueDate;
            candidates.Add(new CommitmentCandidate(
                CommitmentSource.CreditCard,
                key,
                state.Account.Alias,
                null,
                snapshot.CommittedContribution,
                state.Account.Id));
        }

        return dueDates;
    }

    private async Task<(List<CommitmentCandidate> Candidates, Dictionary<string, DateOnly> ExpectedDates)> BuildCandidatesAsync(
        BudgetLedger ledger,
        CancellationToken cancellationToken)
    {
        var candidates = new List<CommitmentCandidate>();

        // 1) Reserved budgets, current window only.
        var reserving = ledger.Budgets
            .Where(b => b.IsActive && b.ReserveFunds)
            .Select(b => (Budget: b, Window: b.WindowContaining(ledger.Today)))
            .Where(x => x.Window is not null)
            .Select(x => (x.Budget, Window: x.Window!.Value))
            .ToList();

        if (reserving.Count > 0)
        {
            var rows = await ledger.LoadRowsAsync(
                reserving.Min(x => x.Window.Start),
                reserving.Max(x => x.Window.End),
                cancellationToken);

            foreach (var (budget, window) in reserving)
            {
                var (progress, _) = ledger.Evaluate(budget, window, rows);
                if (progress.Reserved > 0m)
                {
                    candidates.Add(new CommitmentCandidate(
                        CommitmentSource.ReservedBudget,
                        budget.Id.ToString("N"),
                        budget.Name,
                        budget.CategoryId,
                        progress.Reserved,
                        budget.Id));
                }
            }
        }

        // 2) Recurring payments expected this month.
        var expectedDates = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var payment in await DetectUpcomingPaymentsAsync(ledger, cancellationToken))
        {
            var key = "merchant:" + payment.Merchant.Key;
            expectedDates[key] = payment.ExpectedDate;
            candidates.Add(new CommitmentCandidate(
                CommitmentSource.UpcomingPayment,
                key,
                payment.Merchant.Merchant,
                payment.Merchant.CategoryId,
                MoneyMath.Round(payment.Merchant.AverageAmount)));
        }

        return (candidates, expectedDates);
    }

    private async Task<List<(RecurringMerchant Merchant, DateOnly ExpectedDate)>> DetectUpcomingPaymentsAsync(
        BudgetLedger ledger,
        CancellationToken cancellationToken)
    {
        var dates = ledger.Dates;
        var monthStart = dates.StartOfMonth(ledger.Now);
        var previousMonthStart = dates.StartOfPreviousMonth(ledger.Now);
        var lookbackStart = monthStart.AddMonths(-(RecurringPaymentDetector.LookbackMonths - 1));
        var userId = ledger.User.Id;

        var accountIds = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsArchived)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (accountIds.Count == 0)
        {
            return [];
        }

        var now = ledger.Now;
        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TransactionDate >= lookbackStart
                        && t.TransactionDate <= now
                        && t.Direction == TransactionDirection.Expense
                        && !t.IsInternalTransfer
                        && t.Merchant != null
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .WhereIdIn(t => t.FinancialAccountId, accountIds)
            .Select(t => new RecurringCandidateRow(t.Merchant!, t.Amount, t.TransactionDate, t.CategoryId))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var names = ledger.Categories.ToDictionary(c => c.Key, c => c.Value.Name);
        var recurring = RecurringPaymentDetector.Detect(rows, names, instant =>
        {
            var local = dates.ToLocal(instant);
            return (local.Year, local.Month);
        });

        var result = new List<(RecurringMerchant, DateOnly)>();
        foreach (var merchant in recurring)
        {
            // Seen last month and not yet this month = still pending this month.
            if (merchant.LastSeenAt < previousMonthStart || merchant.LastSeenAt >= monthStart)
            {
                continue;
            }

            var lastSeen = DateOnly.FromDateTime(dates.ToLocalDate(merchant.LastSeenAt));
            var expected = lastSeen.AddMonths(1);
            if (expected < ledger.Today.AddDays(-UpcomingPaymentGraceDays))
            {
                continue;
            }

            result.Add((merchant, expected));
        }

        return result;
    }
}
