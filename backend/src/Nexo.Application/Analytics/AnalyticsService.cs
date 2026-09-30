using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Application.Transactions;
using Nexo.Domain.Accounts;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Analytics;

public interface IAnalyticsService
{
    Task<AnalyticsDashboardDto> GetDashboardAsync(Guid userId, AnalyticsQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Backs the "Dashboard de estadísticas" screen. Every figure here comes from
/// transactions and account balances that already exist -- nothing is
/// projected or forecast. Two derived-but-real ideas do the heavy lifting:
/// <list type="bullet">
/// <item>"Saldo histórico" is reconstructed by walking backwards from each
/// account's current <see cref="FinancialAccount.EstimatedBalance"/>,
/// subtracting posted/pending movements as they're passed going back in time.
/// It is exactly as real as the current balance is (Estimated vs Verified)
/// -- never a dashed "proyección" line.</item>
/// <item>"Pagos recurrentes" / fijo vs. variable is not a category guess: a
/// merchant only counts as recurring once it has actually shown up in at
/// least two of the last six calendar months at a roughly stable amount.</item>
/// </list>
/// </summary>
public sealed class AnalyticsService(INexoDbContext db, IClock clock) : IAnalyticsService
{
    private const int RecurringLookbackMonths = RecurringPaymentDetector.LookbackMonths;

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-EC");

    public async Task<AnalyticsDashboardDto> GetDashboardAsync(
        Guid userId,
        AnalyticsQuery query,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new NotFoundException("User", userId);

        var dates = new StatementDateInterpreter(user.TimeZoneId);
        var now = clock.UtcNow;

        var (from, to, periodCode, label) = ResolvePeriod(query, dates, now);
        var previousFrom = from - (to - from);
        var previousTo = from;

        var accounts = await ResolveAccountsAsync(userId, query.AccountId, cancellationToken);
        var accountIds = accounts.Select(a => a.Id).ToArray();

        if (accountIds.Length == 0)
        {
            return Empty(periodCode, label, from, to);
        }

        var (income, expense) = await SumIncomeExpenseAsync(userId, accountIds, from, to, cancellationToken);
        var (previousIncome, previousExpense) = await SumIncomeExpenseAsync(userId, accountIds, previousFrom, previousTo, cancellationToken);

        var kpis = new AnalyticsKpisDto(
            MoneyMath.Round(income),
            MoneyMath.Round(expense),
            MoneyMath.Round(income - expense),
            SavingsRate(income, income - expense),
            MoneyMath.PercentChange(previousIncome, income),
            MoneyMath.PercentChange(previousExpense, expense),
            MoneyMath.PercentChange(previousIncome - previousExpense, income - expense));

        // Real recurrence, looked up over a fixed 6-month window regardless of the
        // selected period -- a single month of history is not enough to tell a
        // subscription from a one-off purchase.
        var lookbackStart = dates.StartOfMonth(now).AddMonths(-(RecurringLookbackMonths - 1));
        var recurringMerchants = await DetectRecurringMerchantsAsync(userId, accountIds, lookbackStart, now, dates, cancellationToken);

        var moneyFlow = await BuildMoneyFlowAsync(userId, accountIds, from, to, income, recurringMerchants, cancellationToken);

        var buckets = BuildBuckets(from, to, dates);
        var series = await BuildSeriesAsync(userId, accountIds, buckets, cancellationToken);
        var balanceEvolution = await BuildBalanceEvolutionAsync(userId, accountIds, accounts, buckets, now, cancellationToken);

        var categoryBreakdown = await BuildCategoryTrendAsync(userId, accountIds, from, to, previousFrom, previousTo, cancellationToken);
        var spotlight = PickSpotlight(categoryBreakdown);

        var topMerchants = await BuildTopMerchantsAsync(userId, accountIds, from, to, cancellationToken);
        var peakDays = await BuildPeakSpendingDaysAsync(userId, accountIds, from, to, dates, cancellationToken);
        var weekdayWeekend = await BuildWeekdayWeekendAsync(userId, accountIds, from, to, dates, cancellationToken);
        var monthlyHistory = await BuildMonthlyHistoryAsync(userId, accountIds, dates, now, cancellationToken);

        var insights = await db.Insights
            .AsNoTracking()
            .Where(i => i.UserId == userId && i.ValidUntil > now)
            .OrderBy(i => i.DisplayOrder)
            .Take(6)
            .Select(i => new Insights.InsightDto(
                i.Code, i.Title, i.Body, i.Value, i.ComparisonValue, i.PercentChange,
                i.Severity.ToString(), i.ReferenceId, i.PeriodStart, i.PeriodEnd, i.ValidUntil))
            .ToListAsync(cancellationToken);

        return new AnalyticsDashboardDto(
            new AnalyticsPeriodDto(periodCode, label, from, to),
            kpis,
            moneyFlow,
            series,
            balanceEvolution,
            categoryBreakdown,
            spotlight,
            recurringMerchants
                .OrderByDescending(m => m.LastSeenAt)
                .Select(m => new RecurringPaymentDto(m.Merchant, m.CategoryName, MoneyMath.Round(m.AverageAmount), m.Occurrences, m.LastSeenAt))
                .ToList(),
            topMerchants,
            peakDays,
            weekdayWeekend,
            monthlyHistory,
            insights);
    }

    private static AnalyticsDashboardDto Empty(string periodCode, string label, DateTimeOffset from, DateTimeOffset to) =>
        new(
            new AnalyticsPeriodDto(periodCode, label, from, to),
            new AnalyticsKpisDto(0m, 0m, 0m, null, null, null, null),
            new MoneyFlowDto(0m, 0m, 0m, 0m, null),
            [],
            [],
            [],
            null,
            [],
            [],
            [],
            new WeekdayWeekendDto(0m, 0m, 0m, 0m),
            [],
            []);

    private static decimal? SavingsRate(decimal income, decimal net) =>
        income == 0m ? null : Math.Round(net / income * 100m, 1, MidpointRounding.AwayFromZero);

    private (DateTimeOffset From, DateTimeOffset To, string Code, string Label) ResolvePeriod(
        AnalyticsQuery query,
        StatementDateInterpreter dates,
        DateTimeOffset now)
    {
        switch (query.Period)
        {
            case AnalyticsPeriod.LastMonth:
            {
                var from = dates.StartOfPreviousMonth(now);
                var to = dates.StartOfMonth(now);
                var label = Capitalize(dates.ToLocal(from).ToString("MMMM yyyy", Spanish));
                return (from, to, "last_month", label);
            }
            case AnalyticsPeriod.Last3Months:
            {
                var from = dates.StartOfMonth(now).AddMonths(-2);
                return (from, now, "last_3_months", "Últimos 3 meses");
            }
            case AnalyticsPeriod.Last6Months:
            {
                var from = dates.StartOfMonth(now).AddMonths(-5);
                return (from, now, "last_6_months", "Últimos 6 meses");
            }
            case AnalyticsPeriod.Year:
            {
                var local = dates.ToLocal(now);
                var from = dates.StartOfDay(new DateTime(local.Year, 1, 1));
                return (from, now, "year", local.Year.ToString(Spanish));
            }
            case AnalyticsPeriod.Custom:
            {
                if (query.From is not { } from)
                {
                    throw ValidationException.For(nameof(query.From), "Elige una fecha de inicio para el rango personalizado.");
                }

                var to = query.To ?? now;
                if (to > now)
                {
                    to = now;
                }

                if (from >= to)
                {
                    throw ValidationException.For(nameof(query.From), "La fecha de inicio debe ser anterior a la fecha final.");
                }

                return (from, to, "custom", "Personalizado");
            }
            default:
            {
                var from = dates.StartOfMonth(now);
                var label = Capitalize(dates.ToLocal(now).ToString("MMMM yyyy", Spanish));
                return (from, now, "month", label);
            }
        }
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpper(value[0], Spanish) + value[1..];

    private async Task<List<FinancialAccount>> ResolveAccountsAsync(Guid userId, Guid? accountId, CancellationToken cancellationToken)
    {
        if (accountId is { } id)
        {
            var account = await db.FinancialAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("FinancialAccount", id);
            return [account];
        }

        return await db.FinancialAccounts.AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsArchived)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Net income/expense, excluding confirmed internal transfers -- moving your own money is neither.</summary>
    private async Task<(decimal Income, decimal Expense)> SumIncomeExpenseAsync(
        Guid userId,
        IReadOnlyCollection<Guid> accountIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var query = ScopedTransactions(userId, accountIds, from, to)
            .Where(t => !t.IsInternalTransfer);

        var income = await query.Where(t => t.Direction == TransactionDirection.Income)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;
        var expense = await query.Where(t => t.Direction == TransactionDirection.Expense)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        return (income, expense);
    }

    private IQueryable<Transaction> ScopedTransactions(Guid userId, IReadOnlyCollection<Guid> accountIds, DateTimeOffset from, DateTimeOffset to) =>
        db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TransactionDate >= from
                        && t.TransactionDate <= to
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .WhereIdIn(t => t.FinancialAccountId, accountIds);

    private async Task<List<RecurringMerchant>> DetectRecurringMerchantsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> accountIds,
        DateTimeOffset from,
        DateTimeOffset to,
        StatementDateInterpreter dates,
        CancellationToken cancellationToken)
    {
        var rows = await ScopedTransactions(userId, accountIds, from, to)
            .Where(t => t.Direction == TransactionDirection.Expense && !t.IsInternalTransfer && t.Merchant != null)
            .Select(t => new RecurringCandidateRow(t.Merchant!, t.Amount, t.TransactionDate, t.CategoryId))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var categories = await LoadCategoryNamesAsync(userId, cancellationToken);

        // Shared with Comprometido ("Próximos pagos") so both screens agree on
        // what "recurrente" means.
        return RecurringPaymentDetector.Detect(rows, categories, instant =>
        {
            var local = dates.ToLocal(instant);
            return (local.Year, local.Month);
        });
    }

    private async Task<MoneyFlowDto> BuildMoneyFlowAsync(
        Guid userId,
        IReadOnlyCollection<Guid> accountIds,
        DateTimeOffset from,
        DateTimeOffset to,
        decimal income,
        List<RecurringMerchant> recurringMerchants,
        CancellationToken cancellationToken)
    {
        var recurringKeys = recurringMerchants.Select(m => m.Key).ToHashSet();

        var rows = await ScopedTransactions(userId, accountIds, from, to)
            .Where(t => t.Direction == TransactionDirection.Expense && !t.IsInternalTransfer)
            .Select(t => new { t.Merchant, t.Amount })
            .ToListAsync(cancellationToken);

        var fixedExpense = 0m;
        var variableExpense = 0m;
        foreach (var row in rows)
        {
            var key = string.IsNullOrWhiteSpace(row.Merchant) ? null : TextNormalizer.NormalizeForMatching(row.Merchant);
            if (key is not null && recurringKeys.Contains(key))
            {
                fixedExpense += row.Amount;
            }
            else
            {
                variableExpense += row.Amount;
            }
        }

        var net = income - fixedExpense - variableExpense;
        return new MoneyFlowDto(
            MoneyMath.Round(income),
            MoneyMath.Round(fixedExpense),
            MoneyMath.Round(variableExpense),
            MoneyMath.Round(net),
            SavingsRate(income, net));
    }

    private sealed record Bucket(DateTimeOffset From, DateTimeOffset To, string Label);

    /// <summary>
    /// Splits [from, to] into at most 8 roughly-equal buckets (never less than a
    /// day each) -- the same boundaries drive both the income/expense series and
    /// the balance-evolution points, so the two charts always line up.
    /// </summary>
    private List<Bucket> BuildBuckets(DateTimeOffset from, DateTimeOffset to, StatementDateInterpreter dates)
    {
        const int targetBuckets = 8;
        var totalDays = Math.Max(1, (to - from).TotalDays);
        var bucketDays = Math.Max(1, (int)Math.Ceiling(totalDays / targetBuckets));

        var buckets = new List<Bucket>();
        var cursor = from;
        while (cursor < to)
        {
            var end = cursor.AddDays(bucketDays);
            if (end > to)
            {
                end = to;
            }

            var label = dates.ToLocal(cursor).ToString("d MMM", Spanish);
            buckets.Add(new Bucket(cursor, end, Capitalize(label)));
            cursor = end;
        }

        return buckets;
    }

    private async Task<List<SeriesPointDto>> BuildSeriesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> accountIds,
        List<Bucket> buckets,
        CancellationToken cancellationToken)
    {
        var result = new List<SeriesPointDto>();
        foreach (var bucket in buckets)
        {
            var (income, expense) = await SumIncomeExpenseAsync(userId, accountIds, bucket.From, bucket.To, cancellationToken);
            result.Add(new SeriesPointDto(bucket.From, bucket.To, bucket.Label, MoneyMath.Round(income), MoneyMath.Round(expense)));
        }

        return result;
    }

    /// <summary>
    /// Reconstructs the account(s)' combined balance at the end of every bucket by
    /// walking backwards from the current total: subtract each posted/pending
    /// movement (internal transfers included -- they really do move the balance)
    /// as it is passed going back in time. Only ever produces points at or before
    /// "now"; there is deliberately no forward-looking point.
    /// </summary>
    private async Task<List<BalancePointDto>> BuildBalanceEvolutionAsync(
        Guid userId,
        IReadOnlyCollection<Guid> accountIds,
        List<FinancialAccount> accounts,
        List<Bucket> buckets,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (buckets.Count == 0)
        {
            return [];
        }

        var currentBalance = accounts.Sum(a => a.EstimatedBalance);

        var movements = await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TransactionDate <= now
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .WhereIdIn(t => t.FinancialAccountId, accountIds)
            .Select(t => new { t.TransactionDate, t.Amount, t.Direction })
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync(cancellationToken);

        // Movements are sorted newest-first; walking the boundaries newest-first too
        // (then reversing the result) lets a single forward pass over both lists
        // accumulate correctly -- each boundary only ever consumes movements older
        // than the one before it.
        var boundariesDescending = buckets.Select(b => (b.To, b.Label)).OrderByDescending(b => b.To).ToList();
        var running = currentBalance;
        var index = 0;
        var pointsDescending = new List<BalancePointDto>();

        foreach (var (boundary, label) in boundariesDescending)
        {
            while (index < movements.Count && movements[index].TransactionDate > boundary)
            {
                var signed = movements[index].Direction == TransactionDirection.Income ? movements[index].Amount : -movements[index].Amount;
                running -= signed;
                index++;
            }

            pointsDescending.Add(new BalancePointDto(boundary, label, MoneyMath.Round(running)));
        }

        pointsDescending.Reverse();
        return pointsDescending;
    }

    private async Task<Dictionary<Guid, string>> LoadCategoryNamesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .ToListAsync(cancellationToken);
        return categories.ToDictionary(c => c.Id, c => c.Name);
    }

