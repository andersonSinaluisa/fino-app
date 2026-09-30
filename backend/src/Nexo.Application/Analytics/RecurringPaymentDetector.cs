using Nexo.Domain.Transactions;

namespace Nexo.Application.Analytics;

/// <summary>A merchant that behaves like a fixed payment.</summary>
/// <param name="Merchant">Most common spelling, for display.</param>
/// <param name="Key">Normalised merchant -- the identity used everywhere else.</param>
public sealed record RecurringMerchant(
    string Merchant,
    string Key,
    Guid? CategoryId,
    string? CategoryName,
    decimal AverageAmount,
    int Occurrences,
    DateTimeOffset LastSeenAt);

/// <summary>One expense row, already reduced to what recurrence detection needs.</summary>
public sealed record RecurringCandidateRow(string Merchant, decimal Amount, DateTimeOffset TransactionDate, Guid? CategoryId);

/// <summary>
/// The single definition of "pago recurrente": a merchant that showed up in at
/// least two distinct local calendar months of the lookback window, with every
/// amount within 25% of its own average.
///
/// Extracted from AnalyticsService so that the statistics screen ("Pagos
/// recurrentes", fijo vs. variable) and Comprometido ("Próximos pagos") can never
/// disagree about what is recurring.
/// </summary>
public static class RecurringPaymentDetector
{
    /// <summary>A merchant recurs if its amount stays within this fraction of its own average.</summary>
    public const decimal AmountTolerance = 0.25m;

    public const int LookbackMonths = 6;

    /// <param name="rows">Posted/pending expenses, internal transfers already excluded.</param>
    /// <param name="categoryNames">Category lookup for display.</param>
    /// <param name="monthOf">
    /// Maps an instant to its (year, month) in the USER's time zone -- an expense
    /// at 21:00 on 31 ago in Guayaquil is an August payment, even though it is
    /// already September in UTC.
    /// </param>
    public static List<RecurringMerchant> Detect(
        IEnumerable<RecurringCandidateRow> rows,
        IReadOnlyDictionary<Guid, string> categoryNames,
        Func<DateTimeOffset, (int Year, int Month)> monthOf)
    {
        var result = new List<RecurringMerchant>();

        var groups = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Merchant))
            .GroupBy(r => TextNormalizer.NormalizeForMatching(r.Merchant));

        foreach (var group in groups)
        {
            var distinctMonths = group.Select(r => monthOf(r.TransactionDate)).Distinct().Count();
            if (distinctMonths < 2)
            {
                continue;
            }

            var amounts = group.Select(r => r.Amount).ToList();
            var average = amounts.Average();
            if (average <= 0m)
            {
                continue;
            }

            var maxDeviation = amounts.Max(a => Math.Abs(a - average));
            if (maxDeviation / average > AmountTolerance)
            {
                continue;
            }

            var displayName = group
                .GroupBy(r => r.Merchant)
                .OrderByDescending(g => g.Count())
                .First().Key;

            var categoryId = group
                .Where(r => r.CategoryId is not null)
                .GroupBy(r => r.CategoryId!.Value)
                .OrderByDescending(g => g.Count())
                .Select(g => (Guid?)g.Key)
                .FirstOrDefault();

            string? categoryName = null;
            if (categoryId is { } id)
            {
                categoryNames.TryGetValue(id, out categoryName);
            }

            result.Add(new RecurringMerchant(
                displayName,
                group.Key,
                categoryId,
                categoryName,
                average,
                group.Count(),
                group.Max(r => r.TransactionDate)));
        }

        return result;
    }
}
