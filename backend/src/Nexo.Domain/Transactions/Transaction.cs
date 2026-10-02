using Nexo.Domain.Common;
using Nexo.Domain.CreditCards;

namespace Nexo.Domain.Transactions;

/// <summary>
/// The canonical movement. Whatever the channel — CSV, XLSX, email, API or webhook —
/// everything ends up here, in exactly this shape.
/// </summary>
public sealed class Transaction : Entity, IUserOwned
{
    /// <summary>A division needs at least this many parts; one part is just a category.</summary>
    public const int MinimumSplitParts = 2;

    public const int MaximumSplitParts = 20;

    private readonly List<TransactionSplit> _splits = [];

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

    /// <summary>
    /// Movimientos divididos: true when the movement is distributed across
    /// <see cref="Splits"/>. While true, <see cref="CategoryId"/> is null and every
    /// per-category figure (breakdown, statistics, budgets, filters) reads the splits
    /// instead -- never both, so nothing is counted twice. The movement's own
    /// amount, date, description, reference, account, import and fingerprint are
    /// never altered by splitting.
    /// </summary>
    public bool IsSplit { get; private set; }

    /// <summary>
    /// Optimistic-concurrency token for the division: bumped on every change to the
    /// splits, so two simultaneous edits cannot both win and leave $270 distributed
    /// over a $220 movement.
    /// </summary>
    public int SplitVersion { get; private set; }

    public IReadOnlyCollection<TransactionSplit> Splits => _splits;

    /// <summary>
    /// Tarjetas de crédito: what this movement is on a card (compra, pago,
    /// devolución, interés...). Null on every non-card account. See
    /// <see cref="CreditCardMovementRules"/> for what each type means.
    /// </summary>
    public CreditCardMovementType? CardMovementType { get; private set; }

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

