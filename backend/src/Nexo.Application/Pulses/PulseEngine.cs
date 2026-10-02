using Microsoft.EntityFrameworkCore;
using Nexo.Application.Transactions;
using Nexo.Application.Abstractions;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Accounts;
using Nexo.Domain.Common;
using Nexo.Domain.Providers;
using Nexo.Domain.Pulses;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Pulses;

public interface IPulseEngine
{
    /// <summary>
    /// Runs every detection rule for one user and persists whatever clears dedup
    /// and cooldown. Safe to call as often as you like -- unlike
    /// <c>IInsightEngine.RecomputeAsync</c> (which throws away and rebuilds the
    /// whole set every time), this only ever adds rows, and dedup is what keeps
    /// repeated calls from creating repeated pulses.
    /// </summary>
    Task<IReadOnlyList<FinancialPulse>> EvaluateAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// PULSO's detection engine. A rules engine over real transactions/accounts,
/// exactly like <c>InsightEngine</c> -- never a model, never randomness, every
/// number traces back to a query the user could run by hand. What is different
/// from InsightEngine is the shape of the output: a pulse is a permanent event
/// record, not a replaceable snapshot, so this class is additive (it never
/// deletes) and every rule is responsible for not repeating itself.
///
/// FASE 1 shipped seven rules chosen from the taxonomy in <see cref="PulseType"/>
/// for being computable purely from data that already exists in this codebase:
/// <see cref="PulseType.UnusualExpense"/>, <see cref="PulseType.CategorySpike"/>,
/// <see cref="PulseType.SpendingPace"/>, <see cref="PulseType.SpendingImprovement"/>,
/// <see cref="PulseType.NewIncome"/>, <see cref="PulseType.AccountOutdated"/> and
/// <see cref="PulseType.BalanceChange"/>. FASE 4 adds the two that were deferred
/// then for needing state beyond simple dedup/cooldown:
/// <see cref="PulseType.DailyClose"/> (needs "skip on a quiet day") and
/// <see cref="PulseType.MonthEndProjection"/> (needs "only re-notify on a
/// significant change"). <see cref="PulseType.UpcomingCommitment"/> still has no
/// real "compromiso" domain model to detect from, so it remains undetected.
///
/// Anti-noise, three shapes:
/// <list type="bullet">
/// <item>Event-shaped rules (UnusualExpense, NewIncome) and monthly-bucketed
/// rules (CategorySpike, SpendingPace, SpendingImprovement, DailyClose -- daily
/// rather than monthly bucketed, but the same idea) use a
/// <see cref="FinancialPulse.DedupKey"/> that already encodes the one thing it
/// can ever be about (a transaction id, or a category/month, plain month, or
/// calendar day) -- once that key exists, ever, the rule can never fire again
/// for that exact thing. No time window needed; the key itself is the
/// guarantee. DailyClose additionally never even builds a candidate on a
/// "quiet day" (zero movements): there is nothing to summarize, so the dedup
/// key for that day is simply never created and tomorrow gets a clean try.</item>
/// <item>State-shaped rules (AccountOutdated, BalanceChange) describe an
/// ongoing condition that does not resolve itself the instant it is first
/// noticed, so re-checking "does this key exist" forever would mean noticing it
/// once and then staying silent for good even if it gets worse. These use a
/// cooldown window instead: the key can fire again once its rule's own
/// cooldown constant (<see cref="AccountOutdatedCooldownDays"/>,
/// <see cref="BalanceChangeCooldownDays"/>) has passed.</item>
/// <item>MonthEndProjection is a third shape: value-change-triggered. A
/// cooldown alone would still repeat an unchanged projection forever, and a
/// dedup key alone would freeze the very first estimate for the rest of the
/// month. Instead it looks up its own last pulse for the current month and
/// only creates a new one when the new estimate has moved by a meaningful
/// margin since then (and at least a day has passed) -- "avísame de nuevo
/// solo si esto cambió de verdad", never a running commentary on every
/// recomputation.</item>
/// </list>
///
/// Every rule also caps itself to at most one new pulse per call: when several
/// transactions would each qualify (three unusually large purchases in the same
/// pass, say), only the single most significant one is kept. That is PULSO's
/// "agrupa eventos relacionados, no mandes varios" requirement, applied at the
/// point where it is cheap and unambiguous to apply; full cross-rule grouping is
/// a <see cref="IPulseNotificationDecisionService"/> concern (FASE 3), not a
/// detection concern -- including for DailyClose and MonthEndProjection, whose
/// severity (see each rule's remarks) decides through that same severity gate
/// whether they are worth interrupting anyone about at all.
/// </summary>
public sealed class PulseEngine(INexoDbContext db, IClock clock) : IPulseEngine
{
    /// <summary>How far back a rule may look for its own baseline/history.</summary>
    private const int HistoryLookbackDays = 120;

