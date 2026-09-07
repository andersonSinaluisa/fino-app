using Nexo.Domain.Common;

namespace Nexo.Domain.Insights;

public enum InsightSeverity
{
    Neutral = 0,
    Positive = 1,
    Attention = 2,
}

/// <summary>Stable identifiers so the mobile client can render its own copy and icon.</summary>
public static class InsightCodes
{
    public const string MonthlySpend = "MONTHLY_SPEND";
    public const string MonthOverMonth = "MONTH_OVER_MONTH";
    public const string TopCategory = "TOP_CATEGORY";
    public const string CategoryChange = "CATEGORY_CHANGE";
    public const string RecurringSubscription = "RECURRING_SUBSCRIPTION";
    public const string FrequentMerchant = "FREQUENT_MERCHANT";
    public const string HighestSpendDay = "HIGHEST_SPEND_DAY";
    public const string IncomeVsExpense = "INCOME_VS_EXPENSE";
    public const string StaleAccount = "STALE_ACCOUNT";
}

/// <summary>
/// A precomputed, human-readable observation. Insights are derived data: they are
/// recomputed from transactions and can always be thrown away and rebuilt.
/// </summary>
public sealed class Insight : Entity, IUserOwned
{
    private Insight()
    {
    }

    public Guid UserId { get; private set; }

    public string Code { get; private set; } = null!;

    public DateTimeOffset PeriodStart { get; private set; }

    public DateTimeOffset PeriodEnd { get; private set; }

    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    public decimal? Value { get; private set; }

    public decimal? ComparisonValue { get; private set; }

    public decimal? PercentChange { get; private set; }

    public InsightSeverity Severity { get; private set; }

    /// <summary>Optional deep link target, e.g. a category id the client can filter by.</summary>
    public string? ReferenceId { get; private set; }

    public int DisplayOrder { get; private set; }

    /// <summary>
    /// Entregable 16 ("Insights v1"): the instant this observation stops being
    /// worth showing -- "no generar insights irrelevantes" applies over time too,
    /// not only at the moment of computing. Every reader (dashboard, GET
    /// /insights) filters on this in addition to whatever RecomputeAsync already
    /// replaced, so a missed or delayed recompute never leaves a stale insight
    /// on screen. Required, not defaulted: every call site must say on purpose
    /// how long this particular observation stays true.
    /// </summary>
    public DateTimeOffset ValidUntil { get; private set; }

    public static Insight Create(
        Guid userId,
        string code,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        string title,
        string body,
        DateTimeOffset now,
        DateTimeOffset validUntil,
        decimal? value = null,
        decimal? comparisonValue = null,
        decimal? percentChange = null,
        InsightSeverity severity = InsightSeverity.Neutral,
        string? referenceId = null,
        int displayOrder = 0)
    {
        DomainException.Require(validUntil > now, "Un insight no puede nacer ya vencido.");

        var insight = new Insight
        {
            UserId = userId,
            Code = DomainException.RequireText(code, nameof(code), 60),
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Title = DomainException.RequireText(title, nameof(title), 140),
            Body = DomainException.RequireText(body, nameof(body), 400),
            Value = value,
            ComparisonValue = comparisonValue,
            PercentChange = percentChange,
            Severity = severity,
            ReferenceId = referenceId,
            DisplayOrder = displayOrder,
            ValidUntil = validUntil,
        };
        insight.Stamp(now);
        return insight;
    }
}
