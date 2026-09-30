using Nexo.Domain.Common;

namespace Nexo.Domain.Transactions;

/// <summary>
/// One part of a divided movement ("movimiento dividido"). The bank movement
/// (<see cref="Transaction"/>) stays the single source of truth for amount, date,
/// description, reference, account, import and fingerprint; a split only says how
/// much of it belongs to which category.
///
/// <see cref="Amount"/> follows the same convention as <see cref="Transaction.Amount"/>:
/// always a positive magnitude, the sign lives in the parent's
/// <see cref="Transaction.Direction"/>. Works identically for income and expenses.
/// </summary>
public sealed class TransactionSplit : Entity, IUserOwned
{
    public const int NoteMaxLength = 200;

    private TransactionSplit()
    {
    }

    public Guid TransactionId { get; private set; }

    /// <summary>Denormalised from the parent so the per-user query filter applies here too.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Null = "Sin categoría": the part the person chose to leave uncategorised.</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>Positive magnitude, 2 decimals.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Optional per-part description ("Transferencia esposa"). Never replaces the bank description.</summary>
    public string? Note { get; private set; }

    /// <summary>Display order, as the person entered the parts.</summary>
    public int Position { get; private set; }

    internal static TransactionSplit Create(
        Transaction transaction,
        Guid? categoryId,
        decimal amount,
        string? note,
        int position,
        DateTimeOffset now)
    {
        var split = new TransactionSplit
        {
            TransactionId = transaction.Id,
            UserId = transaction.UserId,
            CategoryId = categoryId,
            Amount = amount,
            Note = CleanNote(note),
            Position = position,
        };
        split.Stamp(now);
        return split;
    }

    internal void ChangeAmount(decimal amount, DateTimeOffset now)
    {
        Amount = amount;
        Stamp(now);
    }

    private static string? CleanNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        return trimmed.Length <= NoteMaxLength ? trimmed : trimmed[..NoteMaxLength];
    }
}

/// <summary>What the person asked for: one part of the division, before validation.</summary>
public sealed record SplitLine(Guid? CategoryId, decimal Amount, string? Note = null);