    /// <summary>An event-shaped rule only ever considers movements this recent -- an old transaction does not suddenly become "news" days later.</summary>
    private const int RecentWindowDays = 3;

    /// <summary>How long a state-shaped rule stays quiet about the same account after firing.</summary>
    private const int AccountOutdatedCooldownDays = 14;

    private const int BalanceChangeCooldownDays = 5;

    private sealed record Movement(
        Guid Id,
        DateTimeOffset Date,
        DateTimeOffset CreatedAt,
        decimal Amount,
        TransactionDirection Direction,
        Guid? CategoryId,
        Guid FinancialAccountId,
        string? Merchant);

    public async Task<IReadOnlyList<FinancialPulse>> EvaluateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return [];
        }

        var dates = new StatementDateInterpreter(user.TimeZoneId);
        var now = clock.UtcNow;
        var since = now.AddDays(-HistoryLookbackDays);

        var scope = db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TransactionDate >= since
                        && !t.IsInternalTransfer
                        // Tarjetas de crédito: a card refund is not new income.
                        && t.CardMovementType != CreditCardMovementType.Refund
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending));

        var movements = await scope
            .Select(t => new Movement(t.Id, t.TransactionDate, t.CreatedAt, t.Amount, t.Direction, t.CategoryId, t.FinancialAccountId, t.Merchant))
            .ToListAsync(cancellationToken);

        // Movimientos divididos: only the category spike groups by category, and it
        // must see a divided movement's parts, never the movement itself as well.
        var splits = await CategoryAllocations.LoadSplitsAsync(db, scope, cancellationToken);
        var movementsByCategory = CategoryAllocations
            .ExpandInMemory(movements, m => m.Id, splits, (m, category, amount) => m with { CategoryId = category, Amount = amount })
            .ToList();

        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var accounts = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsArchived)
            .ToListAsync(cancellationToken);

        var pulses = new List<FinancialPulse>();

        async Task AddIfNewAsync(FinancialPulse? candidate)
        {
            if (candidate is null)
            {
                return;
            }

            if (await db.Pulses.AsNoTracking().AnyAsync(p => p.UserId == userId && p.DedupKey == candidate.DedupKey, cancellationToken))
            {
                return;
            }

            pulses.Add(candidate);
        }

        async Task AddIfCooledDownAsync(FinancialPulse? candidate, int cooldownDays)
        {
            if (candidate is null)
            {
                return;
            }

            var cooldownSince = now.AddDays(-cooldownDays);
            var recentlyFired = await db.Pulses
                .AsNoTracking()
                .AnyAsync(p => p.UserId == userId && p.DedupKey == candidate.DedupKey && p.CreatedAt >= cooldownSince, cancellationToken);
            if (!recentlyFired)
            {
                pulses.Add(candidate);
            }
        }

        await AddIfNewAsync(DetectUnusualExpense(userId, movements, now));
        await AddIfNewAsync(DetectNewIncome(userId, movements, now));
        await AddIfNewAsync(DetectCategorySpike(userId, movementsByCategory, categories, dates, now));
        await AddIfNewAsync(DetectSpendingPace(userId, movements, dates, now));
        await AddIfNewAsync(DetectSpendingImprovement(userId, movements, dates, now));
        await AddIfNewAsync(DetectDailyClose(userId, movements, dates, now));
        await AddIfNewAsync(await DetectMonthEndProjectionAsync(userId, movements, dates, now, cancellationToken));

        foreach (var account in accounts)
        {
            await AddIfCooledDownAsync(DetectAccountOutdated(userId, account, now), AccountOutdatedCooldownDays);
        }

        // A card's balance is debt: "el saldo bajó" means nothing for it.
        foreach (var account in accounts.Where(a => !a.IsLiability))
        {
            await AddIfCooledDownAsync(DetectBalanceChange(userId, account, movements, now), BalanceChangeCooldownDays);
        }

        if (pulses.Count > 0)
        {
            db.Pulses.AddRange(pulses);
            await db.SaveChangesAsync(cancellationToken);
        }

        return pulses;
    }

    // ---------------------------------------------------------------------
    // UNUSUAL_EXPENSE: a single recent movement far above the user's own
    // typical expense. "Far above" is relative to the user, never a fixed
    // dollar amount -- someone whose typical purchase is $200 is not surprised
    // by a $250 one, someone whose typical purchase is $8 is.
    // ---------------------------------------------------------------------
    private FinancialPulse? DetectUnusualExpense(Guid userId, List<Movement> movements, DateTimeOffset now)
    {
        var recentSince = now.AddDays(-RecentWindowDays);
        var historical = movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date < recentSince)
            .Select(m => m.Amount)
            .OrderBy(a => a)
            .ToList();

        // Confidence: fewer than 15 historical expenses is not enough of a
        // pattern to call anything "unusual" against -- PULSO must not assert a
        // pattern without sufficient history.
        if (historical.Count < 15)
        {
            return null;
        }

        var median = historical[historical.Count / 2];
        if (median <= 0m)
        {
            return null;
        }

        var candidate = movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date >= recentSince && m.Amount >= 30m)
            .Select(m => new { Movement = m, Ratio = m.Amount / median })
            .Where(x => x.Ratio >= 3.0m)
            .OrderByDescending(x => x.Ratio)
            .FirstOrDefault();

        if (candidate is null)
        {
            return null;
        }

        var ratio = candidate.Ratio;
        var relevance = ComputeRelevance(
            financialImpact: Math.Min(40m, (ratio - 1m) * 10m),
            anomalyStrength: Math.Min(30m, (ratio - 1m) * 8m),
            urgency: 10m,
            confidence: Math.Min(15m, historical.Count / 2m),
            repetitionPenalty: 0m);

        var merchant = string.IsNullOrWhiteSpace(candidate.Movement.Merchant) ? "un movimiento" : candidate.Movement.Merchant;

        return FinancialPulse.Create(
            userId,
            PulseType.UnusualExpense,
            ratio >= 5.0m ? PulseSeverity.Risk : PulseSeverity.Attention,
            "Gasto fuera de lo común",
            $"{merchant}: {Money(candidate.Movement.Amount)}, unas {ratio:0.#}x tu gasto típico.",
            $"Tus gastos suelen rondar {Money(median)}. Este movimiento fue notablemente más alto, así que vale la pena revisarlo.",
            dedupKey: $"unusual_expense:{candidate.Movement.Id}",
            relevanceScore: relevance,
            periodStart: candidate.Movement.Date,
            periodEnd: candidate.Movement.Date,
            occurredAt: candidate.Movement.Date,
            now: now,
            value: candidate.Movement.Amount,
            comparisonValue: median,
            percentChange: MoneyMath.PercentChange(median, candidate.Movement.Amount),
            referenceId: candidate.Movement.Id.ToString());
    }

    // ---------------------------------------------------------------------
    // NEW_INCOME: a recent income movement that is either the user's first
    // real income signal or clearly larger than their typical income.
    // ---------------------------------------------------------------------
    private FinancialPulse? DetectNewIncome(Guid userId, List<Movement> movements, DateTimeOffset now)
    {
        var recentSince = now.AddDays(-RecentWindowDays);
        var historicalIncome = movements
            .Where(m => m.Direction == TransactionDirection.Income && m.Date < recentSince)
            .Select(m => m.Amount)
            .OrderBy(a => a)
            .ToList();

        var recentIncome = movements
            .Where(m => m.Direction == TransactionDirection.Income && m.Date >= recentSince)
            .OrderByDescending(m => m.Amount)
            .FirstOrDefault();

        if (recentIncome is null)
        {
            return null;
        }

        var isFirstSignal = historicalIncome.Count < 3;
        var median = historicalIncome.Count > 0 ? historicalIncome[historicalIncome.Count / 2] : 0m;
        var ratio = median > 0m ? recentIncome.Amount / median : (decimal?)null;
        var isNotablyLarger = ratio is >= 1.5m;

        if (!isFirstSignal && !isNotablyLarger)
        {
            return null;
        }

        var relevance = ComputeRelevance(
            financialImpact: isFirstSignal ? 20m : Math.Min(35m, ((ratio ?? 1m) - 1m) * 15m),
            anomalyStrength: isFirstSignal ? 10m : Math.Min(25m, ((ratio ?? 1m) - 1m) * 10m),
            urgency: 5m,
            confidence: isFirstSignal ? 5m : Math.Min(15m, historicalIncome.Count),
            repetitionPenalty: 0m);

        var body = isFirstSignal
            ? $"Recibiste {Money(recentIncome.Amount)}."
            : $"Recibiste {Money(recentIncome.Amount)}, más de lo habitual (típico: {Money(median)}).";

        return FinancialPulse.Create(
            userId,
            PulseType.NewIncome,
            PulseSeverity.Positive,
            "Ingreso nuevo",
            body,
            isFirstSignal
                ? "Es de los primeros ingresos que vemos en tu cuenta, así que no hay suficiente historial todavía para comparar -- lo marcamos igual porque cambia tu disponible."
                : $"Tus ingresos suelen rondar {Money(median)}. Este fue notablemente mayor.",
            dedupKey: $"new_income:{recentIncome.Id}",
            relevanceScore: relevance,
            periodStart: recentIncome.Date,
            periodEnd: recentIncome.Date,
            occurredAt: recentIncome.Date,
            now: now,
            value: recentIncome.Amount,
            comparisonValue: median > 0m ? median : null,
            percentChange: median > 0m ? MoneyMath.PercentChange(median, recentIncome.Amount) : null,
            referenceId: recentIncome.Id.ToString());
    }

    // ---------------------------------------------------------------------
    // CATEGORY_SPIKE: a category spending clearly more than its own recent
    // average this month.
    // ---------------------------------------------------------------------
    private FinancialPulse? DetectCategorySpike(
        Guid userId,
        List<Movement> movements,
        Dictionary<Guid, string> categories,
        StatementDateInterpreter dates,
        DateTimeOffset now)
    {
        var monthStart = dates.StartOfMonth(now);
        var baselineStart = monthStart.AddMonths(-3);

        var currentByCategory = movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date >= monthStart && m.CategoryId is not null)
            .GroupBy(m => m.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Total = MoneyMath.Round(g.Sum(x => x.Amount)) });

        var baselineByCategory = movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date >= baselineStart && m.Date < monthStart && m.CategoryId is not null)
            .GroupBy(m => m.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => new { Total = g.Sum(x => x.Amount), Months = g.Select(x => dates.StartOfMonth(x.Date)).Distinct().Count() });

        var best = currentByCategory
            .Select(c =>
            {
                if (!baselineByCategory.TryGetValue(c.CategoryId, out var baseline) || baseline.Months < 2)
                {
                    return null;
                }

                var monthlyAverage = MoneyMath.Round(baseline.Total / baseline.Months);
                if (monthlyAverage <= 0m || c.Total < 20m)
                {
                    return null;
                }

                var percent = MoneyMath.PercentChange(monthlyAverage, c.Total);
                if (percent is null || percent < 50m)
                {
                    return null;
                }

                return new { c.CategoryId, c.Total, Average = monthlyAverage, Percent = percent.Value };
            })
            .Where(x => x is not null)
            .OrderByDescending(x => x!.Percent)
            .FirstOrDefault();

        if (best is null || !categories.TryGetValue(best.CategoryId, out var name))
        {
            return null;
        }

        var relevance = ComputeRelevance(
            financialImpact: Math.Min(35m, best.Total / Math.Max(best.Average, 1m) * 10m),
            anomalyStrength: Math.Min(30m, best.Percent / 4m),
            urgency: 8m,
            confidence: 12m,
            repetitionPenalty: 0m);

        return FinancialPulse.Create(
            userId,
            PulseType.CategorySpike,
            PulseSeverity.Attention,
            $"Más gasto en {name}",
            $"Llevas {Money(best.Total)} en {name} este mes, {best.Percent:0}% más que tu promedio de los últimos meses ({Money(best.Average)}).",
            $"Comparamos lo que llevas gastado este mes en {name} contra el promedio mensual de esa categoría en los últimos 3 meses.",
            dedupKey: $"category_spike:{best.CategoryId}:{monthStart:yyyy-MM}",
            relevanceScore: relevance,
            periodStart: monthStart,
            periodEnd: now,
            occurredAt: now,
            now: now,
            value: best.Total,
            comparisonValue: best.Average,
            percentChange: best.Percent,
            referenceId: best.CategoryId.ToString());
    }

    // ---------------------------------------------------------------------
    // SPENDING_PACE: this month's daily spend rate so far is clearly above the
    // user's own recent daily rate. Deliberately not a month-end dollar
    // estimate -- that is MONTH_END_PROJECTION, FASE 4.
    // ---------------------------------------------------------------------
    private FinancialPulse? DetectSpendingPace(Guid userId, List<Movement> movements, StatementDateInterpreter dates, DateTimeOffset now)
    {
        var localNow = dates.ToLocal(now);
        // Too early in the month for a daily rate to mean anything yet.
        if (localNow.Day < 10)
        {
            return null;
        }

        var monthStart = dates.StartOfMonth(now);
        var previousMonthStart = dates.StartOfPreviousMonth(now);
        var daysElapsed = Math.Max(1, (localNow.Date - dates.ToLocal(monthStart).Date).Days + 1);

        var currentTotal = MoneyMath.Round(movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date >= monthStart)
            .Sum(m => m.Amount));

        var previousExpenses = movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date >= previousMonthStart && m.Date < monthStart)
            .ToList();

        if (previousExpenses.Count == 0 || currentTotal <= 0m)
        {
            return null;
        }

        var previousTotal = MoneyMath.Round(previousExpenses.Sum(m => m.Amount));
        var daysInPreviousMonth = DateTime.DaysInMonth(dates.ToLocal(previousMonthStart).Year, dates.ToLocal(previousMonthStart).Month);
        var previousDailyAverage = previousTotal / daysInPreviousMonth;
        if (previousDailyAverage <= 0m)
        {
            return null;
        }

        var currentDailyAverage = currentTotal / daysElapsed;
        var ratio = currentDailyAverage / previousDailyAverage;
        if (ratio < 1.3m)
        {
            return null;
        }

        var relevance = ComputeRelevance(
            financialImpact: Math.Min(30m, currentTotal / Math.Max(previousTotal, 1m) * 10m),
            anomalyStrength: Math.Min(30m, (ratio - 1m) * 30m),
            urgency: 12m,
            confidence: 10m,
            repetitionPenalty: 0m);

        return FinancialPulse.Create(
            userId,
            PulseType.SpendingPace,
            PulseSeverity.Attention,
            "Vas gastando más rápido de lo habitual",
            $"Llevas {Money(currentTotal)} en {daysElapsed} días -- un ritmo diario {(ratio - 1m) * 100m:0}% más alto que el mes pasado.",
            $"Tu promedio diario este mes es {Money(currentDailyAverage)}; el mes pasado fue {Money(previousDailyAverage)}. Esto no es una proyección de cierre, solo tu ritmo actual.",
            dedupKey: $"spending_pace:{monthStart:yyyy-MM}",
            relevanceScore: relevance,
            periodStart: monthStart,
            periodEnd: now,
            occurredAt: now,
            now: now,
            value: currentDailyAverage,
            comparisonValue: previousDailyAverage,
            percentChange: MoneyMath.PercentChange(previousDailyAverage, currentDailyAverage));
    }

    // ---------------------------------------------------------------------
    // SPENDING_IMPROVEMENT: the mirror image of SPENDING_PACE, framed
    // positively and never shaming a "bad" month -- it only ever reports a
    // good one.
    // ---------------------------------------------------------------------
    private FinancialPulse? DetectSpendingImprovement(Guid userId, List<Movement> movements, StatementDateInterpreter dates, DateTimeOffset now)
    {
        var localNow = dates.ToLocal(now);
        if (localNow.Day < 10)
        {
            return null;
        }

        var monthStart = dates.StartOfMonth(now);
        var previousMonthStart = dates.StartOfPreviousMonth(now);
        var daysElapsed = Math.Max(1, (localNow.Date - dates.ToLocal(monthStart).Date).Days + 1);

        var currentTotal = MoneyMath.Round(movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date >= monthStart)
            .Sum(m => m.Amount));

        var previousExpenses = movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date >= previousMonthStart && m.Date < monthStart)
            .ToList();

        if (previousExpenses.Count == 0 || currentTotal <= 0m)
        {
            return null;
        }

        var previousTotal = MoneyMath.Round(previousExpenses.Sum(m => m.Amount));
        var daysInPreviousMonth = DateTime.DaysInMonth(dates.ToLocal(previousMonthStart).Year, dates.ToLocal(previousMonthStart).Month);
        var previousDailyAverage = previousTotal / daysInPreviousMonth;
        if (previousDailyAverage <= 0m)
        {
            return null;
        }

        var currentDailyAverage = currentTotal / daysElapsed;
        var ratio = currentDailyAverage / previousDailyAverage;
        if (ratio > 0.7m)
        {
            return null;
        }

        var savedPercent = (1m - ratio) * 100m;
        var relevance = ComputeRelevance(
            financialImpact: Math.Min(25m, savedPercent / 3m),
            anomalyStrength: Math.Min(20m, savedPercent / 4m),
            urgency: 5m,
            confidence: 10m,
            repetitionPenalty: 0m);

        return FinancialPulse.Create(
            userId,
            PulseType.SpendingImprovement,
            PulseSeverity.Positive,
            "Vas gastando menos que de costumbre",
            $"Llevas {Money(currentTotal)} en {daysElapsed} días -- unos {savedPercent:0}% menos de ritmo diario que el mes pasado.",
            $"Tu promedio diario este mes es {Money(currentDailyAverage)}; el mes pasado fue {Money(previousDailyAverage)}.",
            dedupKey: $"spending_improvement:{monthStart:yyyy-MM}",
            relevanceScore: relevance,
            periodStart: monthStart,
            periodEnd: now,
            occurredAt: now,
            now: now,
            value: currentDailyAverage,
            comparisonValue: previousDailyAverage,
            percentChange: MoneyMath.PercentChange(previousDailyAverage, currentDailyAverage));
    }

    // ---------------------------------------------------------------------
    // DAILY_CLOSE (FASE 4): a summary of yesterday's activity. Skipped
    // entirely on a "quiet day" (zero movements) so this never becomes a
    // nightly ping about nothing. Severity reflects the day itself rather
    // than a fixed rule: a day that ended with more money in than out is
    // Positive; a day whose spending was clearly above the user's own recent
    // daily rate is Attention; an ordinary spending day is Neutral -- which,
    // per IPulseNotificationDecisionService's severity gate, means it is
    // recorded in history but never pushes. A push for every uneventful day
    // would be exactly the "genérico" nagging PULSO exists to avoid.
    // ---------------------------------------------------------------------
    private FinancialPulse? DetectDailyClose(Guid userId, List<Movement> movements, StatementDateInterpreter dates, DateTimeOffset now)
    {
        var today = dates.ToLocalDate(now);
        var yesterday = today.AddDays(-1);
        var startOfYesterday = dates.StartOfDay(yesterday);
        var startOfToday = dates.StartOfDay(today);

        var yesterdaysMovements = movements
            .Where(m => m.Date >= startOfYesterday && m.Date < startOfToday)
            .ToList();

        // Quiet day: nothing happened, nothing to close.
        if (yesterdaysMovements.Count == 0)
        {
            return null;
        }

        var spent = MoneyMath.Round(yesterdaysMovements.Where(m => m.Direction == TransactionDirection.Expense).Sum(m => m.Amount));
        var received = MoneyMath.Round(yesterdaysMovements.Where(m => m.Direction == TransactionDirection.Income).Sum(m => m.Amount));
        var net = MoneyMath.Round(received - spent);
        var movementWord = yesterdaysMovements.Count == 1 ? "movimiento" : "movimientos";

        // A recent baseline (30 days before yesterday, at least 10 of them
        // with expenses) decides whether yesterday's spending was itself
        // notable -- same "compare to the user's own history, never a fixed
        // dollar amount" rule every other rule in this file follows.
        var baselineStart = startOfYesterday.AddDays(-30);
        var baselineExpenses = movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date >= baselineStart && m.Date < startOfYesterday)
            .ToList();
        var baselineDays = baselineExpenses.Select(m => dates.ToLocalDate(m.Date)).Distinct().Count();
        var dailyAverage = baselineDays >= 10 ? MoneyMath.Round(baselineExpenses.Sum(m => m.Amount) / 30m) : (decimal?)null;

        string title;
        string body;
        PulseSeverity severity;
        decimal relevance;

        if (net > 0m)
        {
            severity = PulseSeverity.Positive;
            title = "Cerraste el día en positivo";
            body = spent > 0m
                ? $"Recibiste {Money(received)} y gastaste {Money(spent)} -- un neto de +{Money(net)}."
                : $"Recibiste {Money(received)}, sin gastos registrados ayer.";
            relevance = ComputeRelevance(
                financialImpact: Math.Min(25m, net / 10m),
                anomalyStrength: 5m,
                urgency: 5m,
                confidence: 10m,
                repetitionPenalty: 0m);
        }
        else if (dailyAverage is > 0m && spent >= dailyAverage.Value * 2m && spent >= 20m)
        {
            var ratio = spent / dailyAverage.Value;
            severity = PulseSeverity.Attention;
            title = "Ayer gastaste más de lo habitual";
            body = $"Gastaste {Money(spent)} en {yesterdaysMovements.Count} {movementWord} -- unas {ratio:0.#}x tu día típico ({Money(dailyAverage.Value)}).";
            relevance = ComputeRelevance(
                financialImpact: Math.Min(25m, (ratio - 1m) * 10m),
                anomalyStrength: Math.Min(20m, (ratio - 1m) * 8m),
                urgency: 8m,
                confidence: 10m,
                repetitionPenalty: 0m);
        }
        else
        {
            severity = PulseSeverity.Neutral;
            title = "Cierre de ayer";
            body = $"Gastaste {Money(spent)} en {yesterdaysMovements.Count} {movementWord}.";
            relevance = ComputeRelevance(
                financialImpact: 5m,
                anomalyStrength: 0m,
                urgency: 2m,
                confidence: 8m,
                repetitionPenalty: 0m);
        }

        var explanation = dailyAverage is null
            ? "Resumimos los movimientos posteados o pendientes de ayer, en tu zona horaria. Todavía no hay suficiente historial para comparar contra un día típico."
            : $"Resumimos los movimientos posteados o pendientes de ayer, en tu zona horaria, y los comparamos contra tu gasto diario promedio de los últimos 30 días ({Money(dailyAverage.Value)}).";

        return FinancialPulse.Create(
            userId,
            PulseType.DailyClose,
            severity,
            title,
            body,
            explanation,
            dedupKey: $"daily_close:{yesterday:yyyy-MM-dd}",
            relevanceScore: relevance,
            periodStart: startOfYesterday,
            periodEnd: startOfToday,
            occurredAt: startOfToday,
            now: now,
            value: spent,
            comparisonValue: dailyAverage,
            percentChange: dailyAverage is > 0m ? MoneyMath.PercentChange(dailyAverage.Value, spent) : null);
    }

    // ---------------------------------------------------------------------
    // MONTH_END_PROJECTION (FASE 4): "a este ritmo, así cerrarías el mes".
    // The third anti-noise shape (see class remarks) -- fires the first
    // projection of the month unconditionally, then only fires again once at
    // least a day has passed AND the estimate has moved by 15% or more since
    // the last one it actually sent. Severity compares the projection
    // against what the user actually spent last month, the same real
    // baseline SpendingPace/SpendingImprovement use, at the same ±30%
    // thresholds those rules already established.
    // ---------------------------------------------------------------------
    private async Task<FinancialPulse?> DetectMonthEndProjectionAsync(
        Guid userId,
        List<Movement> movements,
        StatementDateInterpreter dates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var localNow = dates.ToLocal(now);
        // Same "too early in the month to mean anything" floor SpendingPace/SpendingImprovement use.
        if (localNow.Day < 10)
        {
            return null;
        }

        var monthStart = dates.StartOfMonth(now);
        var previousMonthStart = dates.StartOfPreviousMonth(now);
        var localMonthStart = dates.ToLocal(monthStart);
        var daysElapsed = Math.Max(1, (localNow.Date - localMonthStart.Date).Days + 1);
        var daysInMonth = DateTime.DaysInMonth(localMonthStart.Year, localMonthStart.Month);

        var currentTotal = MoneyMath.Round(movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date >= monthStart)
            .Sum(m => m.Amount));

        if (currentTotal <= 0m)
        {
            return null;
        }

        var dailyAverage = currentTotal / daysElapsed;
        var projectedTotal = MoneyMath.Round(dailyAverage * daysInMonth);

        var lastProjection = await db.Pulses
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Type == PulseType.MonthEndProjection && p.PeriodStart == monthStart)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastProjection is not null)
        {
            // Cooldown: never more than one update per day no matter how the
            // math moves in between -- a projection should feel like a daily
            // check-in at most, never a live ticker.
            if (lastProjection.CreatedAt >= now.AddDays(-1))
            {
                return null;
            }

            var changeSinceLast = MoneyMath.PercentChange(lastProjection.Value ?? 0m, projectedTotal);
            if (changeSinceLast is null || Math.Abs(changeSinceLast.Value) < 15m)
            {
                return null;
            }
        }

        var previousMonthTotal = MoneyMath.Round(movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date >= previousMonthStart && m.Date < monthStart)
            .Sum(m => m.Amount));

        var percentVsPreviousMonth = previousMonthTotal > 0m ? MoneyMath.PercentChange(previousMonthTotal, projectedTotal) : null;
        var severity = percentVsPreviousMonth switch
        {
            >= 30m => PulseSeverity.Attention,
            <= -30m => PulseSeverity.Positive,
            _ => PulseSeverity.Neutral,
        };

        var comparisonPhrase = percentVsPreviousMonth is null
            ? "Todavía no tenemos un mes anterior completo para comparar."
            : $"Eso es {Math.Abs(percentVsPreviousMonth.Value):0}% {(percentVsPreviousMonth >= 0m ? "más" : "menos")} que lo que gastaste el mes pasado ({Money(previousMonthTotal)}).";

        var relevance = ComputeRelevance(
            financialImpact: Math.Min(25m, projectedTotal / Math.Max(previousMonthTotal, 1m) * 8m),
            anomalyStrength: Math.Min(20m, Math.Abs(percentVsPreviousMonth ?? 0m) / 3m),
            urgency: severity == PulseSeverity.Attention ? 12m : 5m,
            confidence: Math.Min(15m, daysElapsed / 2m),
            repetitionPenalty: 0m);

        return FinancialPulse.Create(
            userId,
            PulseType.MonthEndProjection,
            severity,
            "Proyección de cierre de mes",
            $"A este ritmo, cerrarías el mes en {Money(projectedTotal)}. {comparisonPhrase}",
            $"Multiplicamos tu gasto promedio diario de este mes ({Money(dailyAverage)}) por los {daysInMonth} días del mes. Solo te avisamos de nuevo si esta proyección cambia 15% o más desde la última vez que te avisamos.",
            dedupKey: $"month_end_projection:{monthStart:yyyy-MM}:{now:yyyyMMddHHmmssfff}",
            relevanceScore: relevance,
            periodStart: monthStart,
            periodEnd: now,
            occurredAt: now,
            now: now,
            value: projectedTotal,
            comparisonValue: previousMonthTotal > 0m ? previousMonthTotal : null,
            percentChange: percentVsPreviousMonth);
    }

    // ---------------------------------------------------------------------
    // ACCOUNT_OUTDATED: same threshold InsightEngine's STALE_ACCOUNT uses
    // (AccountStaleness.ThresholdDays), but as a cooldown-based proactive
    // pulse instead of an always-on dashboard card.
    // ---------------------------------------------------------------------
    private FinancialPulse? DetectAccountOutdated(Guid userId, FinancialAccount account, DateTimeOffset now)
    {
        if (account.ConnectionMode != ConnectionMode.ManualImport)
        {
            return null;
        }

        var staleThreshold = now.AddDays(-AccountStaleness.ThresholdDays);
        if (account.LastSyncedAt is not null && account.LastSyncedAt >= staleThreshold)
        {
            return null;
        }

        var daysSince = account.LastSyncedAt is null ? (int?)null : (int)(now - account.LastSyncedAt.Value).TotalDays;

        var relevance = ComputeRelevance(
            financialImpact: 5m,
            anomalyStrength: 5m,
            urgency: Math.Min(15m, (daysSince ?? 30) / 3m),
            confidence: 15m,
            repetitionPenalty: 0m);

        return FinancialPulse.Create(
            userId,
            PulseType.AccountOutdated,
            PulseSeverity.Neutral,
            "Cuenta por actualizar",
            daysSince is null
                ? $"{account.Alias} todavía no tiene movimientos importados."
                : $"{account.Alias} no se actualiza hace {daysSince} días.",
            "Esta cuenta se administra por importación manual, así que su saldo y sus insights se quedan desactualizados hasta que subas un nuevo extracto.",
            dedupKey: $"account_outdated:{account.Id}",
            relevanceScore: relevance,
            periodStart: account.LastSyncedAt ?? account.CreatedAt,
            periodEnd: now,
            occurredAt: now,
            now: now,
            referenceId: account.Id.ToString());
    }

    // ---------------------------------------------------------------------
    // BALANCE_CHANGE: an account's estimated balance dropped sharply in a
    // short window. Reconstructed the same way AnalyticsService's balance
    // evolution chart is -- walk backwards from the current balance,
    // subtracting movements as they are passed -- scoped to a single
    // 7-day boundary instead of a full series.
    // ---------------------------------------------------------------------
    private FinancialPulse? DetectBalanceChange(Guid userId, FinancialAccount account, List<Movement> movements, DateTimeOffset now)
    {
        var boundary = now.AddDays(-7);
        var since = boundary.AddDays(-1);
        if (account.CreatedAt > boundary)
        {
            // Confidence: an account younger than the comparison window has no
            // "before" balance to compare against.
            return null;
        }

        var movementsSinceBoundary = movements
            .Where(m => m.FinancialAccountId == account.Id && m.Date > boundary)
            .ToList();

        var signedSum = movementsSinceBoundary.Sum(m => m.Direction == TransactionDirection.Income ? m.Amount : -m.Amount);
        var balanceAtBoundary = MoneyMath.Round(account.EstimatedBalance - signedSum);
        var currentBalance = MoneyMath.Round(account.EstimatedBalance);

        if (balanceAtBoundary <= 0m)
        {
            // A near-zero or negative starting balance makes a percent drop
            // meaningless; the absolute drop check below still applies.
            return null;
        }

        var drop = balanceAtBoundary - currentBalance;
        var dropPercent = drop / balanceAtBoundary * 100m;

        if (drop < 30m || dropPercent < 25m)
        {
            return null;
        }

        var severity = currentBalance < 0m || dropPercent >= 50m ? PulseSeverity.Risk : PulseSeverity.Attention;

        var relevance = ComputeRelevance(
            financialImpact: Math.Min(35m, dropPercent / 2m),
            anomalyStrength: Math.Min(25m, dropPercent / 3m),
            urgency: severity == PulseSeverity.Risk ? 20m : 12m,
            confidence: 10m,
            repetitionPenalty: 0m);

        return FinancialPulse.Create(
            userId,
            PulseType.BalanceChange,
            severity,
            $"El saldo de {account.Alias} bajó",
            $"Pasó de {Money(balanceAtBoundary)} a {Money(currentBalance)} en la última semana ({dropPercent:0}% menos).",
            $"Reconstruimos el saldo de hace 7 días restando los movimientos posteriores al saldo actual. La caída viene de la actividad reciente en esta cuenta.",
            dedupKey: $"balance_change:{account.Id}",
            relevanceScore: relevance,
            periodStart: since,
            periodEnd: now,
            occurredAt: now,
            now: now,
            value: currentBalance,
            comparisonValue: balanceAtBoundary,
            percentChange: -dropPercent,
            referenceId: account.Id.ToString());
    }

    /// <summary>
    /// financialImpact + anomalyStrength + urgency + confidence − repetitionPenalty,
    /// clamped to a stable 0-100 scale so relevance is comparable across every
    /// rule regardless of the units each component was computed from. Every
    /// component is a real, explainable number -- never a random one.
    /// </summary>
    private static decimal ComputeRelevance(
        decimal financialImpact,
        decimal anomalyStrength,
        decimal urgency,
        decimal confidence,
        decimal repetitionPenalty) =>
        Math.Clamp(financialImpact + anomalyStrength + urgency + confidence - repetitionPenalty, 0m, 100m);

    private static string Money(decimal amount) => $"${amount:N2}";
}