    private async Task<List<CategoryTrendDto>> BuildCategoryTrendAsync(
        Guid userId,
        IReadOnlyCollection<Guid> accountIds,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset previousFrom,
        DateTimeOffset previousTo,
        CancellationToken cancellationToken)
    {
        var current = await GroupByCategoryAsync(userId, accountIds, from, to, cancellationToken);
        var previous = await GroupByCategoryAsync(userId, accountIds, previousFrom, previousTo, cancellationToken);

        if (current.Count == 0)
        {
            return [];
        }

        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var total = current.Sum(g => g.Total);
        var previousByCategory = previous.ToDictionary(g => g.CategoryId, g => g.Total);

        return current
            .OrderByDescending(g => g.Total)
            .Select(g =>
            {
                categories.TryGetValue(g.CategoryId, out var category);
                var previousTotal = previousByCategory.TryGetValue(g.CategoryId, out var prev) ? prev : (decimal?)null;
                return new CategoryTrendDto(
                    g.CategoryId,
                    category?.Name ?? "Sin categoría",
                    category?.Icon ?? "circle",
                    category?.Color ?? "#ECE9E1",
                    MoneyMath.Round(g.Total),
                    total == 0m ? 0m : Math.Round(g.Total / total * 100m, 1),
                    g.Count,
                    previousTotal is null ? null : MoneyMath.Round(previousTotal.Value),
                    previousTotal is null ? null : MoneyMath.PercentChange(previousTotal.Value, g.Total));
            })
            .ToList();
    }

