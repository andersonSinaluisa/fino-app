using Nexo.Application.Common;

namespace Nexo.Application.Transactions;

public sealed record TransactionListItemDto(
    Guid Id,
    Guid FinancialAccountId,
    string AccountAlias,
    string ProviderCode,
    string BrandColor,
    DateTimeOffset TransactionDate,
    decimal Amount,
    decimal SignedAmount,
    string Currency,
    string Direction,
    string Description,
    string? Merchant,
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryIcon,
    string? CategoryColor,
    string Status,
    string Source,
    // Entregable 13: true once the person confirmed this is one leg of a
    // transfer between their own accounts -- it moves the account's balance
    // but is excluded from net income/expense totals and insights.
    bool IsInternalTransfer);

public sealed record TransactionDetailDto(
    Guid Id,
    Guid FinancialAccountId,
    string AccountAlias,
    string ProviderCode,
    string ProviderName,
    string? AccountMask,
    DateTimeOffset TransactionDate,
    decimal Amount,
    decimal SignedAmount,
    string Currency,
    string Direction,
    string Description,
    string? Merchant,
    // Entregable 12: null unless the user corrected the merchant by hand.
    // Merchant above already reflects the correction when present -- this is
    // metadata for the UI (show a "corregido" hint, know whether Clear applies).
    string? MerchantCorrected,
    Guid? CategoryId,
    string? CategoryName,
    bool CategoryManuallySet,
    string? ExternalReference,
    string Source,
    string SourceConfidence,
    string Status,
    string? Note,
    Guid? PossibleDuplicateOfId,
    Guid? ImportId,
    DateTimeOffset CreatedAt,
    // Entregable 13: mirrors Transaction.IsInternalTransfer/InternalTransferLinkId.
    bool IsInternalTransfer,
    Guid? InternalTransferLinkId);

/// <summary>Filters accepted by the movements screen. All optional, all combinable.</summary>
public sealed record TransactionFilter
{
    public string? Search { get; init; }

    public Guid? AccountId { get; init; }

    public Guid? CategoryId { get; init; }

    /// <summary>"Income", "Expense" or null for both.</summary>
    public string? Direction { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>The account's institution code (e.g. "PICHINCHA"), for a user with several accounts at the same bank.</summary>
    public string? ProviderCode { get; init; }

    /// <summary>Inclusive lower bound on the movement's magnitude (never signed -- see BuildQuery's note).</summary>
    public decimal? MinAmount { get; init; }

    /// <summary>Inclusive upper bound on the movement's magnitude.</summary>
    public decimal? MaxAmount { get; init; }

    public bool IncludeIgnored { get; init; }

    public PageRequest Page { get; init; } = new();
}

public sealed record UpdateCategoryRequest(Guid CategoryId, bool CreateRule = true);

public sealed record UpdateNoteRequest(string? Note);

/// <summary>Entregable 12: null or blank clears the correction back to the automatic guess.</summary>
public sealed record UpdateMerchantRequest(string? Merchant);

/// <summary>
/// Entregable 27: the user's answer to "¿es un movimiento distinto?" for a
/// transaction flagged NeedsReview. True keeps it as its own movement
/// (back to Posted); false agrees it's the same one already on record
/// (Ignored -- kept for the record, excluded from balances and totals).
/// </summary>
public sealed record ResolveDuplicateRequest(bool KeepAsSeparate);

public sealed record CategoryBreakdownItemDto(
    Guid CategoryId,
    string Name,
    string Icon,
    string Color,
    decimal Total,
    decimal Percentage,
    int Count);

public sealed record MonthlyTotalsDto(decimal Income, decimal Expense, decimal Net);

/// <summary>
/// Entregable 15 ("Dashboard final MVP"): "comparación mensual" as a guaranteed,
/// always-computed dashboard figure -- not the MonthOverMonth insight, which only
/// shows up when it happens to win one of the six "Para ti" card slots. Percent
/// fields are null when there is nothing from the previous month to compare
/// against (division by zero would be meaningless, not zero).
/// </summary>
public sealed record MonthComparisonDto(
    decimal PreviousIncome,
    decimal PreviousExpense,
    decimal? IncomeChangePercent,
    decimal? ExpenseChangePercent);

/// <summary>
/// Entregable 15: one entry per manually-imported account that hasn't synced in
/// <see cref="Nexo.Domain.Accounts.AccountStaleness.ThresholdDays"/> days or more
/// (or never has) -- the same rule the "Cuentas por actualizar" insight uses,
/// surfaced here as its own guaranteed dashboard section instead of competing for
/// an insight slot.
/// </summary>
public sealed record StaleAccountDto(Guid AccountId, string Alias, DateTimeOffset? LastSyncedAt);

public sealed record HomeSummaryDto(
    string Greeting,
    string DisplayName,
    decimal TotalBalance,
    string Currency,
    int AccountCount,
    bool AnyEstimatedBalance,
    MonthlyTotalsDto Month,
    MonthComparisonDto MonthComparison,
    IReadOnlyList<Accounts.AccountDto> Accounts,
    IReadOnlyList<TransactionListItemDto> RecentTransactions,
    IReadOnlyList<CategoryBreakdownItemDto> CategoryBreakdown,
    IReadOnlyList<StaleAccountDto> StaleAccounts,
    IReadOnlyList<Insights.InsightDto> Insights);
