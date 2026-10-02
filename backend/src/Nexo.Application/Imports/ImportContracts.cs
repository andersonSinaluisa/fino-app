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
    string? Error,
    // Tarjetas de crédito: why a row is left out on purpose (an installment of a
    // purchase already deferred in Fino). Null otherwise.
    string? SkipReason = null);

/// <summary>Tarjetas de crédito: the official figures read from a card statement header.</summary>
public sealed record CardStatementSummaryDto(
    DateOnly? ClosingDate,
    DateOnly? DueDate,
    decimal? StatementBalance,
    decimal? MinimumPayment,
    decimal? CreditLimit);

public sealed record ImportPreviewDto(
    Guid ImportId,
    Guid FinancialAccountId,
    string FileName,
    string? ParserCode,
    // Onboarding funcional: el código de banco (ProviderCodes, sin sufijo de
    // versión) que el parser de verdad detectó -- null para el parser
    // genérico/mapeo manual. Permite al cliente comparar contra la cuenta
    // seleccionada y avisar "este archivo parece ser de otro banco" sin que
    // el cliente tenga que adivinar el mapeo ParserCode -> ProviderCode.
    string? DetectedProviderCode,
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
    IReadOnlyList<ImportPreviewRowDto> Rows,
    // Entregable 10 (Generic CSV mapper): populated only when Status == "Failed"
    // because no parser recognised the file's headers. UnmappedColumns is the
    // file's first sampled row (often the real header row, but not assumed to
    // be one), UnmappedSampleRows the few rows after it, so the client can show
    // a raw grid and let the person assign fecha/descripción/monto/etc. to a
    // column index via POST /imports/manual.
    IReadOnlyList<string>? UnmappedColumns = null,
    IReadOnlyList<IReadOnlyList<string>>? UnmappedSampleRows = null,
    // Tarjetas de crédito: present when the file is a card statement that prints
    // corte / fecha máxima / total / mínimo / cupo. Declared on confirm.
    CardStatementSummaryDto? CardStatement = null);

public sealed record ConfirmImportRequest(
    IReadOnlyList<Guid>? ExcludedRowIds,
    bool ApplyDeclaredClosingBalance = false);

public sealed record ImportResultDto(
    Guid ImportId,
    int ImportedCount,
    int SkippedDuplicates,
    int FlaggedForReview,
    decimal NewEstimatedBalance,
    string BalanceType,
    // Entregable 27 ("Dedup correo/importación"): of SkippedDuplicates, how many
    // were an exact match whose authoritative data (amount, reference,
    // description) was folded into the existing movement -- typically one an
    // earlier email notification created as Pending/Medium-confidence -- via
    // Transaction.UpgradeFrom, rather than a plain no-op skip.
    int UpgradedCount = 0);

/// <summary>
/// One row of the import history screen -- everything Entregable 8 asks it to
/// show (fecha, cuenta, archivo, nuevos, duplicados, errores, estado) without
/// the row-level detail a full <see cref="ImportPreviewDto"/> carries.
/// </summary>
public sealed record ImportSummaryDto(
    Guid ImportId,
    Guid FinancialAccountId,
    string AccountAlias,
    string FileName,
    string Status,
    DateTimeOffset CreatedAt,
    int NewRows,
    int DuplicateRows,
    int ProbableDuplicateRows,
    int InvalidRows,
    int ImportedCount);
