using Nexo.Domain.Common;

namespace Nexo.Domain.Budgets;

/// <summary>Where a piece of Comprometido comes from.</summary>
public enum CommitmentSource
{
    /// <summary>
    /// A payment Fino has seen recur in previous months (same merchant, stable
    /// amount) that has not happened yet this month. An estimate, always labelled as such.
    /// </summary>
    UpcomingPayment,

    /// <summary>The unspent part of a budget with ReserveFunds = true, in its current window.</summary>
    ReservedBudget,

    /// <summary>
    /// Tarjetas de crédito: the next payment of a card with auto-reserve on (the
    /// pending part of its last statement, or the projected balance of the open
    /// cycle). Never the whole debt: deferred installments of later statements are
    /// not money to set aside today. Never overlaps a budget: the purchases behind
    /// it were already SPENT (and already consumed their budget), so a budget's
    /// reserve only covers what is still to be spent -- different money.
    /// </summary>
    CreditCard,
}

/// <summary>
/// One candidate commitment, before de-duplication.
/// </summary>
/// <param name="Source">What kind of commitment this is.</param>
/// <param name="ReferenceKey">
/// Stable identity of the underlying obligation (budget id, normalised merchant).
/// Two candidates with the same source and key are the same money and count once.
/// </param>
/// <param name="Label">What the person sees ("Alquiler", "Netflix").</param>
/// <param name="CategoryId">Used to detect the same money reported by two sources.</param>
/// <param name="Amount">Positive amount still pending.</param>
/// <param name="ReferenceId">Budget id for reserved budgets, so the UI can open it.</param>
public sealed record CommitmentCandidate(
    CommitmentSource Source,
    string ReferenceKey,
    string Label,
    Guid? CategoryId,
    decimal Amount,
    Guid? ReferenceId = null);

/// <summary>A commitment after de-duplication.</summary>
/// <param name="GrossAmount">What the source reported.</param>
/// <param name="CountedAmount">What actually counts toward Comprometido after removing overlap.</param>
/// <param name="CoveredByReferenceKey">
/// When part (or all) of an upcoming payment is already inside a reserved budget
/// of the same category, the budget that absorbs it -- so the UI can explain
/// "incluido en tu presupuesto Internet" instead of silently dropping it.
/// </param>
public sealed record CommitmentLine(
    CommitmentSource Source,
    string ReferenceKey,
    string Label,
    Guid? CategoryId,
    decimal GrossAmount,
    decimal CountedAmount,
    Guid? ReferenceId,
    string? CoveredByReferenceKey);

public sealed record CommittedMoney(
    /// <summary>Tu dinero.</summary>
    decimal CurrentMoney,
    /// <summary>The REAL committed total -- never capped by the balance.</summary>
    decimal Committed,
    /// <summary>CurrentMoney − Committed, floored at zero.</summary>
    decimal Available,
    /// <summary>How much more is committed than the person actually has. Zero when all is covered.</summary>
    decimal Overcommitted,
    IReadOnlyList<CommitmentLine> Lines)
{
    public bool IsOvercommitted => Overcommitted > 0m;

    public decimal TotalFor(CommitmentSource source) =>
        MoneyMath.Round(Lines.Where(l => l.Source == source).Sum(l => l.CountedAmount));
}

/// <summary>
/// THE single rule for Comprometido and Disponible. Every surface -- Home, the
/// breakdown screen, the budget create/edit preview -- goes through here, so the
/// number can never disagree between screens and the client never re-derives it.
///
/// <para><b>De-duplication.</b> The same money can be reported by two sources: a
/// reserved "Internet $35" budget and the recurring "CNT $35" payment Fino detected
/// in the same category. The rule is: a reserved budget already HAS a destination
/// for its whole category, so a pending payment in that category is absorbed by the
/// budget's reserve first; only the part that does not fit counts on top. This gives
/// max(budget, payment) per category -- never the sum -- and every absorbed amount
/// is reported with <see cref="CommitmentLine.CoveredByReferenceKey"/>.</para>
///
/// <para>Candidates without a category cannot be proven to overlap with anything
/// and are counted as-is. Identical (source, key) candidates are counted once.</para>
/// </summary>
public static class CommittedMoneyCalculator
{
    public static CommittedMoney Calculate(decimal currentMoney, IEnumerable<CommitmentCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var distinct = candidates
            .Where(c => c.Amount > 0m)
            .GroupBy(c => (c.Source, c.ReferenceKey))
            .Select(g => g.First() with { Amount = MoneyMath.Round(g.First().Amount) })
            .ToList();

        var lines = new List<CommitmentLine>();

        // Reserved budgets first, in a stable order, and remember how much each
        // category's reserve can still absorb.
        var capacity = new Dictionary<Guid, List<(string Key, decimal Left)>>();
        foreach (var budget in distinct
                     .Where(c => c.Source == CommitmentSource.ReservedBudget)
                     .OrderBy(c => c.ReferenceKey, StringComparer.Ordinal))
        {
            lines.Add(new CommitmentLine(
                budget.Source, budget.ReferenceKey, budget.Label, budget.CategoryId,
                budget.Amount, budget.Amount, budget.ReferenceId, null));

            if (budget.CategoryId is { } categoryId)
            {
                if (!capacity.TryGetValue(categoryId, out var slots))
                {
                    slots = [];
                    capacity[categoryId] = slots;
                }

                slots.Add((budget.ReferenceKey, budget.Amount));
            }
        }

        foreach (var payment in distinct
                     .Where(c => c.Source == CommitmentSource.UpcomingPayment)
                     .OrderBy(c => c.ReferenceKey, StringComparer.Ordinal))
        {
            var pending = payment.Amount;
            string? coveredBy = null;

            if (payment.CategoryId is { } categoryId && capacity.TryGetValue(categoryId, out var slots))
            {
                for (var i = 0; i < slots.Count && pending > 0m; i++)
                {
                    var (key, left) = slots[i];
                    if (left <= 0m)
                    {
                        continue;
                    }

                    var absorbed = Math.Min(left, pending);
                    slots[i] = (key, left - absorbed);
                    pending -= absorbed;
                    coveredBy ??= key;
                }
            }

            lines.Add(new CommitmentLine(
                payment.Source, payment.ReferenceKey, payment.Label, payment.CategoryId,
                payment.Amount, MoneyMath.Round(pending), payment.ReferenceId, coveredBy));
        }

        // Cards last: each one counts its own next payment, as-is (see CommitmentSource.CreditCard).
        foreach (var card in distinct
                     .Where(c => c.Source == CommitmentSource.CreditCard)
                     .OrderBy(c => c.ReferenceKey, StringComparer.Ordinal))
        {
            lines.Add(new CommitmentLine(
                card.Source, card.ReferenceKey, card.Label, card.CategoryId,
                card.Amount, card.Amount, card.ReferenceId, null));
        }

        var committed = MoneyMath.Round(lines.Sum(l => l.CountedAmount));
        var money = MoneyMath.Round(currentMoney);
        var available = MoneyMath.Round(Math.Max(money - committed, 0m));
        var overcommitted = MoneyMath.Round(Math.Max(committed - Math.Max(money, 0m), 0m));

        return new CommittedMoney(money, committed, available, overcommitted, lines);
    }
}
