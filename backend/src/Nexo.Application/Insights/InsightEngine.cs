using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Common;
using Nexo.Domain.Insights;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Insights;

public interface IInsightEngine
{
    /// <summary>Recomputes and replaces the user's insight set. Idempotent by design.</summary>
    Task<IReadOnlyList<Insight>> RecomputeAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// A rules engine, not a model. Every insight is a small, named computation the
/// user could reproduce by hand — which is exactly what makes it trustworthy for a
/// finance app. The interface leaves room for a model-backed implementation later
/// without any caller changing (roadmap phase 3).
/// </summary>
public sealed class InsightEngine(INexoDbContext db, IClock clock) : IInsightEngine
{
    private const int LookbackDays = 120;
    private const int StaleAccountDays = 7;

    private sealed record Movement(
        DateTimeOffset Date,
        decimal Amount,
        TransactionDirection Direction,
        Guid? CategoryId,
        string NormalizedDescription,
        string? Merchant);

    public async Task<IReadOnlyList<Insight>> RecomputeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return [];
        }

        var dates = new StatementDateInterpreter(user.TimeZoneId);
        var now = clock.UtcNow;
        var currentMonthStart = dates.StartOfMonth(now);
        var previousMonthStart = dates.StartOfPreviousMonth(now);
        var since = now.AddDays(-LookbackDays);

