using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Budgets;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;
using Nexo.Domain.Users;

namespace Nexo.Application.Budgets;

/// <summary>
/// Opens a <see cref="BudgetLedger"/>: everything needed to evaluate a user's
/// budgets, loaded once per request. Shared by BudgetService (the Presupuestos
/// screens) and CommittedMoneyService (Comprometido), so both evaluate budgets
/// with the same code path.
/// </summary>
public sealed class BudgetLedgerFactory(INexoDbContext db, IClock clock)
{
    public async Task<BudgetLedger> OpenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new NotFoundException("User", userId);

        var budgets = await db.Budgets
            .AsNoTracking()
            .Include(b => b.Revisions)
            .Where(b => b.UserId == userId)
            .ToListAsync(cancellationToken);

        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .ToListAsync(cancellationToken);

        return new BudgetLedger(db, user, clock.UtcNow, budgets, categories);
    }
}

public sealed class BudgetLedger
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-EC");

    private readonly INexoDbContext _db;
    private readonly List<Budget> _activeCategoryBudgets;

    internal BudgetLedger(
        INexoDbContext db,
        User user,
        DateTimeOffset now,
        IReadOnlyList<Budget> budgets,
        IReadOnlyList<Category> categories)
    {
        _db = db;
        User = user;
        Now = now;
        Dates = new StatementDateInterpreter(user.TimeZoneId);
        Today = DateOnly.FromDateTime(Dates.ToLocalDate(now));
        Budgets = budgets;
        Categories = categories.ToDictionary(c => c.Id);
        IncomeCategoryIds = categories.Where(c => c.IsIncome).Select(c => c.Id).ToHashSet();
        _activeCategoryBudgets = budgets.Where(b => b.IsActive && b.CategoryId is not null).ToList();
    }

    public User User { get; }

    public DateTimeOffset Now { get; }

    public StatementDateInterpreter Dates { get; }

    /// <summary>Today in the user's time zone.</summary>
    public DateOnly Today { get; }

    public IReadOnlyList<Budget> Budgets { get; }

    public IReadOnlyDictionary<Guid, Category> Categories { get; }

    public IReadOnlySet<Guid> IncomeCategoryIds { get; }

    /// <summary>
    /// Does an ACTIVE category budget track this category on this date? Used by the
    /// general budget so it never consumes a movement a category budget already did.
    /// </summary>
    public bool IsTrackedByCategoryBudget(Guid categoryId, DateOnly date) =>
        _activeCategoryBudgets.Any(b => b.CategoryId == categoryId && b.WindowContaining(date) is not null);

    /// <summary>
    /// Movements that count as money flow between two local dates (inclusive):
    /// Posted or Pending, never an internal transfer.
    /// </summary>
    public async Task<IReadOnlyList<SpendingRow>> LoadRowsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return [];
        }

        var fromInstant = StartOf(from);
        var toInstant = StartOf(to.AddDays(1));
        var userId = User.Id;

        var raw = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TransactionDate >= fromInstant
                        && t.TransactionDate < toInstant
                        && !t.IsInternalTransfer
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .Select(t => new { t.Id, t.CategoryId, t.Direction, t.Amount, t.TransactionDate })
            .ToListAsync(cancellationToken);

        return raw
            .Select(t => new SpendingRow(t.Id, t.CategoryId, t.Direction, t.Amount, DateOnly.FromDateTime(Dates.ToLocalDate(t.TransactionDate))))
            .ToList();
    }

    public SpendingMatch Match(Guid? categoryId, BudgetWindow window, IEnumerable<SpendingRow> rows) =>
        BudgetSpendingMatcher.Match(categoryId, window, rows, IncomeCategoryIds, IsTrackedByCategoryBudget);

    public (BudgetProgress Progress, SpendingMatch Match) Evaluate(Budget budget, BudgetWindow window, IEnumerable<SpendingRow> rows)
    {
        var match = Match(budget.CategoryId, window, rows);
        var progress = BudgetCalculator.Evaluate(
            budget.AmountFor(window),
            match.Expenses,
            match.Refunds,
            window,
            Today,
            budget.ReserveFunds,
            budget.IsActive);
        return (progress, match);
    }

    /// <summary>
    /// The window to show for a budget on a date: the one containing it, or the
    /// nearest one when the budget does not apply that day (not started yet, or
    /// already finished) so its detail screen always has something real to show.
    /// </summary>
    public static BudgetWindow ResolveWindow(Budget budget, DateOnly date)
    {
        if (budget.WindowContaining(date) is { } window)
        {
            return window;
        }

        var first = BudgetSchedule.FirstWindow(budget.Period, budget.StartDate, budget.EndDate);
        if (date < budget.StartDate || !budget.IsRecurring)
        {
            return first;
        }

        return budget.EndDate is { } end && budget.WindowContaining(end) is { } last ? last : first;
    }

    public DateTimeOffset StartOf(DateOnly date) => Dates.StartOfDay(date.ToDateTime(TimeOnly.MinValue));

    public static string WindowLabel(BudgetWindow window)
    {
        var isCalendarMonth = window.Start.Day == 1
                              && window.End.Year == window.Start.Year
                              && window.End.Month == window.Start.Month
                              && window.End.Day == DateTime.DaysInMonth(window.Start.Year, window.Start.Month);
        if (isCalendarMonth)
        {
            return MonthLabel(window.Start);
        }

        var startMonth = ShortMonth(window.Start);
        var endMonth = ShortMonth(window.End);
        return window.Start.Month == window.End.Month && window.Start.Year == window.End.Year
            ? $"{window.Start.Day} – {window.End.Day} {endMonth}"
            : $"{window.Start.Day} {startMonth} – {window.End.Day} {endMonth}";
    }

    public static string MonthLabel(DateOnly date)
    {
        var text = date.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Spanish);
        return char.ToUpper(text[0], Spanish) + text[1..];
    }

    public static BudgetWindowDto ToDto(BudgetWindow window) => new(window.Start, window.End, WindowLabel(window));

    private static string ShortMonth(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("MMM", Spanish).Replace(".", string.Empty, StringComparison.Ordinal);
}
