using Nexo.Domain.Common;

namespace Nexo.Domain.Transactions;

/// <summary>
/// The canonical movement. Whatever the channel — CSV, XLSX, email, API or webhook —
/// everything ends up here, in exactly this shape.
/// </summary>
public sealed class Transaction : Entity, IUserOwned
{
    private Transaction()
    {
    }

    public Guid UserId { get; private set; }

    public Guid FinancialAccountId { get; private set; }

    public string ProviderCode { get; private set; } = null!;

    /// <summary>Bank-issued reference/document number when the source provides one.</summary>
    public string? ExternalReference { get; private set; }

    public DateTimeOffset TransactionDate { get; private set; }

    /// <summary>Always positive. The sign lives in <see cref="Direction"/>.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = Common.Currency.Usd;

    public TransactionDirection Direction { get; private set; }

    public string Description { get; private set; } = null!;

    /// <summary>Comparison key derived from the description; also used for merchant grouping.</summary>
    public string NormalizedDescription { get; private set; } = null!;

    public string? Merchant { get; private set; }

    public Guid? CategoryId { get; private set; }

    /// <summary>True once the user has corrected the category by hand; rules must not overwrite it.</summary>
    public bool CategoryManuallySet { get; private set; }

    public string? AccountMask { get; private set; }

    public TransactionSource Source { get; private set; }

    public SourceConfidence SourceConfidence { get; private set; }

    public TransactionStatus Status { get; private set; }

    public string Fingerprint { get; private set; } = null!;

    /// <summary>Set when this row was kept as a probable duplicate of an existing movement.</summary>
    public Guid? PossibleDuplicateOfId { get; private set; }

    public Guid? ImportId { get; private set; }

    public Guid? EmailConnectionId { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Positive for income, negative for expense. Never persisted; derived on read.</summary>
    public decimal SignedAmount => Direction == TransactionDirection.Income ? Amount : -Amount;

    /// <summary>Only posted movements move balances and feed insights.</summary>
    public bool CountsTowardsBalance => Status is TransactionStatus.Posted or TransactionStatus.Pending;

    public static Transaction Create(
        Guid userId,
        Guid financialAccountId,
        string providerCode,
        DateTimeOffset transactionDate,
        decimal amount,
        TransactionDirection direction,
        string description,
        TransactionSource source,
        DateTimeOffset now,
        string? currency = null,
        string? externalReference = null,
        string? merchant = null,
        Guid? categoryId = null,
        string? accountMask = null,
        SourceConfidence confidence = SourceConfidence.High,
        TransactionStatus status = TransactionStatus.Posted,
        Guid? importId = null,
        Guid? emailConnectionId = null)
    {
        var magnitude = MoneyMath.Abs(amount);
        if (magnitude == 0m)
        {
            throw new DomainException("zero_amount", "A movement must have a non-zero amount.");
        }

        var cleanDescription = DomainException.RequireText(description, nameof(description), 400);

        var transaction = new Transaction
        {
            UserId = userId,
            FinancialAccountId = financialAccountId,
            ProviderCode = DomainException.RequireText(providerCode, nameof(providerCode), 40).ToUpperInvariant(),
            TransactionDate = transactionDate.ToUniversalTime(),
            Amount = magnitude,
            Currency = Common.Currency.Normalize(currency),
            Direction = direction,
            Description = cleanDescription,
            NormalizedDescription = TextNormalizer.NormalizeForMatching(cleanDescription),
            Merchant = merchant ?? TextNormalizer.ExtractMerchant(cleanDescription),
            CategoryId = categoryId,
            AccountMask = accountMask,
            Source = source,
            SourceConfidence = confidence,
            Status = status,
            ExternalReference = NormalizeReference(externalReference),
            ImportId = importId,
            EmailConnectionId = emailConnectionId,
        };

        transaction.Fingerprint = TransactionFingerprint.Compute(
            transaction.ProviderCode,
            financialAccountId,
            transaction.ExternalReference,
            transaction.TransactionDate,
            transaction.Amount,
            transaction.Direction,
            transaction.Description);

        transaction.Stamp(now);
        return transaction;
    }

    /// <summary>Bank references are bounded by the schema (64) and come from a file.</summary>
    private static string? NormalizeReference(string? externalReference)
    {
        if (string.IsNullOrWhiteSpace(externalReference))
        {
            return null;
        }

        var trimmed = externalReference.Trim();
        return trimmed.Length <= 64 ? trimmed : trimmed[..64];
    }

    /// <summary>Applied by the rules engine. Never overrides a manual choice.</summary>
    public bool ApplyAutomaticCategory(Guid categoryId, DateTimeOffset now)
    {
        if (CategoryManuallySet)
        {
            return false;
        }

        CategoryId = categoryId;
        Stamp(now);
        return true;
    }

    public void SetCategoryManually(Guid categoryId, DateTimeOffset now)
    {
        CategoryId = categoryId;
        CategoryManuallySet = true;
        Stamp(now);
    }

    public void SetNote(string? note, DateTimeOffset now)
    {
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)];
        Stamp(now);
    }

    public void FlagAsPossibleDuplicate(Guid existingTransactionId, DateTimeOffset now)
    {
        PossibleDuplicateOfId = existingTransactionId;
        Status = TransactionStatus.NeedsReview;
        Stamp(now);
    }

    /// <summary>User reviewed a probable duplicate and confirmed it is a distinct movement.</summary>
    public void ConfirmNotDuplicate(DateTimeOffset now)
    {
        PossibleDuplicateOfId = null;
        Status = TransactionStatus.Posted;
        Stamp(now);
    }

    public void Ignore(DateTimeOffset now)
    {
        Status = TransactionStatus.Ignored;
        Stamp(now);
    }

    /// <summary>
    /// A movement first seen through an email notification is upgraded when the
    /// authoritative statement row arrives: the reference and the exact amount win,
    /// the source becomes the import and confidence becomes High.
    /// </summary>
    public void UpgradeFrom(Transaction authoritative, DateTimeOffset now)
    {
        Amount = authoritative.Amount;
        TransactionDate = authoritative.TransactionDate;
        Description = authoritative.Description;
        NormalizedDescription = authoritative.NormalizedDescription;
        ExternalReference ??= authoritative.ExternalReference;
        Merchant ??= authoritative.Merchant;
        AccountMask ??= authoritative.AccountMask;
        Source = authoritative.Source;
        SourceConfidence = SourceConfidence.High;
        Status = TransactionStatus.Posted;
        ImportId = authoritative.ImportId ?? ImportId;
        Fingerprint = authoritative.Fingerprint;
        Stamp(now);
    }
}
