using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Application.Transactions;
using Nexo.Domain.Audit;
using Nexo.Domain.Budgets;
using Nexo.Domain.Common;

namespace Nexo.Application.Budgets;

public interface IBudgetService
{
    /// <summary>The budgets that apply on <paramref name="onDate"/> (today by default), with totals and the top insight.</summary>
    Task<BudgetOverviewDto> ListAsync(Guid userId, DateOnly? onDate, CancellationToken cancellationToken);

    Task<BudgetDetailDto> GetAsync(Guid userId, Guid budgetId, DateOnly? onDate, CancellationToken cancellationToken);

    /// <summary>Exactly the movements summed into the window's Spent.</summary>
    Task<BudgetMovementsDto> GetMovementsAsync(Guid userId, Guid budgetId, DateOnly? onDate, CancellationToken cancellationToken);

    Task<BudgetDetailDto> CreateAsync(Guid userId, CreateBudgetRequest request, CancellationToken cancellationToken);

    Task<BudgetDetailDto> UpdateAsync(Guid userId, Guid budgetId, UpdateBudgetRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, Guid budgetId, CancellationToken cancellationToken);
}

/// <summary>
/// Presupuestos. Stores only the plan (<see cref="Budget"/>); every figure shown
/// -- spent, remaining, reserved, level, insights -- is evaluated on read from the
/// movements through <see cref="BudgetLedger"/>, so creating, editing or deleting a
/// movement is reflected on the next read with nothing to keep in sync.
/// </summary>
public sealed class BudgetService(
    INexoDbContext db,
    IClock clock,
    BudgetLedgerFactory ledgers,
    ITransactionService transactions) : IBudgetService
{
    /// <summary>How many windows the detail screen's history shows, current included.</summary>
    public const int HistoryWindows = 6;

    public async Task<BudgetOverviewDto> ListAsync(Guid userId, DateOnly? onDate, CancellationToken cancellationToken)
    {
        var ledger = await ledgers.OpenAsync(userId, cancellationToken);
        var day = onDate ?? ledger.Today;

        var applicable = ledger.Budgets
            .Select(b => (Budget: b, Window: b.WindowContaining(day)))
            .Where(x => x.Window is not null)
            .Select(x => (x.Budget, Window: x.Window!.Value))
            .ToList();

        IReadOnlyList<SpendingRow> rows = applicable.Count == 0
            ? []
            : await ledger.LoadRowsAsync(
                applicable.Min(x => x.Window.Start),
                applicable.Max(x => x.Window.End),
                cancellationToken);

        var evaluated = applicable
            .Select(x => (x.Budget, x.Window, Result: ledger.Evaluate(x.Budget, x.Window, rows)))
            .OrderByDescending(x => x.Budget.IsActive)
            .ThenBy(x => x.Budget.Priority)
            .ThenBy(x => x.Budget.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var active = evaluated.Where(x => x.Budget.IsActive).ToList();
        var totals = new BudgetTotalsDto(
            MoneyMath.Round(active.Sum(x => x.Result.Progress.Amount)),
            MoneyMath.Round(active.Sum(x => x.Result.Progress.Spent)),
            MoneyMath.Round(active.Sum(x => x.Result.Progress.Remaining)),
            MoneyMath.Round(active.Sum(x => x.Result.Progress.Reserved)),
            active.Count(x => x.Result.Progress.Level == BudgetUsageLevel.Exceeded));

        // Max. one budget insight for Home, and only one that is worth the space.
        var topInsight = active
            .SelectMany(x => BudgetInsightRules.For(
                x.Budget.Id,
                x.Budget.Name,
                x.Budget.Priority,
                x.Result.Progress,
                previousSpentToSameDay: null,
                BudgetInsightRules.PeriodNoun(x.Budget.Period)))
            .Where(i => i.IsHomeWorthy)
            .OrderByDescending(i => i.Relevance)
            .Select(ToDto)
            .FirstOrDefault();

        return new BudgetOverviewDto(
            day,
            BudgetLedger.MonthLabel(day),
            day == ledger.Today,
            totals,
            evaluated.Select(x => Map(x.Budget, ledger, x.Window, x.Result.Progress)).ToList(),
            topInsight);
    }

    public async Task<BudgetDetailDto> GetAsync(Guid userId, Guid budgetId, DateOnly? onDate, CancellationToken cancellationToken)
    {
        var ledger = await ledgers.OpenAsync(userId, cancellationToken);
        var budget = ledger.Budgets.FirstOrDefault(b => b.Id == budgetId)
                     ?? throw new NotFoundException("Budget", budgetId);

        return await BuildDetailAsync(ledger, budget, onDate ?? ledger.Today, cancellationToken);
    }

    public async Task<BudgetMovementsDto> GetMovementsAsync(Guid userId, Guid budgetId, DateOnly? onDate, CancellationToken cancellationToken)
    {
        var ledger = await ledgers.OpenAsync(userId, cancellationToken);
        var budget = ledger.Budgets.FirstOrDefault(b => b.Id == budgetId)
                     ?? throw new NotFoundException("Budget", budgetId);

        var window = BudgetLedger.ResolveWindow(budget, onDate ?? ledger.Today);
        var rows = await ledger.LoadRowsAsync(window.Start, window.End, cancellationToken);
        var match = ledger.Match(budget.CategoryId, window, rows);

        var items = await transactions.ListByIdsAsync(userId, match.TransactionIds, cancellationToken);
        return new BudgetMovementsDto(BudgetLedger.ToDto(window), items);
    }

    public async Task<BudgetDetailDto> CreateAsync(Guid userId, CreateBudgetRequest request, CancellationToken cancellationToken)
    {
        var ledger = await ledgers.OpenAsync(userId, cancellationToken);
        var category = ResolveCategory(ledger, request.CategoryId);

        var period = ParsePeriod(request.Period);
        var startDate = request.StartDate ?? DefaultStart(period, ledger.Today);
        if (period == BudgetPeriod.Custom && request.EndDate is null)
        {
            throw ValidationException.For(nameof(request.EndDate), "Elige hasta cuándo dura este presupuesto.");
        }

        var isRecurring = period != BudgetPeriod.Custom && (request.IsRecurring ?? true);
        EnsureNoOverlap(ledger, null, request.CategoryId, category?.Name, period, isRecurring, startDate, request.EndDate);

        var now = clock.UtcNow;
        Budget budget;
        try
        {
            budget = Budget.Create(
                userId,
                DisplayName(request.Name, category?.Name),
                request.CategoryId,
                request.Amount,
                period,
                startDate,
                request.EndDate,
                isRecurring,
                request.ReserveFunds,
                ParsePriority(request.Priority),
                now,
                ledger.User.PreferredCurrency);
        }
        catch (DomainException exception)
        {
            throw ValidationException.For(FieldFor(exception), exception.Message);
        }

        db.Budgets.Add(budget);
        db.AuditLog.Add(AuditLogEntry.Record(userId, AuditActions.BudgetCreated, nameof(Budget), now, budget.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        NexoTelemetry.BudgetsChanged.Add(1, new KeyValuePair<string, object?>("action", "created"));

        return await GetAsync(userId, budget.Id, null, cancellationToken);
    }

    public async Task<BudgetDetailDto> UpdateAsync(Guid userId, Guid budgetId, UpdateBudgetRequest request, CancellationToken cancellationToken)
    {
        var ledger = await ledgers.OpenAsync(userId, cancellationToken);
        var budget = await db.Budgets
                         .Include(b => b.Revisions)
                         .FirstOrDefaultAsync(b => b.Id == budgetId && b.UserId == userId, cancellationToken)
                     ?? throw new NotFoundException("Budget", budgetId);

        var category = ResolveCategory(ledger, request.CategoryId);
        if (request.IsActive)
        {
            EnsureNoOverlap(ledger, budget.Id, request.CategoryId, category?.Name, budget.Period, budget.IsRecurring, budget.StartDate, request.EndDate);
        }

        var now = clock.UtcNow;
        var effectiveFrom = BudgetLedger.ResolveWindow(budget, ledger.Today).Start;

        try
        {
            budget.Update(
                DisplayName(request.Name, category?.Name),
                request.CategoryId,
                request.Amount,
                request.EndDate,
                request.ReserveFunds,
                ParsePriority(request.Priority),
                effectiveFrom,
                now);
        }
        catch (DomainException exception)
        {
            throw ValidationException.For(FieldFor(exception), exception.Message);
        }

        if (request.IsActive && !budget.IsActive)
        {
            budget.Resume(now);
        }
        else if (!request.IsActive && budget.IsActive)
        {
            budget.Pause(now);
        }

        db.AuditLog.Add(AuditLogEntry.Record(userId, AuditActions.BudgetUpdated, nameof(Budget), now, budget.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        NexoTelemetry.BudgetsChanged.Add(1, new KeyValuePair<string, object?>("action", "updated"));

        return await GetAsync(userId, budget.Id, null, cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, Guid budgetId, CancellationToken cancellationToken)
    {
        var budget = await db.Budgets
                         .Include(b => b.Revisions)
                         .FirstOrDefaultAsync(b => b.Id == budgetId && b.UserId == userId, cancellationToken)
                     ?? throw new NotFoundException("Budget", budgetId);

        // Only the plan goes away. Movements are never touched: a budget reads
        // them, it does not own them.
        db.Budgets.Remove(budget);
        db.AuditLog.Add(AuditLogEntry.Record(userId, AuditActions.BudgetDeleted, nameof(Budget), clock.UtcNow, budget.Id.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        NexoTelemetry.BudgetsChanged.Add(1, new KeyValuePair<string, object?>("action", "deleted"));
    }

    internal static BudgetDto Map(Budget budget, BudgetLedger ledger, BudgetWindow? window, BudgetProgress? progress)
    {
        Domain.Categories.Category? category = null;
        if (budget.CategoryId is { } categoryId)
        {
            ledger.Categories.TryGetValue(categoryId, out category);
        }

        return new BudgetDto(
            budget.Id,
            budget.Name,
            budget.Amount,
            budget.CategoryId,
            category?.Name,
            category?.Icon,
            category?.Color,
            budget.Period.ToString(),
            budget.StartDate,
            budget.EndDate,
            budget.IsRecurring,
            budget.ReserveFunds,
            budget.Priority.ToString(),
            budget.IsActive,
            budget.Currency,
            window is { } w ? BudgetLedger.ToDto(w) : null,
            progress is null ? null : ToDto(progress),
            budget.CreatedAt,
            budget.UpdatedAt);
    }

    internal static BudgetProgressDto ToDto(BudgetProgress progress) => new(
        progress.Amount,
        progress.Spent,
        progress.Remaining,
        progress.Overspent,
        progress.PercentUsed,
        progress.Level.ToString(),
        progress.Reserved,
        progress.DaysInWindow,
        progress.DaysElapsed,
        progress.DaysRemaining,
        progress.DailyAllowance,
        progress.ProjectedSpend,
        progress.IsCurrentWindow);

    internal static BudgetInsightDto ToDto(BudgetInsight insight) =>
        new(insight.Kind.ToString(), insight.BudgetId, insight.Message, insight.MessageWithoutAmounts);

    internal static BudgetPeriod ParsePeriod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BudgetPeriod.Monthly;
        }

        return Enum.TryParse<BudgetPeriod>(value, ignoreCase: true, out var period) && Enum.IsDefined(period)
            ? period
            : throw ValidationException.For("Period", "Período no válido. Usa Weekly, Biweekly, Monthly o Custom.");
    }

    internal static BudgetPriority ParsePriority(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BudgetPriority.Important;
        }

        return Enum.TryParse<BudgetPriority>(value, ignoreCase: true, out var priority) && Enum.IsDefined(priority)
            ? priority
            : throw ValidationException.For("Priority", "Prioridad no válida. Usa Essential, Important o Flexible.");
    }

    /// <summary>
    /// Monthly budgets default to the calendar month ("Septiembre"); weekly and
    /// biweekly ones to the Monday of the current week.
    /// </summary>
    internal static DateOnly DefaultStart(BudgetPeriod period, DateOnly today) => period switch
    {
        BudgetPeriod.Monthly => new DateOnly(today.Year, today.Month, 1),
        BudgetPeriod.Weekly or BudgetPeriod.Biweekly => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
        _ => today,
    };

    internal static Domain.Categories.Category? ResolveCategory(BudgetLedger ledger, Guid? categoryId)
    {
        if (categoryId is not { } id)
        {
            return null;
        }

        if (!ledger.Categories.TryGetValue(id, out var category))
        {
            throw new NotFoundException("Category", id);
        }

        if (category.IsIncome)
        {
            throw ValidationException.For("CategoryId", "Los presupuestos son para gastos; elige una categoría de gasto.");
        }

        return category;
    }

    private static async Task<BudgetDetailDto> BuildDetailAsync(BudgetLedger ledger, Budget budget, DateOnly date, CancellationToken cancellationToken)
    {
        var window = BudgetLedger.ResolveWindow(budget, date);

        var windows = new List<BudgetWindow> { window };
        while (windows.Count < HistoryWindows && budget.PreviousWindow(windows[^1]) is { } previous)
        {
            windows.Add(previous);
        }

        var rows = await ledger.LoadRowsAsync(windows[^1].Start, window.End, cancellationToken);
        var (progress, _) = ledger.Evaluate(budget, window, rows);

        var history = windows
            .Select(w =>
            {
                var (p, _) = ledger.Evaluate(budget, w, rows);
                return new BudgetHistoryItemDto(BudgetLedger.ToDto(w), p.Amount, p.Spent, p.PercentUsed, p.Level.ToString());
            })
            .ToList();

        decimal? previousToSameDay = null;
        if (progress.IsCurrentWindow && windows.Count > 1)
        {
            var previousWindow = windows[1];
            var sameDay = previousWindow.Start.AddDays(progress.DaysElapsed - 1);
            var truncated = new BudgetWindow(previousWindow.Start, sameDay < previousWindow.End ? sameDay : previousWindow.End);
            var match = ledger.Match(budget.CategoryId, truncated, rows);
            previousToSameDay = MoneyMath.Round(Math.Max(match.Expenses - match.Refunds, 0m));
        }

        var insights = budget.IsActive
            ? BudgetInsightRules.For(
                    budget.Id,
                    budget.Name,
                    budget.Priority,
                    progress,
                    previousToSameDay,
                    BudgetInsightRules.PeriodNoun(budget.Period))
                .OrderByDescending(i => i.Relevance)
                .Select(ToDto)
                .ToList()
            : [];

        return new BudgetDetailDto(Map(budget, ledger, window, progress), insights, history);
    }

    /// <summary>
    /// "Una categoría no puede tener dos presupuestos activos cuyas fechas se
    /// solapen" -- regardless of period, so a movement can never be consumed by
    /// two active budgets. The same applies to the general (no-category) budget.
    /// </summary>
    private static void EnsureNoOverlap(
        BudgetLedger ledger,
        Guid? selfId,
        Guid? categoryId,
        string? categoryName,
        BudgetPeriod period,
        bool isRecurring,
        DateOnly startDate,
        DateOnly? endDate)
    {
        if (endDate is { } end && end < startDate)
        {
            throw ValidationException.For("EndDate", "La fecha de fin no puede ser anterior a la de inicio.");
        }

        var range = BudgetSchedule.ActiveRange(period, isRecurring, startDate, endDate);
        var conflict = ledger.Budgets
            .Where(b => b.IsActive && b.Id != selfId && b.CategoryId == categoryId)
            .FirstOrDefault(b => BudgetSchedule.RangesOverlap(b.ActiveRange(), range));

        if (conflict is null)
        {
            return;
        }

        var subject = categoryName is null ? "un presupuesto general" : $"un presupuesto para {categoryName}";
        throw new ConflictException(
            $"Ya tienes {subject} activo en esas fechas (\"{conflict.Name}\"). Edítalo o páusalo antes de crear otro.");
    }

    private static string DisplayName(string? requested, string? categoryName)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested.Trim();
        }

        return categoryName ?? "Presupuesto general";
    }

    private static string FieldFor(DomainException exception) => exception.Code switch
    {
        "invalid_budget_amount" => "Amount",
        "invalid_budget_period" => "Period",
        "invalid_currency" => "Currency",
        _ => "Budget",
    };
}
