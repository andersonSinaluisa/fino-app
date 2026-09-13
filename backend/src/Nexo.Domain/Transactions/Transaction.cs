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

    /// <summary>
    /// Entregable 12: the user's own correction, kept separate from
    /// <see cref="Merchant"/> (Nexo's automatic guess from the bank
    /// description) and from <see cref="Description"/> (the untouched bank
    /// text) so neither is ever overwritten by an edit.
    /// </summary>
    public string? MerchantCorrected { get; private set; }

    /// <summary>What the UI should show: the user's correction if there is one, else the guess.</summary>
    public string? EffectiveMerchant => string.IsNullOrWhiteSpace(MerchantCorrected) ? Merchant : MerchantCorrected;

    public Guid? CategoryId { get; private set; }

    /// <summary>True once the user has corrected the category by hand; rules must not overwrite it.</summary>
    public bool CategoryManuallySet { get; private set; }

    /// <summary>
    /// "Categorización personal": traceability for why <see cref="CategoryId"/> is
    /// what it is -- a manual choice, a personal rule, a system rule, the generic
    /// fallback bucket, or nothing at all yet.
    /// </summary>
    public CategorySource CategorySource { get; private set; } = CategorySource.Uncategorized;

    /// <summary>Set only when <see cref="CategorySource"/> is <see cref="Transactions.CategorySource.UserRule"/> or <see cref="Transactions.CategorySource.SystemRule"/>.</summary>
    public Guid? CategorizationRuleId { get; private set; }

    public string? AccountMask { get; private set; }

    public TransactionSource Source { get; private set; }

    public SourceConfidence SourceConfidence { get; private set; }

    public TransactionStatus Status { get; private set; }

    public string Fingerprint { get; private set; } = null!;

    /// <summary>Set when this row was kept as a probable duplicate of an existing movement.</summary>
    public Guid? PossibleDuplicateOfId { get; private set; }

    public Guid? ImportId { get; private set; }

    public Guid? EmailConnectionId { get; private set; }

    /// <summary>
    /// Registro rápido de efectivo (§36, "duplicados"): el identificador que el
    /// cliente generó ANTES de mandar la petición. Dos toques rápidos en Guardar
    /// mandan el mismo valor, así que el segundo se resuelve devolviendo el
    /// movimiento ya creado en vez de crear otro. Null para todo lo que no venga
    /// de un registro manual -- imports y correos ya se desduplican por huella.
    /// </summary>
    public Guid? ClientRequestId { get; private set; }

    public string? Note { get; private set; }

    /// <summary>
    /// Entregable 13: true once the person confirmed this movement is one leg of a
    /// transfer between their own accounts. Confirmed transfers still move each
    /// account's balance (the money really did move) but are excluded from net
    /// income/expense totals and insights -- moving your own money is neither
    /// spending nor earning.
    /// </summary>
    public bool IsInternalTransfer { get; private set; }

    /// <summary>The matching movement on the other account, once confirmed.</summary>
    public Guid? InternalTransferLinkId { get; private set; }

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
        Guid? emailConnectionId = null,
        CategorySource categorySource = CategorySource.Uncategorized,
        Guid? categorizationRuleId = null,
        Guid? clientRequestId = null)
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
            CategorySource = categoryId is null ? CategorySource.Uncategorized : categorySource,
            CategorizationRuleId = categoryId is null ? null : categorizationRuleId,
            AccountMask = accountMask,
            Source = source,
            SourceConfidence = confidence,
            Status = status,
            ExternalReference = NormalizeReference(externalReference),
            ImportId = importId,
            EmailConnectionId = emailConnectionId,
            ClientRequestId = clientRequestId,
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

    /// <summary>
    /// Registro rápido de efectivo: corrige un movimiento que la persona escribió
    /// a mano. Solo para <see cref="TransactionSource.Manual"/>: un movimiento que
    /// vino de un banco es un hecho reportado por el banco y nunca se reescribe
    /// (para esos existen la corrección de comercio y la de categoría, que dejan
    /// intacto el dato original). El llamador es responsable de reajustar el saldo
    /// de la cuenta, porque el importe con signo pudo cambiar.
    /// </summary>
    public void UpdateManualDetails(
        decimal amount,
        TransactionDirection direction,
        string description,
        DateTimeOffset transactionDate,
        DateTimeOffset now)
    {
        if (Source != TransactionSource.Manual)
        {
            throw new DomainException(
                "not_manual",
                "Solo se puede editar así un movimiento registrado a mano.");
        }

        var magnitude = MoneyMath.Abs(amount);
        if (magnitude == 0m)
        {
            throw new DomainException("zero_amount", "A movement must have a non-zero amount.");
        }

        var cleanDescription = DomainException.RequireText(description, nameof(description), 400);

        Amount = magnitude;
        Direction = direction;
        Description = cleanDescription;
        NormalizedDescription = TextNormalizer.NormalizeForMatching(cleanDescription);
        // El comercio automático se recalcula solo si la persona no lo corrigió
        // a mano; MerchantCorrected siempre manda (Entregable 12).
        Merchant = TextNormalizer.ExtractMerchant(cleanDescription);
        TransactionDate = transactionDate.ToUniversalTime();

        Fingerprint = TransactionFingerprint.Compute(
            ProviderCode,
            FinancialAccountId,
            ExternalReference,
            TransactionDate,
            Amount,
            Direction,
            Description);

        Stamp(now);
    }

    /// <summary>Applied by the rules engine. Never overrides a manual choice.</summary>
    public bool ApplyAutomaticCategory(
        Guid categoryId,
        DateTimeOffset now,
        CategorySource source = CategorySource.Imported,
        Guid? categorizationRuleId = null)
    {
        if (CategoryManuallySet)
        {
            return false;
        }

        CategoryId = categoryId;
        CategorySource = source;
        CategorizationRuleId = categorizationRuleId;
        Stamp(now);
        return true;
    }

    /// <summary>
    /// Registro rápido de efectivo: deja el movimiento explícitamente sin
    /// categoría. Distinto de "todavía nadie lo miró": la persona borró la
    /// categoría a propósito, así que las reglas tampoco deben volver a ponerle
    /// una. Necesario porque el camino rápido permite guardar sin categorizar.
    /// </summary>
    public void ClearCategoryManually(DateTimeOffset now)
    {
        CategoryId = null;
        CategoryManuallySet = true;
        CategorySource = CategorySource.Uncategorized;
        CategorizationRuleId = null;
        Stamp(now);
    }

    public void SetCategoryManually(Guid categoryId, DateTimeOffset now)
    {
        CategoryId = categoryId;
        CategoryManuallySet = true;
        CategorySource = CategorySource.Manual;
        CategorizationRuleId = null;
        Stamp(now);
    }

    public void SetNote(string? note, DateTimeOffset now)
    {
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)];
        Stamp(now);
    }

    /// <summary>
    /// Entregable 12: a user override for the merchant name -- passing null or
    /// blank clears it, falling back to the automatic guess again. Never
    /// touches <see cref="Merchant"/> or <see cref="Description"/>.
    /// </summary>
    public void CorrectMerchant(string? merchant, DateTimeOffset now)
    {
        MerchantCorrected = string.IsNullOrWhiteSpace(merchant) ? null : merchant.Trim()[..Math.Min(merchant.Trim().Length, 120)];
        Stamp(now);
    }

    /// <summary>
    /// Entregable 13: links this movement to the matching one on another of the
    /// person's own accounts. The caller (InternalTransferService) is responsible
    /// for calling this on both legs, since a transfer is always a pair.
    /// </summary>
    public void MarkAsInternalTransfer(Guid linkedTransactionId, DateTimeOffset now)
    {
        if (linkedTransactionId == Id)
        {
            throw new DomainException("invalid_transfer_link", "A movement cannot be its own transfer pair.");
        }

        IsInternalTransfer = true;
        InternalTransferLinkId = linkedTransactionId;
        Stamp(now);
    }

    /// <summary>Undoes a transfer confirmation, e.g. because it was matched by mistake.</summary>
    public void ClearInternalTransfer(DateTimeOffset now)
    {
        IsInternalTransfer = false;
        InternalTransferLinkId = null;
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
