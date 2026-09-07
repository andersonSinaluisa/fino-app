namespace Nexo.Application.Analytics;

/// <summary>
/// Every window the statistics dashboard understands. "Custom" is the only one
/// that reads <see cref="AnalyticsQuery.From"/>/<see cref="AnalyticsQuery.To"/> --
/// the rest are computed from <c>now</c> so two people never get different
/// boundaries for "este mes" depending on when they ask.
/// </summary>
public enum AnalyticsPeriod
{
    Month = 0,
    LastMonth = 1,
    Last3Months = 2,
    Last6Months = 3,
    Year = 4,
    Custom = 5,
}

public sealed record AnalyticsQuery
{
    public AnalyticsPeriod Period { get; init; } = AnalyticsPeriod.Month;

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>When set, every figure is scoped to this single account.</summary>
    public Guid? AccountId { get; init; }
}

public sealed record AnalyticsPeriodDto(
    string Period,
    string Label,
    DateTimeOffset From,
    DateTimeOffset To);

/// <summary>
/// The four headline numbers, each compared against an equally-long window
/// immediately before <see cref="AnalyticsPeriodDto.From"/>. The change
/// percentages are null when that previous window has nothing to compare
/// against (division by zero would be meaningless, not zero) -- same
/// convention as <c>MonthComparisonDto</c>.
/// </summary>
public sealed record AnalyticsKpisDto(
    decimal Income,
    decimal Expense,
    decimal Net,
    /// <summary>Net / Income * 100, rounded. Null when there was no income to divide by.</summary>
    decimal? SavingsRatePercent,
    decimal? IncomeChangePercent,
    decimal? ExpenseChangePercent,
    decimal? NetChangePercent);

/// <summary>
/// Entregable "Dashboard de estadísticas": a la Fijo/Variable split, but built
/// from actually-observed recurrence (<see cref="RecurringPaymentDto"/>), not
/// a guess based on the category alone -- a gym membership and a one-off
/// purchase can share a category but not a recurrence pattern.
/// </summary>
public sealed record MoneyFlowDto(
    decimal Income,
    decimal FixedExpense,
    decimal VariableExpense,
    decimal NetSavings,
    decimal? SavingsRatePercent);

public sealed record SeriesPointDto(DateTimeOffset From, DateTimeOffset To, string Label, decimal Income, decimal Expense);

/// <summary>
/// A real, reconstructed balance at a point in time -- current total balance
/// minus every posted/pending movement that happened after that instant.
/// Never a forecast: only points at or before "now" ever appear here.
/// </summary>
public sealed record BalancePointDto(DateTimeOffset AsOf, string Label, decimal Balance);

public sealed record CategoryTrendDto(
    Guid CategoryId,
    string Name,
    string Icon,
    string Color,
    decimal Total,
    decimal Percentage,
    int Count,
    decimal? PreviousTotal,
    decimal? ChangePercent);

/// <summary>
/// A merchant that showed up in at least two of the last six calendar months
/// with a roughly stable amount (within 25%) -- the same signal that decides
/// whether a transaction counts as "fixed" in <see cref="MoneyFlowDto"/>.
/// </summary>
public sealed record RecurringPaymentDto(
    string Merchant,
    string? CategoryName,
    decimal AverageAmount,
    int OccurrencesLast6Months,
    DateTimeOffset LastSeenAt);

public sealed record MerchantRankingDto(string Merchant, decimal Total, int Count);

public sealed record DailySpendDto(DateTimeOffset Date, decimal Total);

public sealed record WeekdayWeekendDto(
    decimal WeekdayTotal,
    decimal WeekdayAveragePerDay,
    decimal WeekendTotal,
    decimal WeekendAveragePerDay);

/// <summary>Always the last six calendar months, regardless of the selected period -- the "tabla de comparación" the mockup shows.</summary>
public sealed record MonthlyHistoryDto(string MonthLabel, decimal Income, decimal Expense, decimal Net);

public sealed record AnalyticsDashboardDto(
    AnalyticsPeriodDto Period,
    AnalyticsKpisDto Kpis,
    MoneyFlowDto MoneyFlow,
    IReadOnlyList<SeriesPointDto> Series,
    IReadOnlyList<BalancePointDto> BalanceEvolution,
    IReadOnlyList<CategoryTrendDto> CategoryBreakdown,
    Guid? SpotlightCategoryId,
    IReadOnlyList<RecurringPaymentDto> RecurringPayments,
    IReadOnlyList<MerchantRankingDto> TopMerchants,
    IReadOnlyList<DailySpendDto> PeakSpendingDays,
    WeekdayWeekendDto WeekdayWeekend,
    IReadOnlyList<MonthlyHistoryDto> MonthlyHistory,
    IReadOnlyList<Insights.InsightDto> Insights);