        var movements = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TransactionDate >= since
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .Select(t => new Movement(
                t.TransactionDate,
                t.Amount,
                t.Direction,
                t.CategoryId,
                t.NormalizedDescription,
                t.Merchant))
            .ToListAsync(cancellationToken);

        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);
        var categoryNames = categories.ToDictionary(c => c.Id, c => c.Name);

        var insights = new List<Insight>();
        var order = 0;

        var currentExpenses = movements
            .Where(m => m.Direction == TransactionDirection.Expense && m.Date >= currentMonthStart)
            .ToList();
        var previousExpenses = movements
            .Where(m => m.Direction == TransactionDirection.Expense
                        && m.Date >= previousMonthStart
                        && m.Date < currentMonthStart)
            .ToList();
        var currentIncome = movements
            .Where(m => m.Direction == TransactionDirection.Income && m.Date >= currentMonthStart)
            .ToList();

        var currentTotal = MoneyMath.Round(currentExpenses.Sum(m => m.Amount));
        var previousTotal = MoneyMath.Round(previousExpenses.Sum(m => m.Amount));
        var incomeTotal = MoneyMath.Round(currentIncome.Sum(m => m.Amount));

        if (currentExpenses.Count > 0)
        {
            insights.Add(Insight.Create(
                userId,
                InsightCodes.MonthlySpend,
                currentMonthStart,
                now,
                "Gasto de este mes",
                $"Llevas {Money(currentTotal)} gastados este mes en {currentExpenses.Count} movimientos.",
                now,
                value: currentTotal,
                displayOrder: order++));
        }

        if (previousExpenses.Count > 0 && currentExpenses.Count > 0)
        {
            var delta = MoneyMath.Round(currentTotal - previousTotal);
            var percent = MoneyMath.PercentChange(previousTotal, currentTotal);
            var cheaper = delta < 0;

            insights.Add(Insight.Create(
                userId,
                InsightCodes.MonthOverMonth,
                currentMonthStart,
                now,
                cheaper ? "Vas gastando menos" : "Vas gastando más",
                cheaper
                    ? $"Gastaste {Money(Math.Abs(delta))} menos que el mes pasado."
                    : $"Gastaste {Money(Math.Abs(delta))} más que el mes pasado.",
                now,
                value: currentTotal,
                comparisonValue: previousTotal,
                percentChange: percent,
                severity: cheaper ? InsightSeverity.Positive : InsightSeverity.Attention,
                displayOrder: order++));
        }

        var topCategory = currentExpenses
            .Where(m => m.CategoryId is not null)
            .GroupBy(m => m.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Total = MoneyMath.Round(g.Sum(x => x.Amount)) })
            .OrderByDescending(g => g.Total)
            .FirstOrDefault();

        if (topCategory is not null && categoryNames.TryGetValue(topCategory.CategoryId, out var topName))
        {
            var share = currentTotal == 0m ? 0m : Math.Round(topCategory.Total / currentTotal * 100m, 0);
            insights.Add(Insight.Create(
                userId,
                InsightCodes.TopCategory,
                currentMonthStart,
                now,
                $"Tu mayor gasto: {topName}",
                $"{topName} representa el {share:0}% de lo que gastaste este mes ({Money(topCategory.Total)}).",
                now,
                value: topCategory.Total,
                referenceId: topCategory.CategoryId.ToString(),
                displayOrder: order++));

            var previousCategoryTotal = MoneyMath.Round(
                previousExpenses.Where(m => m.CategoryId == topCategory.CategoryId).Sum(m => m.Amount));

            var categoryPercent = MoneyMath.PercentChange(previousCategoryTotal, topCategory.Total);
            if (previousCategoryTotal > 0m && categoryPercent is not null && Math.Abs(categoryPercent.Value) >= 10m)
            {
                var up = categoryPercent.Value > 0;
                insights.Add(Insight.Create(
                    userId,
                    InsightCodes.CategoryChange,
                    currentMonthStart,
                    now,
                    up ? $"Más gasto en {topName}" : $"Menos gasto en {topName}",
                    $"Gastaste {Math.Abs(categoryPercent.Value):0}% {(up ? "más" : "menos")} en {topName} este mes.",
                    now,
                    value: topCategory.Total,
                    comparisonValue: previousCategoryTotal,
                    percentChange: categoryPercent,
                    severity: up ? InsightSeverity.Attention : InsightSeverity.Positive,
                    referenceId: topCategory.CategoryId.ToString(),
                    displayOrder: order++));
            }
        }

        var subscription = DetectRecurring(movements, now);
        if (subscription is not null)
        {
            insights.Add(Insight.Create(
                userId,
                InsightCodes.RecurringSubscription,
                since,
                now,
                "Pago recurrente detectado",
                $"{subscription.Label} se repite cada mes por alrededor de {Money(subscription.TypicalAmount)}.",
                now,
                value: subscription.TypicalAmount,
                displayOrder: order++));
        }

        var frequent = currentExpenses
            .Where(m => !string.IsNullOrWhiteSpace(m.Merchant))
            .GroupBy(m => m.Merchant!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Merchant = g.Key, Count = g.Count(), Total = MoneyMath.Round(g.Sum(x => x.Amount)) })
            .Where(g => g.Count >= 3)
            .OrderByDescending(g => g.Count)
            .FirstOrDefault();

        if (frequent is not null)
        {
            insights.Add(Insight.Create(
                userId,
                InsightCodes.FrequentMerchant,
                currentMonthStart,
                now,
                $"Compras frecuentes en {frequent.Merchant}",
                $"Fuiste {frequent.Count} veces este mes y llevas {Money(frequent.Total)} ahí.",
                now,
                value: frequent.Total,
                displayOrder: order++));
        }

        var highestDay = currentExpenses
            .GroupBy(m => dates.ToLocalDate(m.Date))
            .Select(g => new { Day = g.Key, Total = MoneyMath.Round(g.Sum(x => x.Amount)) })
            .OrderByDescending(g => g.Total)
            .FirstOrDefault();

        if (highestDay is not null && highestDay.Total > 0m)
        {
            insights.Add(Insight.Create(
                userId,
                InsightCodes.HighestSpendDay,
                currentMonthStart,
                now,
                "Tu día de mayor gasto",
                $"El {highestDay.Day:dd/MM} gastaste {Money(highestDay.Total)}.",
                now,
                value: highestDay.Total,
                displayOrder: order++));
        }

        if (incomeTotal > 0m || currentTotal > 0m)
        {
            var net = MoneyMath.Round(incomeTotal - currentTotal);
            insights.Add(Insight.Create(
                userId,
                InsightCodes.IncomeVsExpense,
                currentMonthStart,
                now,
                net >= 0 ? "Vas en positivo" : "Vas en negativo",
                net >= 0
                    ? $"Recibiste {Money(incomeTotal)} y gastaste {Money(currentTotal)}: te quedan {Money(net)}."
                    : $"Gastaste {Money(Math.Abs(net))} más de lo que recibiste este mes.",
                now,
                value: incomeTotal,
                comparisonValue: currentTotal,
                severity: net >= 0 ? InsightSeverity.Positive : InsightSeverity.Attention,
                displayOrder: order++));
        }

        var staleThreshold = now.AddDays(-StaleAccountDays);
        var staleAccounts = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId
                        && !a.IsArchived
                        && a.ConnectionMode == ConnectionMode.ManualImport
                        && (a.LastSyncedAt == null || a.LastSyncedAt < staleThreshold))
            .Select(a => a.Alias)
            .ToListAsync(cancellationToken);

        if (staleAccounts.Count > 0)
        {
            insights.Add(Insight.Create(
                userId,
                InsightCodes.StaleAccount,
                since,
                now,
                "Cuentas por actualizar",
                staleAccounts.Count == 1
                    ? $"{staleAccounts[0]} no se actualiza hace más de una semana."
                    : $"{staleAccounts.Count} cuentas no se actualizan hace más de una semana.",
                now,
                severity: InsightSeverity.Attention,
                displayOrder: order++));
        }

        var existing = await db.Insights.Where(i => i.UserId == userId).ToListAsync(cancellationToken);
        db.Insights.RemoveRange(existing);
        db.Insights.AddRange(insights);
        await db.SaveChangesAsync(cancellationToken);

        return insights;
    }

    private sealed record RecurringCandidate(string Label, decimal TypicalAmount, int Occurrences);

    /// <summary>
    /// A subscription looks like the same merchant charging a near-identical amount
    /// roughly every 28-33 days. Three occurrences is the minimum that separates a
    /// subscription from a coincidence.
    /// </summary>
    private static RecurringCandidate? DetectRecurring(IReadOnlyList<Movement> movements, DateTimeOffset now)
    {
        var groups = movements
            .Where(m => m.Direction == TransactionDirection.Expense && !string.IsNullOrWhiteSpace(m.Merchant))
            .GroupBy(m => m.Merchant!, StringComparer.OrdinalIgnoreCase);

        RecurringCandidate? best = null;

        foreach (var group in groups)
        {
            var ordered = group.OrderBy(m => m.Date).ToList();
            if (ordered.Count < 3)
            {
                continue;
            }

            var median = ordered.Select(m => m.Amount).OrderBy(a => a).ElementAt(ordered.Count / 2);
            if (median <= 0m)
            {
                continue;
            }

            var consistent = ordered.Count(m => Math.Abs(m.Amount - median) <= median * 0.15m);
            if (consistent < 3)
            {
                continue;
            }

            var gaps = new List<double>();
            for (var i = 1; i < ordered.Count; i++)
            {
                gaps.Add((ordered[i].Date - ordered[i - 1].Date).TotalDays);
            }

            var monthly = gaps.Count(g => g is >= 25 and <= 35);
            if (monthly < 2)
            {
                continue;
            }

            if (ordered[^1].Date < now.AddDays(-45))
            {
                continue;
            }

            if (best is null || consistent > best.Occurrences)
            {
                best = new RecurringCandidate(group.Key, MoneyMath.Round(median), consistent);
            }
        }

        return best;
    }

    private static string Money(decimal amount) => $"${amount:N2}";
}
