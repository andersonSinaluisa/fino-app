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
    bool IsInternalTransfer,
    // Movimientos divididos: when true, CategoryId is null and Splits holds the
    // parts. Still ONE movement in every list.
    bool IsSplit,
    IReadOnlyList<TransactionSplitDto> Splits);

/// <summary>One part of a divided movement. Amount is a positive magnitude, like the movement's.</summary>
public sealed record TransactionSplitDto(
    Guid Id,
    Guid? CategoryId,
    string CategoryName,
    string CategoryIcon,
    string CategoryColor,
    decimal Amount,
    string? Note);

/// <summary>
/// PUT /transactions/{id}/splits: the WHOLE division, replaced atomically. Parts must
/// add up exactly to the movement's amount. A null CategoryId is "Sin categoría".
/// </summary>
public sealed record ReplaceSplitsRequest(
    IReadOnlyList<SplitLineRequest> Splits,
    /// <summary>The SplitVersion the client edited; a stale one is rejected with 409.</summary>
    int? ExpectedVersion = null);

public sealed record SplitLineRequest(Guid? CategoryId, decimal Amount, string? Note = null);

public sealed record TransactionSplitsDto(
    Guid TransactionId,
    decimal Amount,
    string Direction,
    bool IsSplit,
    int Version,
    IReadOnlyList<TransactionSplitDto> Splits);

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
    // "Categorización personal": why CategoryId is what it is ("Manual", "UserRule",
    // "SystemRule", "Imported" or "Uncategorized") plus, when a rule is behind it,
    // which one -- lets the UI say "asignada automáticamente según una regla creada
    // por ti" instead of leaving the person to guess (point 11).
    string CategorySource,
    Guid? CategorizationRuleId,
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
    Guid? InternalTransferLinkId,
    // Movimientos divididos.
    bool IsSplit,
    int SplitVersion,
    IReadOnlyList<TransactionSplitDto> Splits,
    // Set only by UpdateCategoryAsync when ApplyToExistingMatches was requested:
    // how many OTHER movements were just recategorised by the same rule (point 7).
    // Null on every other read of a transaction.
    int? RecategorizedCount = null);

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

/// <summary>
/// "Categorización personal". <paramref name="CreateRule"/> mirrors the mobile
/// toggle "aplicar también a movimientos similares" -- true learns/updates a
/// personal rule from this correction so future movements are categorised
/// automatically ("este y futuros"); false only changes this one movement.
/// <paramref name="ApplyToExistingMatches"/> is the extra "este, anteriores y
/// futuros" step: once the rule exists, also recategorise this user's past
/// movements that match it (never one the user already corrected by hand) in the
/// same request, after they confirmed the count from the preview endpoint.
/// </summary>
public sealed record UpdateCategoryRequest(Guid CategoryId, bool CreateRule = true, bool ApplyToExistingMatches = false);

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
