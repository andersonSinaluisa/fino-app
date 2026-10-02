using Nexo.Domain.Accounts;
using Nexo.Domain.Common;

namespace Nexo.Domain.CreditCards;

public enum CardNetwork
{
    Visa = 0,
    Mastercard = 1,
    AmericanExpress = 2,
    Diners = 3,
    Discover = 4,
    Other = 5,
}

/// <summary>
/// Tarjetas de crédito. A card IS a <see cref="FinancialAccount"/> of type
/// <see cref="AccountType.CreditCard"/> -- its movements, imports, deduplication,
/// categories, splits and balance are the ones every account already has. This
/// entity only adds what an account does not know: the credit limit and the billing
/// terms. One per card account.
///
/// <para>Deliberately absent: the full card number, the CVV, the PIN and any bank
/// credential. Name, bank, last four digits, currency and "archived" live on the
/// account (Alias, ProviderCode, Mask, Currency, IsArchived) so they are never
/// stored twice.</para>
///
/// <para>Sign convention: the account balance of a card is NEGATIVE when the
/// person owes money (a purchase is an expense on the card). Current debt is
/// therefore <c>max(−balance, 0)</c>, computed in <see cref="CreditCardCalculator"/>.</para>
/// </summary>
public sealed class CreditCard : Entity, IUserOwned
{
    private CreditCard()
    {
    }

    public Guid UserId { get; private set; }

    public Guid FinancialAccountId { get; private set; }

    public CardNetwork Network { get; private set; }

    public decimal CreditLimit { get; private set; }

    public int ClosingDay { get; private set; }

    public int PaymentDueDay { get; private set; }

    /// <summary>
    /// "Reservar automáticamente dinero para pagar la tarjeta": when true, the
    /// next payment feeds Comprometido. When false the debt is still shown but does
    /// not reduce Disponible.
    /// </summary>
    public bool AutoReserve { get; private set; }

    public static CreditCard Configure(
        FinancialAccount account,
        CardNetwork network,
        decimal creditLimit,
        int closingDay,
        int paymentDueDay,
        bool autoReserve,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.AccountType != AccountType.CreditCard)
        {
            throw new DomainException("not_a_credit_card", "Solo una cuenta de tipo tarjeta de crédito puede tener datos de tarjeta.");
        }

        var card = new CreditCard
        {
            UserId = account.UserId,
            FinancialAccountId = account.Id,
        };
        card.Update(network, creditLimit, closingDay, paymentDueDay, autoReserve, now);
        return card;
    }

    public void Update(
        CardNetwork network,
        decimal creditLimit,
        int closingDay,
        int paymentDueDay,
        bool autoReserve,
        DateTimeOffset now)
    {
        var limit = MoneyMath.Round(creditLimit);
        if (limit <= 0m)
        {
            throw new DomainException("card_invalid_limit", "El cupo de la tarjeta debe ser mayor que cero.");
        }

        if (decimal.Round(creditLimit, 2) != creditLimit)
        {
            throw new DomainException("card_invalid_limit", "El cupo puede tener como máximo dos decimales.");
        }

        BillingCalendar.EnsureValidDays(closingDay, paymentDueDay);

        Network = network;
        CreditLimit = limit;
        ClosingDay = closingDay;
        PaymentDueDay = paymentDueDay;
        AutoReserve = autoReserve;
        Stamp(now);
    }

    /// <summary>The bank's statement says the limit changed (e.g. an increase).</summary>
    public void ChangeLimit(decimal creditLimit, DateTimeOffset now) =>
        Update(Network, creditLimit, ClosingDay, PaymentDueDay, AutoReserve, now);

    public CreditCardTerms Terms => new(CreditLimit, ClosingDay, PaymentDueDay, AutoReserve);
}

public sealed record CreditCardTerms(decimal CreditLimit, int ClosingDay, int PaymentDueDay, bool AutoReserve);
