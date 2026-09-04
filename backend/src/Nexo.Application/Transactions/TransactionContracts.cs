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
    string Source);

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
    DateTimeOffset CreatedAt);

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

    public bool IncludeIgnored { get; init; }

    public PageRequest Page { get; init; } = new();
}

public sealed record UpdateCategoryRequest(Guid CategoryId, bool CreateRule = true);

public sealed record UpdateNoteRequest(string? Note);

public sealed record CategoryBreakdownItemDto(
    Guid CategoryId,
    string Name,
    string Icon,
    string Color,
    decimal Total,
    decimal Percentage,
    int Count);

public sealed record MonthlyTotalsDto(decimal Income, decimal Expense, decimal Net);

public sealed record HomeSummaryDto(
    string Greeting,
    string DisplayName,
    decimal TotalBalance,
    string Currency,
    int AccountCount,
    bool AnyEstimatedBalance,
    MonthlyTotalsDto Month,
    IReadOnlyList<Accounts.AccountDto> Accounts,
    IReadOnlyList<TransactionListItemDto> RecentTransactions,
    IReadOnlyList<CategoryBreakdownItemDto> CategoryBreakdown,
    IReadOnlyList<Insights.InsightDto> Insights);
