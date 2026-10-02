using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Domain.Accounts;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.CreditCards;
using Nexo.Domain.Transactions;

namespace Nexo.Application.CreditCards;

public interface ICardMovementClassifier
{
    /// <summary>
    /// Gives a movement of a CARD account its type: the requested one, else the type it
    /// already has (if still consistent with its direction), else the default from
    /// <see cref="CreditCardMovementRules.Classify"/>. Movements of any other account are
    /// left untouched. Interest and fees that nobody categorised by hand go to their own
    /// categories (Intereses / Comisiones e impuestos) instead of looking like consumption.
    /// </summary>
    Task ApplyAsync(
        FinancialAccount account,
        Transaction transaction,
        CreditCardMovementType? requested,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>
/// The one place every write path (registro manual, importación, correo, pagos,
/// transferencias) asks "what is this card movement?", so a card movement can never
/// be stored without a type -- and therefore never as income by accident.
/// </summary>
public sealed class CardMovementClassifier(INexoDbContext db) : ICardMovementClassifier
{
    private Dictionary<string, Guid>? _systemCategories;

    public async Task ApplyAsync(
        FinancialAccount account,
        Transaction transaction,
        CreditCardMovementType? requested,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(transaction);

        if (!account.IsLiability)
        {
            if (requested is not null)
            {
                throw new DomainException("not_a_credit_card", "Solo los movimientos de una tarjeta de crédito tienen tipo de tarjeta.");
            }

            return;
        }

        var type = requested
                   ?? (transaction.CardMovementType is { } current
                       && CreditCardMovementRules.RequiredDirection(current) is var required
                       && (required is null || required == transaction.Direction)
                           ? current
                           : CreditCardMovementRules.Classify(transaction.Direction, transaction.Description));

        transaction.ClassifyAsCardMovement(type, now);

        if (CreditCardMovementRules.IsFinancialCharge(type)
            && !transaction.IsSplit
            && transaction.CategorySource is not (CategorySource.Manual or CategorySource.UserRule))
        {
            var code = type == CreditCardMovementType.Interest ? CategoryCodes.Interest : CategoryCodes.Fees;
            var categories = _systemCategories ??= await db.Categories
                .AsNoTracking()
                .Where(c => c.UserId == null)
                .ToDictionaryAsync(c => c.Code, c => c.Id, StringComparer.Ordinal, cancellationToken);

            if (categories.TryGetValue(code, out var categoryId))
            {
                transaction.ApplyAutomaticCategory(categoryId, now, CategorySource.SystemRule);
            }
        }
    }
}
