using Nexo.Application.Transactions;

namespace Nexo.Application.Budgets;

/// <summary>
/// Create a budget. Only <see cref="Amount"/> is truly required: with no category
/// it becomes the "presupuesto general"; with no period it is monthly and
/// recurring, starting on the 1st of the current month.
/// </summary>
public sealed record CreateBudgetRequest(
    decimal Amount,
    Guid? CategoryId = null,
    string? Name = null,
    string? Period = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    bool? IsRecurring = null,
    bool ReserveFunds = false,
    string? Priority = null);

/// <summary>
/// Full replacement of the editable fields. Period and start date are not
/// editable: changing them would rewrite every past window, so the honest
/// path is a new budget.
/// </summary>
public sealed record UpdateBudgetRequest(
    decimal Amount,
    Guid? CategoryId,
    string? Name,
    DateOnly? EndDate,
    bool ReserveFunds,
    string? Priority,
    bool IsActive = true);

/// <summary>
/// "¿Qué pasa con mi Disponible si guardo esto?" -- same fields as create, plus
/// <see cref="BudgetId"/> when previewing an edit (so the budget's current
/// reserve is replaced, not added twice).
/// </summary>
public sealed record BudgetPreviewRequest(
    decimal Amount,
    Guid? CategoryId = null,
    string? Period = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    bool? IsRecurring = null,
    bool ReserveFunds = false,
    Guid? BudgetId = null);

public sealed record BudgetWindowDto(DateOnly Start, DateOnly End, string Label);

public sealed record BudgetProgressDto(
    decimal Amount,
    decimal Spent,
    decimal Remaining,
    decimal Overspent,
    decimal PercentUsed,
    /// <summary>"Normal" (&lt;70%), "Attention" (70–89), "NearLimit" (90–99), "Exceeded" (≥100).</summary>
    string Level,
    /// <summary>What this budget currently adds to Comprometido (0 unless it reserves, is active and the window is current).</summary>
    decimal Reserved,
    int DaysInWindow,
    int DaysElapsed,
    int DaysRemaining,
    decimal? DailyAllowance,
    decimal? ProjectedSpend,
    bool IsCurrentWindow);

public sealed record BudgetDto(
    Guid Id,
    string Name,
    /// <summary>The CURRENT amount of the definition (a past window may have had another; see Progress.Amount).</summary>
    decimal Amount,
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryIcon,
    string? CategoryColor,
    string Period,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsRecurring,
    bool ReserveFunds,
    string Priority,
    bool IsActive,
    string Currency,
    /// <summary>The window being shown; null when the budget does not apply on the requested date.</summary>
    BudgetWindowDto? Window,
    BudgetProgressDto? Progress,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record BudgetInsightDto(
    string Kind,
    Guid BudgetId,
    string Message,
    string MessageWithoutAmounts);

public sealed record BudgetTotalsDto(
    decimal Budgeted,
    decimal Spent,
    decimal Remaining,
    decimal Reserved,
    int ExceededCount);

/// <summary>GET /budgets: the list and its header totals, for one date (today by default).</summary>
public sealed record BudgetOverviewDto(
    DateOnly Date,
    /// <summary>"Septiembre 2026".</summary>
    string Label,
    bool IsCurrent,
    BudgetTotalsDto Totals,
    IReadOnlyList<BudgetDto> Budgets,
    /// <summary>The single most relevant, Home-worthy insight across active budgets, or null.</summary>
    BudgetInsightDto? TopInsight);

public sealed record BudgetHistoryItemDto(
    BudgetWindowDto Window,
    decimal Amount,
    decimal Spent,
    decimal PercentUsed,
    string Level);

public sealed record BudgetDetailDto(
    BudgetDto Budget,
    IReadOnlyList<BudgetInsightDto> Insights,
    /// <summary>Most recent first, current window included.</summary>
    IReadOnlyList<BudgetHistoryItemDto> History);

public sealed record BudgetMovementsDto(
    BudgetWindowDto? Window,
    /// <summary>Exactly the movements that were summed into Spent -- expenses and refunds.</summary>
    IReadOnlyList<TransactionListItemDto> Items);

public sealed record BudgetPreviewDto(
    decimal CurrentMoney,
    decimal CommittedNow,
    decimal AvailableNow,
    /// <summary>Net change to Comprometido after de-duplication (can be less than the budget's remaining amount).</summary>
    decimal BudgetContribution,
    decimal CommittedAfter,
    decimal AvailableAfter,
    decimal OvercommittedAfter,
    /// <summary>Spending already made in the window for this category -- what the reserve starts from.</summary>
    decimal AlreadySpent);

/// <summary>GET /finance/committed: Comprometido with its full explanation.</summary>
public sealed record CommittedMoneyDto(
    decimal CurrentMoney,
    decimal Committed,
    decimal Available,
    /// <summary>Committed − CurrentMoney when positive: "Tienes $X más comprometidos de lo que tienes".</summary>
    decimal Overcommitted,
    bool IsOvercommitted,
    string Currency,
    IReadOnlyList<CommittedSourceDto> Sources,
    /// <summary>Local days left in the calendar month, today included.</summary>
    int DaysRemainingInMonth,
    /// <summary>Available / DaysRemainingInMonth.</summary>
    decimal? DailyAvailable);

public sealed record CommittedSourceDto(
    /// <summary>"reserved_budget" | "upcoming_payment".</summary>
    string Type,
    string Label,
    string Description,
    decimal Amount,
    IReadOnlyList<CommittedItemDto> Items);

public sealed record CommittedItemDto(
    string Label,
    /// <summary>What counts toward Comprometido.</summary>
    decimal Amount,
    /// <summary>What the source reported before removing overlap.</summary>
    decimal GrossAmount,
    Guid? BudgetId,
    Guid? CategoryId,
    /// <summary>Set when (part of) this payment is already inside a reserved budget.</summary>
    string? CoveredByBudgetName,
    /// <summary>Upcoming payments: the estimated date, one month after it was last seen.</summary>
    DateOnly? ExpectedDate);