    private sealed record CategoryTotal(Guid CategoryId, decimal Total, int Count);

    private async Task<List<CategoryTotal>> GroupByCategoryAsync(
        Guid userId,
        IReadOnlyCollection<Guid> accountIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var grouped = await ScopedTransactions(userId, accountIds, from, to)
            .Where(t => t.Direction == TransactionDirection.Expense && !t.IsInternalTransfer)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(cancellationToken);

        return grouped.Select(g => new CategoryTotal(g.CategoryId ?? Guid.Empty, g.Total, g.Count)).ToList();
    }

    /// <summary>
    /// The category with the biggest swing vs. the previous period, ignoring
    /// categories too small to matter (under 5% of the period's spend) and ones
    /// with nothing to compare against yet -- a brand-new category "growing"
    /// from $0 is not an interesting spotlight, it is just noise.
    /// </summary>
    private static Guid? PickSpotlight(List<CategoryTrendDto> categories)
    {
        var total = categories.Sum(c => c.Total);
        if (total <= 0m)
        {
            return null;
        }

        return categories
            .Where(c => c.PreviousTotal is > 0m && c.Total >= total * 0.05m)
            .OrderByDescending(c => Math.Abs(c.ChangePercent ?? 0m))
            .Select(c => (Guid?)c.CategoryId)
            .FirstOrDefault();
    }

