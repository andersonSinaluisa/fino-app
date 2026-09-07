using Nexo.Domain.Common;
using Nexo.Domain.Providers;

namespace Nexo.Domain.Accounts;

public enum AccountType
{
    Checking = 0,
    Savings = 1,
    CreditCard = 2,
    Wallet = 3,
    Other = 4,
}

/// <summary>
/// Nexo never claims to know a bank's official balance. A balance is
/// <see cref="Verified"/> only while it is exactly what the user last imported or
/// confirmed; as soon as a movement lands after that anchor it becomes
/// <see cref="Estimated"/> and the UI must say so.
/// </summary>
public enum BalanceType
{
    Estimated = 0,
    Verified = 1,
}

/// <summary>
/// Shared threshold for "this account hasn't synced in a while", used both by
/// <c>InsightEngine</c> (the "Cuentas por actualizar" insight) and
/// <c>TransactionService.GetHomeSummaryAsync</c> (Entregable 15's dedicated
/// "cuentas desactualizadas" dashboard section) -- one number, not two that could
/// silently drift apart.
/// </summary>
public static class AccountStaleness
{
    public const int ThresholdDays = 7;
}

public sealed class FinancialAccount : Entity, IUserOwned
{
    private FinancialAccount()
    {
    }

    public Guid UserId { get; private set; }

    public string ProviderCode { get; private set; } = null!;

    public string Alias { get; private set; } = null!;

    public AccountType AccountType { get; private set; }

    /// <summary>Last digits only — Nexo never stores a full account number.</summary>
    public string? Mask { get; private set; }

    public string Currency { get; private set; } = Common.Currency.Usd;

    public ConnectionMode ConnectionMode { get; private set; }

    public decimal? LastVerifiedBalance { get; private set; }

    public DateTimeOffset? LastVerifiedAt { get; private set; }

    public decimal EstimatedBalance { get; private set; }

    public DateTimeOffset? LastTransactionAt { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    public bool IsArchived { get; private set; }

    public int DisplayOrder { get; private set; }

    public BalanceType BalanceKind =>
        LastVerifiedAt is null
            ? BalanceType.Estimated
            : LastTransactionAt is null || LastTransactionAt <= LastVerifiedAt
                ? BalanceType.Verified
                : BalanceType.Estimated;

    public static FinancialAccount Open(
        Guid userId,
        Provider provider,
        string alias,
        AccountType accountType,
        ConnectionMode connectionMode,
        DateTimeOffset now,
        string? mask = null,
        string? currency = null,
        decimal? openingVerifiedBalance = null,
        int displayOrder = 0)
    {
        if (!provider.Supports(connectionMode))
        {
            throw new DomainException(
                "unsupported_connection_mode",
                $"{provider.Name} does not support {connectionMode} yet.");
        }

        var account = new FinancialAccount
        {
            UserId = userId,
            ProviderCode = provider.Code,
            Alias = DomainException.RequireText(alias, nameof(alias), 80),
            AccountType = accountType,
            ConnectionMode = connectionMode,
            Mask = NormalizeMask(mask),
            Currency = Common.Currency.Normalize(currency),
            DisplayOrder = displayOrder,
        };
        account.Stamp(now);

        if (openingVerifiedBalance is not null)
        {
            account.SetVerifiedBalance(openingVerifiedBalance.Value, now, now);
        }

        return account;
    }

    /// <summary>Anchors the balance to something the user actually saw at the bank.</summary>
    public void SetVerifiedBalance(decimal balance, DateTimeOffset asOf, DateTimeOffset now)
    {
        LastVerifiedBalance = MoneyMath.Round(balance);
        LastVerifiedAt = asOf;
        EstimatedBalance = LastVerifiedBalance.Value;
        if (LastTransactionAt is not null && LastTransactionAt <= asOf)
        {
            // Everything known so far is already reflected in the verified figure.
            LastTransactionAt = asOf;
        }

        Stamp(now);
    }

    /// <summary>
    /// Applies a single movement to the running estimate. Movements dated before or
    /// at the verification anchor are ignored: they are already inside the verified
    /// figure and adding them would double-count.
    /// </summary>
    public void ApplyMovement(decimal signedAmount, DateTimeOffset transactionDate, DateTimeOffset now)
    {
        if (LastVerifiedAt is null || transactionDate > LastVerifiedAt)
        {
            EstimatedBalance = MoneyMath.Round(EstimatedBalance + signedAmount);
        }

        if (LastTransactionAt is null || transactionDate > LastTransactionAt)
        {
            LastTransactionAt = transactionDate;
        }

        Stamp(now);
    }

    /// <summary>
    /// Rebuilds the estimate from the verified anchor plus the supplied net delta.
    /// Used after imports and after a user deletes movements, so the estimate can
    /// never drift away from the stored transactions.
    /// </summary>
    public void RebuildEstimate(decimal netAmountAfterAnchor, DateTimeOffset? lastTransactionAt, DateTimeOffset now)
    {
        var anchor = LastVerifiedBalance ?? 0m;
        EstimatedBalance = MoneyMath.Round(anchor + netAmountAfterAnchor);
        LastTransactionAt = lastTransactionAt ?? LastTransactionAt;
        Stamp(now);
    }

    public void MarkSynced(DateTimeOffset now)
    {
        LastSyncedAt = now;
        Stamp(now);
    }

    public void Rename(string alias, DateTimeOffset now)
    {
        Alias = DomainException.RequireText(alias, nameof(alias), 80);
        Stamp(now);
    }

    public void Archive(DateTimeOffset now)
    {
        IsArchived = true;
        Stamp(now);
    }

    private static string? NormalizeMask(string? mask)
    {
        if (string.IsNullOrWhiteSpace(mask))
        {
            return null;
        }

        var digits = new string(mask.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
        {
            return null;
        }

        return digits.Length <= 4 ? digits : digits[^4..];
    }
}