        if (IsSplit && magnitude != Amount)
        {
            throw new DomainException(
                "transaction_split",
                "Este movimiento está dividido. Edita o quita la división antes de cambiar el monto.");
        }

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
        if (CategoryManuallySet || IsSplit)
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
        EnsureNotSplit();
        CategoryId = null;
        CategoryManuallySet = true;
        CategorySource = CategorySource.Uncategorized;
        CategorizationRuleId = null;
        Stamp(now);
    }

    public void SetCategoryManually(Guid categoryId, DateTimeOffset now)
    {
        EnsureNotSplit();
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
        // A card payment, cash advance or adjustment stays neutral even without its
        // other leg: paying the card is never income, whether or not the bank side
        // of the payment is in Fino.
        IsInternalTransfer = CardMovementType is { } type && CreditCardMovementRules.IsNeutral(type);
        InternalTransferLinkId = null;
        Stamp(now);
    }

    /// <summary>
    /// Tarjetas de crédito: says what this movement is on its card. The type must
    /// agree with the direction (a purchase is a charge, a payment a credit). Neutral
    /// types (pago, avance, ajuste) are excluded from income and spending through
    /// <see cref="IsInternalTransfer"/> -- the same flag every report already
    /// honours -- so no report needs a second exclusion rule.
    /// </summary>
    public void ClassifyAsCardMovement(CreditCardMovementType type, DateTimeOffset now)
    {
        CreditCardMovementRules.EnsureConsistent(type, Direction);

        if (IsSplit && !CreditCardMovementRules.CanBeSplit(type))
        {
            throw new DomainException(
                "transaction_split",
                "Este movimiento está dividido en categorías. Quita la división antes de marcarlo como pago, avance o ajuste.");
        }

        var neutral = CreditCardMovementRules.IsNeutral(type);
        if (!neutral && InternalTransferLinkId is not null)
        {
            throw new DomainException(
                "card_movement_linked",
                "Este movimiento está vinculado como pago desde otra de tus cuentas. Desvincúlalo antes de cambiar su tipo.");
        }

        CardMovementType = type;
        IsInternalTransfer = neutral || InternalTransferLinkId is not null;
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
        if (IsSplit && authoritative.Amount != Amount)
        {
            RebalanceSplits(authoritative.Amount, now);
        }

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

    /// <summary>
    /// Divides the movement. The whole division is replaced atomically: validated
    /// here, persisted by one SaveChanges. Rules:
    /// <list type="bullet">
    /// <item>At least <see cref="MinimumSplitParts"/> parts (one part is just a category).</item>
    /// <item>Every part &gt; 0, at most 2 decimals.</item>
    /// <item>No category twice (null = "Sin categoría" counts as one category).</item>
    /// <item>The parts add up EXACTLY to <see cref="Amount"/>: "Falta asignar" and
    /// "Te pasaste" are both rejected, so a stored division is always complete.</item>
    /// </list>
    /// </summary>
    public void Split(IReadOnlyList<SplitLine> lines, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (!CreditCardMovementRules.CanBeSplit(CardMovementType))
        {
            throw new DomainException(
                "split_not_spending",
                "Un pago, avance o ajuste de tarjeta no es un gasto: no se puede dividir en categorías.");
        }

        if (lines.Count < MinimumSplitParts)
        {
            throw new DomainException("split_too_few_parts", "Una división necesita al menos dos partes.");
        }

        if (lines.Count > MaximumSplitParts)
        {
            throw new DomainException("split_too_many_parts", $"Una división puede tener como máximo {MaximumSplitParts} partes.");
        }

        foreach (var line in lines)
        {
            if (line.Amount <= 0m)
            {
                throw new DomainException("split_invalid_amount", "Cada parte debe ser mayor que cero.");
            }

            if (decimal.Round(line.Amount, 2) != line.Amount)
            {
                throw new DomainException("split_invalid_amount", "Cada parte puede tener como máximo dos decimales.");
            }
        }

        if (lines.GroupBy(l => l.CategoryId).Any(g => g.Count() > 1))
        {
            throw new DomainException("split_duplicate_category", "Cada categoría puede aparecer una sola vez en la división.");
        }

        var total = lines.Sum(l => l.Amount);
        if (total < Amount)
        {
            throw new DomainException(
                "split_incomplete",
                $"Falta asignar ${MoneyMath.Round(Amount - total).ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        if (total > Amount)
        {
            throw new DomainException(
                "split_exceeds_total",
                $"Te pasaste por ${MoneyMath.Round(total - Amount).ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        _splits.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            _splits.Add(TransactionSplit.Create(this, lines[i].CategoryId, lines[i].Amount, lines[i].Note, i, now));
        }

        IsSplit = true;
        CategoryId = null;
        CategoryManuallySet = true;
        CategorySource = CategorySource.ManualSplit;
        CategorizationRuleId = null;
        SplitVersion++;
        Stamp(now);
    }

    /// <summary>
    /// "Quitar división": back to one category (or none). The bank movement is not
    /// touched -- only the analytic distribution goes away.
    /// </summary>
    public void RemoveSplit(Guid? categoryId, DateTimeOffset now)
    {
        if (!IsSplit)
        {
            throw new DomainException("transaction_not_split", "Este movimiento no está dividido.");
        }

        _splits.Clear();
        IsSplit = false;
        CategoryId = categoryId;
        CategoryManuallySet = true;
        CategorySource = categoryId is null ? CategorySource.Uncategorized : CategorySource.Manual;
        CategorizationRuleId = null;
        SplitVersion++;
        Stamp(now);
    }

    private void EnsureNotSplit()
    {
        if (IsSplit)
        {
            throw new DomainException(
                "transaction_split",
                "Este movimiento está dividido en varias categorías. Edita la división en lugar de cambiar la categoría.");
        }
    }

    /// <summary>
    /// The authoritative statement row arrived with a slightly different amount than
    /// the email that created this movement. The person's division is kept: the
    /// difference goes to (or comes from) the largest part. Only if that would leave
    /// the part at zero or below does the division collapse into that part's category
    /// -- never silently into "no category".
    /// </summary>
    private void RebalanceSplits(decimal newAmount, DateTimeOffset now)
    {
        var delta = newAmount - Amount;
        var largest = _splits.OrderByDescending(s => s.Amount).ThenBy(s => s.Position).First();
        var adjusted = largest.Amount + delta;

        if (adjusted > 0m)
        {
            largest.ChangeAmount(adjusted, now);
            SplitVersion++;
            return;
        }

        var keep = largest.CategoryId;
        _splits.Clear();
        IsSplit = false;
        CategoryId = keep;
        CategorySource = keep is null ? CategorySource.Uncategorized : CategorySource.Manual;
        SplitVersion++;
    }
}
