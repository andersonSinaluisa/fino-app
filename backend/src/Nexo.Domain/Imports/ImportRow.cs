using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Domain.Imports;

public enum ImportRowStatus
{
    /// <summary>Parsed, new, ready to be written on confirm.</summary>
    Ready = 0,

    /// <summary>Fingerprint already present: skipped silently.</summary>
    ExactDuplicate = 1,

    /// <summary>Looks like an existing movement but is not certain: imported for review, never dropped.</summary>
    ProbableDuplicate = 2,

    /// <summary>Row could not be parsed. Kept with its error so the user can see what failed.</summary>
    Invalid = 3,

    /// <summary>Written to the transactions table.</summary>
    Imported = 4,

    /// <summary>Excluded by the user before confirming.</summary>
    Skipped = 5,
}

/// <summary>
/// One line of a statement, kept after the import so a preview can be shown, an
/// import can be audited, and a partial import can be explained row by row.
/// </summary>
public sealed class ImportRow : Entity, IUserOwned
{
    private ImportRow()
    {
    }

    public Guid UserId { get; private set; }

    public Guid ImportId { get; private set; }

    public int RowNumber { get; private set; }

    public ImportRowStatus Status { get; private set; }

    public DuplicateMatchType MatchType { get; private set; }

    public Guid? MatchedTransactionId { get; private set; }

    public double? MatchScore { get; private set; }

    public DateTimeOffset? TransactionDate { get; private set; }

    public decimal? Amount { get; private set; }

    public TransactionDirection? Direction { get; private set; }

    public string? Description { get; private set; }

    public string? ExternalReference { get; private set; }

    public string? Fingerprint { get; private set; }

    public Guid? SuggestedCategoryId { get; private set; }

    /// <summary>
    /// "Categorización personal": carries why <see cref="SuggestedCategoryId"/> is
    /// what it is from the preview stage through to confirm, so the resulting
    /// Transaction is created with the right traceability the first time -- never
    /// recomputed at confirm time, which could pick up rules created in between.
    /// </summary>
    public CategorySource SuggestedCategorySource { get; private set; } = CategorySource.Uncategorized;

    public Guid? SuggestedCategorizationRuleId { get; private set; }

    public Guid? CreatedTransactionId { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Original cells, kept as JSON for troubleshooting a misparsed statement.</summary>
    public string? RawPayload { get; private set; }

    public static ImportRow Parsed(
        Guid userId,
        Guid importId,
        int rowNumber,
        DateTimeOffset transactionDate,
        decimal amount,
        TransactionDirection direction,
        string description,
        string? externalReference,
        string fingerprint,
        string? rawPayload,
        DateTimeOffset now)
    {
        var row = new ImportRow
        {
            UserId = userId,
            ImportId = importId,
            RowNumber = rowNumber,
            Status = ImportRowStatus.Ready,
            MatchType = DuplicateMatchType.NoMatch,
            TransactionDate = transactionDate,
            Amount = MoneyMath.Abs(amount),
            Direction = direction,
            // These values come straight out of a file the user chose, so they are
            // clamped to the column widths here rather than failing the whole
            // insert on one unusually wide row.
            Description = DomainException.RequireText(description, nameof(description), 400),
            ExternalReference = Clamp(externalReference, 64),
            Fingerprint = fingerprint,
            RawPayload = Clamp(rawPayload, 2000),
        };
        row.Stamp(now);
        return row;
    }

    public static ImportRow Rejected(
        Guid userId,
        Guid importId,
        int rowNumber,
        string error,
        string? rawPayload,
        DateTimeOffset now)
    {
        var row = new ImportRow
        {
            UserId = userId,
            ImportId = importId,
            RowNumber = rowNumber,
            Status = ImportRowStatus.Invalid,
            MatchType = DuplicateMatchType.NoMatch,
            Error = DomainException.RequireText(error, nameof(error), 400),
            RawPayload = Clamp(rawPayload, 2000),
        };
        row.Stamp(now);
        return row;
    }

    public void MarkDuplicate(DuplicateMatchType matchType, Guid matchedTransactionId, double score, DateTimeOffset now)
    {
        MatchType = matchType;
        MatchedTransactionId = matchedTransactionId;
        MatchScore = score;
        Status = matchType switch
        {
            DuplicateMatchType.ExactMatch => ImportRowStatus.ExactDuplicate,
            DuplicateMatchType.ProbableMatch => ImportRowStatus.ProbableDuplicate,
            _ => Status,
        };
        Stamp(now);
    }

    public void SuggestCategory(
        Guid? categoryId,
        DateTimeOffset now,
        CategorySource source = CategorySource.Uncategorized,
        Guid? categorizationRuleId = null)
    {
        SuggestedCategoryId = categoryId;
        SuggestedCategorySource = categoryId is null ? CategorySource.Uncategorized : source;
        SuggestedCategorizationRuleId = categoryId is null ? null : categorizationRuleId;
        Stamp(now);
    }

    public void MarkImported(Guid transactionId, DateTimeOffset now)
    {
        CreatedTransactionId = transactionId;
        Status = ImportRowStatus.Imported;
        Stamp(now);
    }

    public void Skip(DateTimeOffset now)
    {
        Status = ImportRowStatus.Skipped;
        Stamp(now);
    }

    private static string? Clamp(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
