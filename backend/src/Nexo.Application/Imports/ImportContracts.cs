namespace Nexo.Application.Imports;

public sealed record ImportPreviewRowDto(
    Guid Id,
    int RowNumber,
    DateTimeOffset? TransactionDate,
    decimal? Amount,
    string? Direction,
    string? Description,
    string? ExternalReference,
    string Status,
    string MatchType,
    Guid? MatchedTransactionId,
    Guid? SuggestedCategoryId,
    string? SuggestedCategoryName,
    string? Error);

public sealed record ImportPreviewDto(
    Guid ImportId,
    Guid FinancialAccountId,
    string FileName,
    string? ParserCode,
    string Status,
    int TotalRows,
    int NewRows,
    int DuplicateRows,
    int ProbableDuplicateRows,
    int InvalidRows,
    decimal IncomeTotal,
    decimal ExpenseTotal,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    decimal? DeclaredClosingBalance,
    string? FailureReason,
    // True when an identical file was already imported into this account.
    bool PreviouslyImportedFile,
    IReadOnlyList<ImportPreviewRowDto> Rows);

public sealed record ConfirmImportRequest(
    IReadOnlyList<Guid>? ExcludedRowIds,
    bool ApplyDeclaredClosingBalance = false);

public sealed record ImportResultDto(
    Guid ImportId,
    int ImportedCount,
    int SkippedDuplicates,
    int FlaggedForReview,
    decimal NewEstimatedBalance,
    string BalanceType);