    private async Task<List<MerchantRankingDto>> BuildTopMerchantsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> accountIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var rows = await ScopedTransactions(userId, accountIds, from, to)
            .Where(t => t.Direction == TransactionDirection.Expense && !t.IsInternalTransfer && t.Merchant != null)
            .Select(t => new { t.Merchant, t.Amount })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Merchant))
            .GroupBy(r => r.Merchant!)
            .Select(g => new MerchantRankingDto(g.Key, MoneyMath.Round(g.Sum(r => r.Amount)), g.Count()))
            .OrderByDescending(m => m.Total)
            .Take(6)
            .ToList();
    }

    private async Task<List<DailySpendDto>> BuildPeakSpendingDaysAsync(
        Guid userId,
        IReadOnlyCollection<Guid> accountIds,
        DateTimeOffset from,
        DateTimeOffset to,
        StatementDateInterpreter dates,
        CancellationToken cancellationToken)
    {
        var rows = await ScopedTransactions(userId, accountIds, from, to)
            .Where(t => t.Direction == TransactionDirection.Expense && !t.IsInternalTransfer)
            .Select(t => new { t.TransactionDate, t.Amount })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => dates.ToLocalDate(r.TransactionDate))
            .Select(g => new DailySpendDto(dates.StartOfDay(g.Key), MoneyMath.Round(g.Sum(r => r.Amount))))
            .OrderByDescending(d => d.Total)
            .Take(5)
            .ToList();
    }

    private async Task<WeekdayWeekendDto> BuildWeekdayWeekendAsync(
        Guid userId,
        IReadOnlyCollection<Guid> accountIds,
        DateTimeOffset from,
        DateTimeOffset to,
        StatementDateInterpreter dates,
        CancellationToken cancellationToken)
    {
        var rows = await ScopedTransactions(userId, accountIds, from, to)
            .Where(t => t.Direction == TransactionDirection.Expense && !t.IsInternalTransfer)
            .Select(t => new { t.TransactionDate, t.Amount })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new WeekdayWeekendDto(0m, 0m, 0m, 0m);
        }

        var byDay = rows
            .Select(r => new { Local = dates.ToLocalDate(r.TransactionDate), r.Amount })
            .ToList();

        var weekdayTotal = byDay.Where(r => r.Local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Sum(r => r.Amount);
        var weekendTotal = byDay.Where(r => r.Local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday).Sum(r => r.Amount);

        var weekdayCount = Math.Max(1, byDay.Select(r => r.Local).Distinct().Count(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)));
        var weekendCount = Math.Max(1, byDay.Select(r => r.Local).Distinct().Count(d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));

        return new WeekdayWeekendDto(
            MoneyMath.Round(weekdayTotal),
            MoneyMath.Round(weekdayTotal / weekdayCount),
            MoneyMath.Round(weekendTotal),
            MoneyMath.Round(weekendTotal / weekendCount));
    }

    /// <summary>Always the last six calendar months, independent of the selected period -- a short, stable "tabla de comparación".</summary>
    private async Task<List<MonthlyHistoryDto>> BuildMonthlyHistoryAsync(
        Guid userId,
        IReadOnlyCollection<Guid> accountIds,
        StatementDateInterpreter dates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var result = new List<MonthlyHistoryDto>();
        var monthStart = dates.StartOfMonth(now);

        for (var i = 5; i >= 0; i--)
        {
            var start = monthStart.AddMonths(-i);
            var end = start.AddMonths(1) > now && i == 0 ? now : start.AddMonths(1);
            var (income, expense) = await SumIncomeExpenseAsync(userId, accountIds, start, end, cancellationToken);
            var label = Capitalize(dates.ToLocal(start).ToString("MMM", Spanish));
            result.Add(new MonthlyHistoryDto(label, MoneyMath.Round(income), MoneyMath.Round(expense), MoneyMath.Round(income - expense)));
        }

        return result;
    }
}
