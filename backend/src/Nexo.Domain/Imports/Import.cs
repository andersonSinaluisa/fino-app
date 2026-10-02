using Nexo.Domain.Common;

namespace Nexo.Domain.Imports;

public enum ImportStatus
{
    /// <summary>File received and stored, nothing parsed yet.</summary>
    Received = 0,

    /// <summary>Parsed and deduplicated; the user is looking at the preview.</summary>
    PreviewReady = 1,

    /// <summary>User confirmed; rows were written as transactions.</summary>
    Completed = 2,

    /// <summary>Parsing failed or the file was rejected.</summary>
    Failed = 3,

    /// <summary>User discarded the preview.</summary>
    Cancelled = 4,
}

/// <summary>
/// One upload of a statement file. Holds the preview totals so the confirm step
/// never has to re-parse, and so the user always confirms exactly what they saw.
/// </summary>
public sealed class Import : Entity, IUserOwned
{
    private Import()
    {
    }

    public Guid UserId { get; private set; }

    public Guid FinancialAccountId { get; private set; }

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long FileSizeBytes { get; private set; }

    /// <summary>SHA-256 of the file. Lets us warn when the very same file is uploaded twice.</summary>
    public string ContentHash { get; private set; } = null!;

    public string? ParserCode { get; private set; }

    /// <summary>
    /// El banco que el parser de verdad reconoció (StatementParseResult.DetectedProviderCode),
    /// no el que la cuenta tenía seleccionado -- null para el parser genérico/mapeo manual,
    /// que no afirman reconocer ningún banco en particular. Onboarding funcional: esto es lo
    /// que permite avisar "este archivo parece ser de otro banco" en vez de confundir a la
    /// persona con una importación que funcionó pero con un `ParserCode` que no esperaba.
    /// </summary>
    public string? DetectedProviderCode { get; private set; }

    public ImportStatus Status { get; private set; } = ImportStatus.Received;

    public int TotalRows { get; private set; }

    public int NewRows { get; private set; }

    public int DuplicateRows { get; private set; }

    public int ProbableDuplicateRows { get; private set; }

    public int InvalidRows { get; private set; }

    public decimal IncomeTotal { get; private set; }

    public decimal ExpenseTotal { get; private set; }

    public DateTimeOffset? PeriodStart { get; private set; }

    public DateTimeOffset? PeriodEnd { get; private set; }

    /// <summary>Closing balance declared by the statement, when the parser finds one.</summary>
    public decimal? DeclaredClosingBalance { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public int ImportedCount { get; private set; }

    // Tarjetas de crédito: the official figures read from a card statement's
    // header (corte, fecha máxima, total a pagar, pago mínimo, cupo). Kept on the
    // import between preview and confirm, and as the trace of where a declared
    // statement came from. All null for bank-account imports.
    public DateOnly? CardPeriodStart { get; private set; }

    public DateOnly? CardClosingDate { get; private set; }

    public DateOnly? CardDueDate { get; private set; }

    public decimal? CardStatementBalance { get; private set; }

    public decimal? CardMinimumPayment { get; private set; }

    public decimal? CardCreditLimit { get; private set; }

    public bool HasCardStatementSummary => CardClosingDate is not null && CardDueDate is not null && CardStatementBalance is not null;

    public static Import Start(
        Guid userId,
        Guid financialAccountId,
        string fileName,
        string contentType,
        long fileSizeBytes,
        string contentHash,
        DateTimeOffset now)
    {
        var import = new Import
        {
            UserId = userId,
            FinancialAccountId = financialAccountId,
            FileName = DomainException.RequireText(fileName, nameof(fileName), 260),
            ContentType = DomainException.RequireText(contentType, nameof(contentType), 120),
            FileSizeBytes = fileSizeBytes,
            ContentHash = DomainException.RequireText(contentHash, nameof(contentHash), 64),
        };
        import.Stamp(now);
        return import;
    }

    public void MarkPreviewReady(
        string parserCode,
        string? detectedProviderCode,
        int totalRows,
        int newRows,
        int duplicateRows,
        int probableDuplicateRows,
        int invalidRows,
        decimal incomeTotal,
        decimal expenseTotal,
        DateTimeOffset? periodStart,
        DateTimeOffset? periodEnd,
        decimal? declaredClosingBalance,
        DateTimeOffset now)
    {
        ParserCode = parserCode;
        DetectedProviderCode = detectedProviderCode;
        TotalRows = totalRows;
        NewRows = newRows;
        DuplicateRows = duplicateRows;
        ProbableDuplicateRows = probableDuplicateRows;
        InvalidRows = invalidRows;
        IncomeTotal = MoneyMath.Round(incomeTotal);
        ExpenseTotal = MoneyMath.Round(expenseTotal);
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        DeclaredClosingBalance = declaredClosingBalance;
        Status = ImportStatus.PreviewReady;
        Stamp(now);
    }

    public void AttachCardStatementSummary(
        DateOnly? periodStart,
        DateOnly? closingDate,
        DateOnly? dueDate,
        decimal? statementBalance,
        decimal? minimumPayment,
        decimal? creditLimit,
        DateTimeOffset now)
    {
        CardPeriodStart = periodStart;
        CardClosingDate = closingDate;
        CardDueDate = dueDate;
        CardStatementBalance = statementBalance is { } balance ? MoneyMath.Round(balance) : null;
        CardMinimumPayment = minimumPayment is { } minimum ? MoneyMath.Round(minimum) : null;
        CardCreditLimit = creditLimit is { } limit ? MoneyMath.Round(limit) : null;
        Stamp(now);
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        Status = ImportStatus.Failed;
        FailureReason = DomainException.RequireText(reason, nameof(reason), 500);
        Stamp(now);
    }

    public void MarkCompleted(int importedCount, DateTimeOffset now)
    {
        if (Status is not ImportStatus.PreviewReady)
        {
            throw new DomainException(
                "import_not_confirmable",
                "Only an import with a ready preview can be confirmed.");
        }

        Status = ImportStatus.Completed;
        ImportedCount = importedCount;
        ConfirmedAt = now;
        Stamp(now);
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is ImportStatus.Completed)
        {
            throw new DomainException("import_already_completed", "A completed import cannot be cancelled.");
        }

        Status = ImportStatus.Cancelled;
        Stamp(now);
    }
}
